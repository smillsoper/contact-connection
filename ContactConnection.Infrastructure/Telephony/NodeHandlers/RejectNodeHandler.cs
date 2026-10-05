using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

public class RejectNodeHandler(ILogger<RejectNodeHandler>? logger = null) : ITelephonyNodeHandler
{
    public string NodeType => "tf_reject";

    public async Task<TelephonyNodeResult> ExecuteAsync(
        JsonObject node, TelephonyFlowContext ctx, CancellationToken ct = default)
    {
        var cause = node["cause"]?.GetValue<string>() ?? "busy";

        // SIP cause codes via Q.850:
        //   17 = User Busy           → SIP 486 Busy Here
        //   19 = No Answer           → SIP 480 Temporarily Unavailable
        //   21 = Call Rejected       → SIP 603 Decline
        int causeCode = cause switch
        {
            "unavailable" => 19,
            "declined"    => 21,
            _             => 17  // busy (default)
        };

        // No ESL on this context (e.g. a trigger_telephony_event branch) — nothing to act on the channel with (S179).
        if (ctx.Esl is null)
            logger?.LogWarning("RejectNodeHandler [{Uuid}]: no ESL connection — can't reject; ending the flow", ctx.ChannelUuid);
        else
            await ctx.Esl.KillChannelAsync(ctx.ChannelUuid, causeCode, ct);
        return new TelephonyNodeResult(null, "rejected");
    }
}
