using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.FlowEngine.NodeHandlers;

/// <summary>
/// Fires a named custom event on the telephony flow bridged to this CRM session, so an agent's
/// script can kick off telephony-side behavior mid-call — e.g. starting a tf_secure_collect card
/// capture without hanging up or transferring. The CRM session and its telephony call always
/// share the same CallRecordId, so the live TelephonyCallSession (and its ChannelUuid) is found by
/// scanning active sessions for that match, then
/// <see cref="ITelephonyFlowEngine.FireEventAsync"/> runs the matching
/// <c>tf_on_custom_event</c> branch (registered under <c>custom:{eventName}</c> — see
/// TelephonyFlowEngine.ScanEventHandlers) in that flow.
///
/// Fire-and-continue: this node always advances to "default" immediately regardless of whether an
/// event handler exists on the other side — the telephony-side branch (e.g. SecureCollectNodeHandler
/// parking the agent leg on hold for a capture, then re-bridging) runs independently. A script
/// author who needs to react to the outcome should have the telephony flow write a result back via
/// a set_variable-equivalent on its side and poll/branch on it, or (future) a webhook/SignalR push;
/// this node does not block waiting for one.
///
/// Node schema:
/// {
///   "type": "trigger_telephony_event",
///   "label": "Capture Card",
///   "eventName": "capture_card",
///   "transitions": { "default": "node_next" }
/// }
/// </summary>
public class TriggerTelephonyEventNodeHandler(
    IVariableResolver resolver,
    ITelephonyCallSessionStore sessionStore,
    ITelephonyFlowEngine telephonyEngine,
    ILogger<TriggerTelephonyEventNodeHandler> logger) : NodeHandlerBase(resolver), INodeHandler
{
    public string NodeType => "trigger_telephony_event";

    public async Task<NodeResult> ExecuteAsync(
        JsonObject node, FlowExecutionContext ctx,
        string? agentInput, string agentTransition, CancellationToken ct = default)
    {
        var eventName = Str(node, "eventName")?.Trim();
        var next = Transition(node, "default");

        if (string.IsNullOrEmpty(eventName))
        {
            logger.LogWarning(
                "TriggerTelephonyEventNodeHandler [{Session}]: no eventName configured — skipping",
                ctx.SessionId);
        }
        else
        {
            var sessions = await sessionStore.GetAllAsync(ct);
            var callSession = sessions.FirstOrDefault(s => s.CallRecordId == ctx.CallRecordId);

            if (callSession is null)
            {
                logger.LogWarning(
                    "TriggerTelephonyEventNodeHandler [{Session}]: no live telephony session for " +
                    "CallRecord {CallRecordId} — call may have already ended",
                    ctx.SessionId, ctx.CallRecordId);
            }
            else
            {
                try
                {
                    var result = await telephonyEngine.FireEventAsync(
                        callSession.ChannelUuid,
                        $"custom:{eventName}",
                        new FireEventContext { AgentId = ctx.AgentId },
                        ct);

                    logger.LogInformation(
                        "TriggerTelephonyEventNodeHandler [{Session}]: fired '{Event}' on channel " +
                        "{Uuid} — handled={Handled}",
                        ctx.SessionId, eventName, callSession.ChannelUuid, result.Handled);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex,
                        "TriggerTelephonyEventNodeHandler [{Session}]: failed to fire '{Event}' on " +
                        "channel {Uuid}",
                        ctx.SessionId, eventName, callSession.ChannelUuid);
                }
            }
        }

        AppendHistory(ctx, node, input: null, transition: next);
        var state = BuildState(ctx, node, resolvedContent: string.Empty);
        return new NodeResult(state, next);
    }
}
