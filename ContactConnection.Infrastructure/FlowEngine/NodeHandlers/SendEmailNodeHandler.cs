using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Common;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.FlowEngine.NodeHandlers;

/// <summary>
/// Handles "send_email" nodes — sends a templated email (To/Cc/Bcc/From name/Reply-to/Subject/
/// Body, all {{variable}}-resolved against the call) and advances immediately; transparent to the
/// agent. Not to be confused with the "email" node, which captures an email address from the
/// agent. Sent in the background so the agent's next node isn't held up by the email provider.
///
/// Node schema:
/// {
///   "type": "send_email",
///   "emailTo": "orders@client.com, {{flow.rep_email}}", "emailCc": "", "emailBcc": "",
///   "emailFromName": "", "emailReplyTo": "", "emailSubject": "Order {{flow.order_number}}",
///   "emailBodyHtml": "<p>…</p>",
///   "transitions": { "default": "node_010" }
/// }
/// </summary>
public class SendEmailNodeHandler(IVariableResolver resolver, IEmailService email, ILogger<SendEmailNodeHandler> logger)
    : NodeHandlerBase(resolver), INodeHandler
{
    public string NodeType => "send_email";

    public Task<NodeResult> ExecuteAsync(
        JsonObject node, FlowExecutionContext ctx,
        string? agentInput, string agentTransition, CancellationToken ct = default)
    {
        var varCtx = ctx.ToVariableContext();
        var message = FlowEmail.Compose(node, "email", t => Resolver.Resolve(t, varCtx), "Call {{call_record.id}}");
        if (message is not null)
            FlowEmail.SendInBackground(email, message, logger, $"send_email [{ctx.SessionId}]");
        else
            logger.LogWarning("send_email [{Session}]: no recipients after resolving — skipped", ctx.SessionId);

        var next = Transition(node, agentTransition) ?? Transition(node, "default");
        AppendHistory(ctx, node, input: null, transition: next);
        return Task.FromResult(new NodeResult(BuildState(ctx, node, resolvedContent: string.Empty), next));
    }
}
