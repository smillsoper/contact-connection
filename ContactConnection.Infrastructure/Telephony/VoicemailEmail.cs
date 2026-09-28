using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Common;

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
    public static IReadOnlyList<string> ParseRecipients(string? raw) => FlowEmail.ParseRecipients(raw);

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

        var attach = node["deliveryAttachAudio"]?.GetValue<bool>() ?? true;
        return FlowEmail.Compose(
            node, "deliveryEmail", t => resolver.Resolve(t, vars), "New voicemail from {{caller.phone}}",
            attach && attachment is not null ? [attachment] : []);
    }
}
