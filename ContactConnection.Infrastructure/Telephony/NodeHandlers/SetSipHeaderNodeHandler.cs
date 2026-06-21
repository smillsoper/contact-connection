using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

/// <summary>
/// Sets a SIP header on the channel via uuid_setvar so FreeSWITCH includes it
/// in subsequent outgoing SIP messages (e.g. when bridging to an agent).
/// Node data: { "headerName": "X-Contact-ID", "value": "{{custom_ani}}" }
/// </summary>
public class SetSipHeaderNodeHandler : ITelephonyNodeHandler
{
    public string NodeType => "tf_set_sip_header";

    public async Task<TelephonyNodeResult> ExecuteAsync(
        JsonObject node, TelephonyFlowContext ctx, CancellationToken ct = default)
    {
        var headerName = node["headerName"]?.GetValue<string>();
        var rawValue = node["value"]?.GetValue<string>() ?? "";

        if (!string.IsNullOrWhiteSpace(headerName))
        {
            var resolved = TelSetVariableNodeHandler.Resolve(rawValue, ctx);
            await ctx.Esl.SetChannelVarAsync(ctx.ChannelUuid, $"sip_h_{headerName}", resolved, ct);
        }

        var nextNodeId = node["transitions"]?["default"]?.GetValue<string>();
        return new TelephonyNodeResult(nextNodeId, "default");
    }
}
