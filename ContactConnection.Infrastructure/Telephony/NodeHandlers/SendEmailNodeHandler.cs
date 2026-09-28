using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Common;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

/// <summary>
/// tf_send_email — sends a templated email (To/Cc/Bcc/From name/Reply-to/Subject/Body, all
/// {{variable}}-resolved) and continues immediately. The voicemail node's delivery block without
/// the recording: e.g. alert the MOD when a call queues with nobody logged in. Sent in the
/// background so the caller never waits on the email provider; a send failure is logged and the
/// flow continues on "default" regardless.
/// </summary>
public class SendEmailNodeHandler(IEmailService email, ILogger<SendEmailNodeHandler> logger) : ITelephonyNodeHandler
{
    public string NodeType => "tf_send_email";

    public Task<TelephonyNodeResult> ExecuteAsync(JsonObject node, TelephonyFlowContext ctx, CancellationToken ct = default)
    {
        var message = FlowEmail.Compose(
            node, "email", t => TelSetVariableNodeHandler.Resolve(t, ctx), "Call from {{caller.ani}}");
        if (message is not null)
            FlowEmail.SendInBackground(email, message, logger, $"tf_send_email [{ctx.ChannelUuid}]");
        else
            logger.LogWarning("tf_send_email [{Uuid}]: no recipients after resolving — skipped", ctx.ChannelUuid);

        var nextNodeId = node["transitions"]?["default"]?.GetValue<string>();
        return Task.FromResult(new TelephonyNodeResult(nextNodeId, "default"));
    }
}
