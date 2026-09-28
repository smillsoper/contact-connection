using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Common;

/// <summary>
/// Builds an <see cref="EmailMessage"/> from a flow node's email fields — shared by tf_voicemail's
/// delivery block (prefix "deliveryEmail"), the telephony tf_send_email node and the CRM
/// send_email node (prefix "email"). Fields: {prefix}To/Cc/Bcc/FromName/ReplyTo/Subject/BodyHtml,
/// each run through the caller's resolver ({{variable}} tags) before recipients are split.
/// </summary>
public static class FlowEmail
{
    /// <summary>Splits a "a@x.com, b@y.com; c@z.com" style list into distinct trimmed addresses.</summary>
    public static IReadOnlyList<string> ParseRecipients(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];
        return raw
            .Split([',', ';', '\n', '\r', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Contains('@'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The composed email, or null when it resolves to no recipients.</summary>
    public static EmailMessage? Compose(
        JsonObject node, string prefix, Func<string, string> resolve, string defaultSubject,
        IReadOnlyList<EmailAttachment>? attachments = null)
    {
        string Raw(string field) => node[prefix + field]?.GetValue<string>() ?? string.Empty;
        string R(string field) => Raw(field) is { Length: > 0 } t ? resolve(t) : string.Empty;

        var to  = ParseRecipients(R("To"));
        var cc  = ParseRecipients(R("Cc"));
        var bcc = ParseRecipients(R("Bcc"));
        if (to.Count == 0 && cc.Count == 0 && bcc.Count == 0)
            return null;

        var subjectTemplate = string.IsNullOrWhiteSpace(Raw("Subject")) ? defaultSubject : Raw("Subject");
        var fromName = R("FromName");
        var replyTo  = R("ReplyTo").Trim();

        return new EmailMessage
        {
            To          = to,
            Cc          = cc,
            Bcc         = bcc,
            FromName    = string.IsNullOrWhiteSpace(fromName) ? null : fromName,
            ReplyTo     = string.IsNullOrWhiteSpace(replyTo) ? null : replyTo,
            Subject     = resolve(subjectTemplate),
            HtmlBody    = R("BodyHtml"),
            Attachments = attachments ?? [],
        };
    }

    /// <summary>
    /// Sends without making the caller (a live phone call, or an agent waiting on the next script
    /// node) wait on the email provider. <see cref="IEmailService"/> is a singleton, so running it
    /// off the request is safe; failures are logged, never thrown into the flow.
    /// </summary>
    public static void SendInBackground(IEmailService email, EmailMessage message, ILogger logger, string context) =>
        _ = Task.Run(async () =>
        {
            try { await email.SendAsync(message, CancellationToken.None); }
            catch (Exception ex) { logger.LogError(ex, "{Context}: email send failed", context); }
        });
}
