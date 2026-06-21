using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

/// <summary>
/// Reads a SIP header value from the incoming INVITE (pre-populated in ChannelVars)
/// and stores it as a named variable for use by subsequent nodes.
/// Node data: { "headerName": "X-Original-ANI", "variableName": "custom_ani" }
/// </summary>
public class GetSipHeaderNodeHandler : ITelephonyNodeHandler
{
    public string NodeType => "tf_get_sip_header";

    public Task<TelephonyNodeResult> ExecuteAsync(
        JsonObject node, TelephonyFlowContext ctx, CancellationToken ct = default)
    {
        var headerName = node["headerName"]?.GetValue<string>();
        var variableName = node["variableName"]?.GetValue<string>();

        if (!string.IsNullOrWhiteSpace(headerName) && !string.IsNullOrWhiteSpace(variableName))
        {
            // Channel vars are stored as-is (e.g. "X-Original-ANI")
            if (ctx.ChannelVars.TryGetValue(headerName, out var headerValue))
                ctx.Vars[variableName] = headerValue;
            else
                ctx.Vars[variableName] = string.Empty;
        }

        var nextNodeId = node["transitions"]?["default"]?.GetValue<string>();
        return Task.FromResult(new TelephonyNodeResult(nextNodeId, "default"));
    }
}
