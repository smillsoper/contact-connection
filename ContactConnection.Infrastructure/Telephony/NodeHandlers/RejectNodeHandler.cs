using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

public class RejectNodeHandler : ITelephonyNodeHandler
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

        await ctx.Esl.KillChannelAsync(ctx.ChannelUuid, causeCode, ct);
        return new TelephonyNodeResult(null, "rejected");
    }
}
