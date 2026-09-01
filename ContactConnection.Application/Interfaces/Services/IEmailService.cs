namespace ContactConnection.Application.Interfaces.Services;

public interface IEmailService
{
    /// <summary>Simple single-recipient send. Convenience wrapper over <see cref="SendAsync(EmailMessage, CancellationToken)"/>.</summary>
    Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default);

    /// <summary>
    /// Full send — multiple recipients, cc/bcc, an overridable display name on the From address,
    /// a reply-to, and file attachments (used by the tf_voicemail node to deliver the recorded
    /// message as a .wav).
    /// </summary>
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}

/// <summary>
/// A fully-specified outbound email. Recipient lists are already split into individual addresses
/// (the caller resolves any <c>{{variable}}</c> templates and comma-splitting first). An empty
/// <see cref="To"/> with a non-empty <see cref="Bcc"/> is valid.
/// </summary>
public sealed record EmailMessage
{
    public IReadOnlyList<string> To { get; init; } = [];
    public IReadOnlyList<string> Cc { get; init; } = [];
    public IReadOnlyList<string> Bcc { get; init; } = [];

    /// <summary>Display name to put on the From address; the address itself stays the configured sender.</summary>
    public string? FromName { get; init; }
    public string? ReplyTo { get; init; }

    public string Subject { get; init; } = string.Empty;
    public string HtmlBody { get; init; } = string.Empty;

    public IReadOnlyList<EmailAttachment> Attachments { get; init; } = [];

    /// <summary>True when there is at least one deliverable recipient across to/cc/bcc.</summary>
    public bool HasRecipients => To.Count > 0 || Cc.Count > 0 || Bcc.Count > 0;
}

public sealed record EmailAttachment(string FileName, byte[] Content, string ContentType);
