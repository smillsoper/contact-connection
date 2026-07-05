using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

/// <summary>
/// Sends a sequence of DTMF tones on the current call channel.
///
/// Uses FreeSWITCH uuid_send_dtmf which queues the tones asynchronously —
/// the command returns immediately and the flow continues via the default transition.
///
/// Supports {{variable}} substitution from session vars so callers' account numbers,
/// PINs, or other stored values can be dialled automatically (e.g. into conference bridges).
///
/// Invalid characters are stripped before sending. Valid: 0-9 * # A-D w (500ms pause) W (1s pause).
/// </summary>
public class DtmfNodeHandler : ITelephonyNodeHandler
{
    public string NodeType => "tf_dtmf";

    private readonly ILogger<DtmfNodeHandler> _logger;

    public DtmfNodeHandler(ILogger<DtmfNodeHandler> logger) => _logger = logger;

    public async Task<TelephonyNodeResult> ExecuteAsync(
        JsonObject node, TelephonyFlowContext ctx, CancellationToken ct = default)
    {
        if (ctx.Esl is null)
        {
            _logger.LogWarning("DtmfNodeHandler [{Uuid}]: no ESL connection", ctx.ChannelUuid);
            return FollowDefault(node);
        }

        var digits            = node["digits"]?.GetValue<string>() ?? "";
        var durationMs        = node["durationMs"]?.GetValue<int>() ?? 100;
        var interDigitGapMs   = node["interDigitGapMs"]?.GetValue<int>() ?? 50;
        var waitForCompletion = node["waitForCompletion"]?.GetValue<bool>() ?? true;

        // Substitute {{key}} placeholders from session vars
        foreach (var (key, val) in ctx.Vars)
            digits = digits.Replace($"{{{{{key}}}}}", val);

        // Strip anything that isn't a valid FreeSWITCH DTMF character
        var valid = new string(digits.Where(static c =>
            char.IsDigit(c) || c == '*' || c == '#' ||
            (c >= 'A' && c <= 'D') || c == 'w' || c == 'W').ToArray());

        if (!string.IsNullOrEmpty(valid))
        {
            _logger.LogInformation(
                "DtmfNodeHandler [{Uuid}]: sending '{Digits}' tone={Tone}ms gap={Gap}ms wait={Wait}",
                ctx.ChannelUuid, valid, durationMs, interDigitGapMs, waitForCompletion);

            var sendTask = SendDigitsAsync(ctx.Esl, ctx.ChannelUuid, valid, durationMs, interDigitGapMs, ct);
            if (waitForCompletion)
                await sendTask;
        }
        else
        {
            _logger.LogWarning(
                "DtmfNodeHandler [{Uuid}]: no valid DTMF digits after substitution — skipping",
                ctx.ChannelUuid);
        }

        return FollowDefault(node);
    }

    private static async Task SendDigitsAsync(
        IEslCommander esl, string channelUuid, string digits,
        int durationMs, int interDigitGapMs, CancellationToken ct)
    {
        try
        {
            foreach (var c in digits)
            {
                switch (c)
                {
                    case 'w':
                        await Task.Delay(500, ct);
                        break;
                    case 'W':
                        await Task.Delay(1000, ct);
                        break;
                    default:
                        await esl.SendDtmfAsync(channelUuid, c.ToString(), durationMs, ct);
                        await Task.Delay(durationMs + interDigitGapMs, ct);
                        break;
                }
            }
        }
        catch (OperationCanceledException) { /* call ended mid-sequence — stop gracefully */ }
    }

    private static TelephonyNodeResult FollowDefault(JsonObject node)
    {
        var next = node["transitions"]?.AsObject()?["default"]?.GetValue<string>();
        return new TelephonyNodeResult(next, "default");
    }
}
