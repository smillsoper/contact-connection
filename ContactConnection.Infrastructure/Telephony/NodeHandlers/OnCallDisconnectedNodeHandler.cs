using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

/// <summary>
/// Entry point for the call_disconnected event branch.
/// Fires on CHANNEL_HANGUP after the call ends. Use this branch for post-call actions
/// such as disposition updates, follow-up scheduling, or CRM writes.
/// ctx.Vars["hangup_cause"] contains the FreeSWITCH hangup cause string.
/// </summary>
public class OnCallDisconnectedNodeHandler : ITelephonyNodeHandler
{
    public string NodeType => "tf_on_call_disconnected";

    public Task<TelephonyNodeResult> ExecuteAsync(
        JsonObject node, TelephonyFlowContext ctx, CancellationToken ct = default)
    {
        var next = node["transitions"]?["default"]?.GetValue<string>();
        return Task.FromResult(new TelephonyNodeResult(next));
    }
}
