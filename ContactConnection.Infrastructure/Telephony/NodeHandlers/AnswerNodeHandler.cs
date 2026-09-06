using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

public class AnswerNodeHandler : ITelephonyNodeHandler
{
    public string NodeType => "tf_answer";

    public async Task<TelephonyNodeResult> ExecuteAsync(
        JsonObject node, TelephonyFlowContext ctx, CancellationToken ct = default)
    {
        await ctx.Esl.AnswerChannelAsync(ctx.ChannelUuid, ct);
        ctx.Vars["_answered"] = "true";

        // Lead-in silence — right after answering, play a short burst of silence and wait for it
        // to finish before proceeding. This forces RTP to start flowing and primes the far-end
        // jitter buffer, so the first syllable of the next prompt isn't clipped (the single most
        // common IVR defect on PSTN/mobile). Default 300 ms; set leadInSilenceMs to 0 to disable.
        var leadInMs = node["leadInSilenceMs"]?.GetValue<int>() ?? 300;
        if (leadInMs > 0)
        {
            await ctx.Esl.BroadcastAsync(ctx.ChannelUuid, $"silence_stream://{leadInMs},0", ct);
            await Task.Delay(leadInMs + 100, ct);
            ctx.Vars["_media_primed"] = "true";
        }

        var nextNodeId = node["transitions"]?["default"]?.GetValue<string>();
        return new TelephonyNodeResult(nextNodeId);
    }
}
