using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

/// <summary>
/// tf_clear_hot_digit — "Clear DTMF Listener" in the designer. Explicitly disarms a hot-digit
/// listener armed by a tf_ivr_menu(alwaysListen=true) node, for flows that want to turn it off
/// under their own condition rather than relying on the automatic clear triggers (entering a
/// synchronous capture node, or bridging to an agent). A no-op if nothing is armed.
/// </summary>
public class ClearHotDigitListenerNodeHandler : ITelephonyNodeHandler
{
    public string NodeType => "tf_clear_hot_digit";

    private readonly ILogger<ClearHotDigitListenerNodeHandler> _logger;

    public ClearHotDigitListenerNodeHandler(ILogger<ClearHotDigitListenerNodeHandler> logger) => _logger = logger;

    public Task<TelephonyNodeResult> ExecuteAsync(
        JsonObject node, TelephonyFlowContext ctx, CancellationToken ct = default)
    {
        if (ctx.Vars.ContainsKey("_hot_digit_options"))
        {
            _logger.LogInformation("ClearHotDigitListenerNodeHandler [{Uuid}]: listener cleared", ctx.ChannelUuid);
            ctx.RemoveSessionVar("_hot_digit_options");
            ctx.RemoveSessionVar("_hot_digit_node_id");
        }

        var next = node["transitions"]?["default"]?.GetValue<string>();
        return Task.FromResult(new TelephonyNodeResult(next, "default"));
    }
}
