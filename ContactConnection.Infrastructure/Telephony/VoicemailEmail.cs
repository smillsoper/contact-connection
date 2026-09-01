using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.Telephony;

/// <summary>
/// Pure helpers for the tf_voicemail node's optional email delivery — recipient-list parsing and
/// building the <see cref="EmailMessage"/> from the node's <c>delivery*</c> config, with every
/// text field run through the variable resolver ({{caller.ani}}, {{call_record.*}}, {{flow.*}}, …)
/// exactly like the CRM script node. Separated out so the templating / recipient logic is
/// testable without an ESL connection.
/// </summary>
public static class VoicemailEmail
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

    /// <summary>
    /// Returns the email to send, or null when delivery is disabled on the node or resolves to
    /// no recipients. <paramref name="attachment"/> is the recorded .wav (omitted when the node
    /// has <c>deliveryAttachAudio</c> = false).
    /// </summary>
    public static EmailMessage? Build(
        JsonObject node,
        IVariableResolver resolver,
        VariableContext vars,
        EmailAttachment? attachment)
    {
        if (node["deliveryEmailEnabled"]?.GetValue<bool>() != true)
            return null;

        string R(string? key) => string.IsNullOrEmpty(key) ? string.Empty : resolver.Resolve(key, vars);

        var to  = ParseRecipients(R(node["deliveryEmailTo"]?.GetValue<string>()));
        var cc  = ParseRecipients(R(node["deliveryEmailCc"]?.GetValue<string>()));
        var bcc = ParseRecipients(R(node["deliveryEmailBcc"]?.GetValue<string>()));
        if (to.Count == 0 && cc.Count == 0 && bcc.Count == 0)
            return null;

        var subjectTemplate = node["deliveryEmailSubject"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(subjectTemplate))
            subjectTemplate = "New voicemail from {{caller.phone}}";

        var attach = node["deliveryAttachAudio"]?.GetValue<bool>() ?? true;

        return new EmailMessage
        {
            To          = to,
            Cc          = cc,
            Bcc         = bcc,
            FromName    = string.IsNullOrWhiteSpace(node["deliveryEmailFromName"]?.GetValue<string>())
                            ? null
                            : R(node["deliveryEmailFromName"]!.GetValue<string>()),
            ReplyTo     = string.IsNullOrWhiteSpace(node["deliveryEmailReplyTo"]?.GetValue<string>())
                            ? null
                            : R(node["deliveryEmailReplyTo"]!.GetValue<string>()).Trim(),
            Subject     = R(subjectTemplate),
            HtmlBody    = R(node["deliveryEmailBodyHtml"]?.GetValue<string>()),
            Attachments = attach && attachment is not null ? [attachment] : [],
        };
    }
}
