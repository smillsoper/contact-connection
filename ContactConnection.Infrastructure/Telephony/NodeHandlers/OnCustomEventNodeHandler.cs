using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

/// <summary>
/// Entry point for user-defined custom event branches.
/// The node's "eventName" field sets the event name (e.g. "disposition_set").
/// Fire the event via ITelephonyFlowEngine.FireEventAsync(channelUuid, "custom:disposition_set", ...).
/// </summary>
public class OnCustomEventNodeHandler : ITelephonyNodeHandler
{
    public string NodeType => "tf_on_custom_event";

    public Task<TelephonyNodeResult> ExecuteAsync(
        JsonObject node, TelephonyFlowContext ctx, CancellationToken ct = default)
    {
        var next = node["transitions"]?["default"]?.GetValue<string>();
        return Task.FromResult(new TelephonyNodeResult(next));
    }
}
