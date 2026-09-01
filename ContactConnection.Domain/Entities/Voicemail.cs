namespace ContactConnection.Domain.Entities;

/// <summary>
/// One recorded caller message captured by a <c>tf_voicemail</c> telephony flow node — after
/// hours, on queue overflow, or any "leave a message" branch. A child of the call record (the
/// Single Record of Truth container); the audio itself lives in blob storage under
/// <see cref="StorageKey"/>. Optionally also delivered by email as an attachment at capture time
/// — <see cref="EmailDeliveryStatus"/> / <see cref="EmailDeliveredTo"/> record that outcome.
/// See ARCHITECTURE.md §14.
/// </summary>
public class Voicemail
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid CallRecordId { get; private set; }
    public Guid CampaignId { get; private set; }

    /// <summary>Caller ANI as presented on the inbound call.</summary>
    public string? CallerId { get; private set; }

    /// <summary>Blob key of the recorded message, e.g. <c>voicemails/{callRecordId}/{id}.wav</c>.</summary>
    public string StorageKey { get; private set; } = string.Empty;
    public int DurationSeconds { get; private set; }

    public string Status { get; private set; } = VoicemailStatus.New;

    /// <summary>Populated by a later transcription pass; null until then.</summary>
    public string? Transcription { get; private set; }

    // ── Email delivery (optional, decided by the node config at capture time) ──
    /// <summary>null = not attempted; else <see cref="VoicemailEmailStatus"/>.</summary>
    public string? EmailDeliveryStatus { get; private set; }
    /// <summary>Comma-joined recipient list actually used (to + cc + bcc), for the audit trail.</summary>
    public string? EmailDeliveredTo { get; private set; }
    public string? EmailDeliveryError { get; private set; }
    public DateTimeOffset? EmailDeliveredAt { get; private set; }

    // ── Review lifecycle ─────────────────────────────────────────────────────
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? HeardAt { get; private set; }
    public Guid? HeardBy { get; private set; }
    public DateTimeOffset? ArchivedAt { get; private set; }

    private Voicemail() { }

    public static Voicemail Create(
        Guid tenantId,
        Guid callRecordId,
        Guid campaignId,
        string? callerId,
        int durationSeconds,
        string storageKeyPrefix = "voicemails")
    {
        var id = Guid.NewGuid();
        return new Voicemail
        {
            Id              = id,
            TenantId        = tenantId,
            CallRecordId    = callRecordId,
            CampaignId      = campaignId,
            CallerId        = callerId,
            StorageKey      = $"{storageKeyPrefix.Trim('/')}/{callRecordId}/{id}.wav",
            DurationSeconds = Math.Max(0, durationSeconds),
            Status          = VoicemailStatus.New,
            CreatedAt       = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>Records the outcome of the optional email delivery. <paramref name="recipients"/> is the joined to/cc/bcc list.</summary>
    public void RecordEmailDelivery(string status, string? recipients, string? error = null)
    {
        EmailDeliveryStatus = VoicemailEmailStatus.IsValid(status) ? status : VoicemailEmailStatus.Failed;
        EmailDeliveredTo    = recipients;
        EmailDeliveryError  = error;
        EmailDeliveredAt    = EmailDeliveryStatus == VoicemailEmailStatus.Sent ? DateTimeOffset.UtcNow : null;
    }

    public void SetTranscription(string text) => Transcription = text;

    /// <summary>First listen marks it heard; later listens don't move the timestamp.</summary>
    public void MarkHeard(Guid agentId)
    {
        if (Status == VoicemailStatus.New) Status = VoicemailStatus.Heard;
        HeardAt ??= DateTimeOffset.UtcNow;
        HeardBy ??= agentId;
    }

    public void Archive()
    {
        Status     = VoicemailStatus.Archived;
        ArchivedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Back to the inbox (undo an archive / re-flag for follow-up).</summary>
    public void Restore() => Status = HeardAt is null ? VoicemailStatus.New : VoicemailStatus.Heard;
}

public static class VoicemailStatus
{
    public const string New      = "new";
    public const string Heard    = "heard";
    public const string Archived = "archived";

    public static bool IsValid(string value) => value is New or Heard or Archived;
}

public static class VoicemailEmailStatus
{
    public const string Sent    = "sent";
    public const string Failed  = "failed";
    public const string Skipped = "skipped";   // delivery not configured on the node

    public static bool IsValid(string value) => value is Sent or Failed or Skipped;
}
