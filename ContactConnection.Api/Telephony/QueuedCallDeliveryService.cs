using System.Text.Json;
using ContactConnection.Api.Hubs;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Api.Telephony;

public sealed record DeliveryResult(bool Success, string? ErrorDetail);

/// <summary>
/// Claims a queued call for a specific agent and delivers it — the core logic behind
/// TelephonyEndpoints.AnswerQueuedCall, extracted so it's callable both from that HTTP endpoint
/// (an agent's manual "Pick Up" click — still the only path for RingAll/RingTopNByProficiency)
/// and, in a later step, from QueuePollingService's server-initiated arbitration loop for
/// RingStrategy.AutoAnswerBestAgent (no HTTP round trip — the system picked the agent, not a
/// click). This extraction changes no behavior for the existing click path; it only relocates it.
///
/// Two delivery paths depending on whether the flow has an agent_selected event branch:
///   No agent_selected branch (simple path): bridge caller → agent immediately, fire
///     "agent_answer", CHANNEL_BRIDGE later pushes the CRM script pop.
///   Has agent_selected branch (whisper path): originate to the agent with auto-answer, park,
///     fire the agent_selected branch (may include tf_whisper/tf_script_pop) — the actual bridge
///     happens later inside TelEndNodeHandler.
/// </summary>
public class QueuedCallDeliveryService(
    ITenantDbContextFactory dbFactory,
    ITelephonyFlowEngine telephonyFlowEngine,
    ITelephonyCallSessionStore sessionStore,
    IFlowEngine flowEngine,
    IHubContext<FlowHub, IFlowHubClient> hub,
    IAgentStateStore stateStore,
    ICallStateHistoryRecorder callStateRecorder,
    IConfiguration config,
    ILogger<QueuedCallDeliveryService> logger,
    ILogger<EslClient> eslLogger)
{
    public async Task<DeliveryResult> DeliverAsync(
        Guid tenantId, string tenantSchema, string tenantSubdomain,
        Guid callRecordId, Guid agentId, CancellationToken ct)
    {
        await using var db = dbFactory.Create(tenantSchema);

        var agent = await db.Agents.FirstOrDefaultAsync(a => a.Id == agentId && a.IsActive, ct);
        if (agent is null) return new DeliveryResult(false, "Agent not found.");
        if (string.IsNullOrEmpty(agent.SipExtension))
            return new DeliveryResult(false, "Agent has no SIP extension configured.");

        var record = await db.CallRecords.FirstOrDefaultAsync(r => r.Id == callRecordId, ct);
        if (record is null) return new DeliveryResult(false, "Call record not found.");
        if (string.IsNullOrEmpty(record.ContactIdExternal))
            return new DeliveryResult(false, "Call record has no associated channel.");

        var callerUuid = record.ContactIdExternal;

        logger.LogInformation(
            "Delivery: call {CallRecordId} → agent {AgentId} (ext {Ext}), callerUuid={CallerUuid}",
            callRecordId, agentId, agent.SipExtension, callerUuid);

        // Assign this agent to the call record and create the interaction
        record.SetAgent(agentId);
        var interaction = record.AddInteraction(InteractionType.CustomerService);
        db.CallInteractions.Add(interaction);
        await db.SaveChangesAsync(ct);

        await callStateRecorder.RecordAsync(
            tenantId, tenantSchema, record.Id,
            CallHistoryState.Routing, record.CampaignId, agentId, detail: null, ct: ct);

        var host = config["FreeSWITCH:Host"] ?? "127.0.0.1";
        var port = int.Parse(config["FreeSWITCH:EslPort"] ?? "8021");
        var pass = config["FreeSWITCH:EslPassword"] ?? "ClueCon";

        await using var esl = new EslClient(eslLogger);
        await esl.ConnectAsync(host, port, pass, ct);

        // Check whether the telephony flow has an agent_selected event branch
        var session          = await sessionStore.GetAsync(callerUuid, ct);
        var hasAgentSelected = session?.EventHandlers.ContainsKey("agent_selected") == true;
        logger.LogInformation(
            "Delivery: call {CallRecordId} path={Path} (session={HasSession})",
            callRecordId, hasAgentSelected ? "whisper/agent_selected" : "simple-bridge", session is not null);

        if (hasAgentSelected)
        {
            // Originate to agent with auto-answer and park — bridge happens later via TelEndNodeHandler
            var (agentUuid, originateError) = await esl.OriginateAndParkAsync(
                agent.SipExtension, tenantSubdomain, record.CallerId ?? "Unknown", ct);

            logger.LogInformation(
                "Delivery: call {CallRecordId} originate → agentUuid={AgentUuid} error={Error}",
                callRecordId, agentUuid, originateError);

            if (agentUuid is null)
            {
                // Undo the interaction — bridge never happened, call stays in queue
                db.CallInteractions.Remove(interaction);
                await db.SaveChangesAsync(ct);
                return new DeliveryResult(false,
                    "Your softphone could not be reached. Make sure the softphone is registered and try again.");
            }

            // Dequeue — prevents QueuePollingService from re-delivering this call
            session!.Vars.Remove("_queued");
            // _assigned_agent_id persists through bridge for hangup cleanup
            session.Vars["_assigned_agent_id"]      = agentId.ToString();
            session.Vars["_agent_uuid"]              = agentUuid;
            session.Vars["_pending_agent_id"]        = agentId.ToString();
            session.Vars["_pending_interaction_id"]  = interaction.Id.ToString();
            await sessionStore.SaveAsync(session, ct);

            // Mark agent as on-call so QueuePollingService skips them + UI updates
            await stateStore.SetAsync(tenantId, agentId, tenantSchema,
                new AgentStateEntry(AgentStateCodes.OnCall, "On Call", null, DateTimeOffset.UtcNow), ct);
            await hub.Clients.Group($"agent:{agentId}").ReceiveAgentStateChange(AgentStateCodes.OnCall, "On Call", null);

            // Store reverse mapping so PLAYBACK_STOP on the agent channel can find the caller session
            await sessionStore.SetKeyAsync($"whisper:{agentUuid}", callerUuid, TimeSpan.FromMinutes(10), ct);

            // Fire the agent_selected branch (may include tf_whisper, tf_script_pop, etc.)
            var agentSelectedResult = await telephonyFlowEngine.FireEventAsync(
                callerUuid, "agent_selected",
                new FireEventContext
                {
                    AgentId       = agentId,
                    InteractionId = interaction.Id,
                    FlowEngine    = flowEngine,
                    Esl           = esl,
                }, ct);

            logger.LogInformation(
                "Delivery: call {CallRecordId} agent_selected fired — crmScriptPop={HasScriptPop}",
                callRecordId, agentSelectedResult.CrmFlowSession is not null);

            // Push CRM script pop immediately if tf_script_pop ran in the agent_selected branch
            // (e.g. before or after whisper so agent has the script while the whisper plays).
            if (agentSelectedResult.CrmFlowSession is not null)
            {
                var sessionJson = JsonSerializer.Serialize(
                    agentSelectedResult.CrmFlowSession,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                await hub.Clients.Group($"agent:{agentId}").ReceiveScriptPop(sessionJson);
            }

            return new DeliveryResult(true, null);
        }
        else
        {
            // Simple path: store pending IDs so CHANNEL_BRIDGE fires agent_answer (same as whisper path)
            if (session is not null)
            {
                session.Vars.Remove("_queued");
                session.Vars["_assigned_agent_id"]      = agentId.ToString();
                session.Vars["_pending_agent_id"]        = agentId.ToString();
                session.Vars["_pending_interaction_id"]  = interaction.Id.ToString();
                await sessionStore.SaveAsync(session, ct);
            }

            await stateStore.SetAsync(tenantId, agentId, tenantSchema,
                new AgentStateEntry(AgentStateCodes.OnCall, "On Call", null, DateTimeOffset.UtcNow), ct);
            await hub.Clients.Group($"agent:{agentId}").ReceiveAgentStateChange(AgentStateCodes.OnCall, "On Call", null);

            logger.LogInformation(
                "Delivery: call {CallRecordId} simple-bridge → BridgeToAgentAsync(caller={CallerUuid}, ext={Ext})",
                callRecordId, callerUuid, agent.SipExtension);
            await esl.BridgeToAgentAsync(callerUuid, agent.SipExtension, tenantSubdomain, record.CallerId ?? "Unknown", ct);

            return new DeliveryResult(true, null);
        }
    }
}
