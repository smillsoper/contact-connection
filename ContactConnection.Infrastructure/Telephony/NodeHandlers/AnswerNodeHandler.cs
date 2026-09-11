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

        // Every inbound leg is bridged and re-bridged via the ESL-driven uuid_bridge API, not the
        // dialplan bridge() app — FreeSWITCH's default bridge-teardown behavior hangs BOTH legs up
        // the instant either one leaves the bridge (e.g. tf_secure_collect uuid_transfer-ing the
        // agent leg to park_with_moh for a mid-call capture). park_after_bridge=true tells the core
        // to park this leg instead of hanging it up when its bridge partner disappears, so a
        // subsequent uuid_transfer back into a controlled extension (or a re-bridge) still finds a
        // live channel. Set once here, before any bridge exists, since channel vars persist across
        // bridges for the life of the call.
        await ctx.Esl.SetChannelVarAsync(ctx.ChannelUuid, "park_after_bridge", "true", ct);

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
