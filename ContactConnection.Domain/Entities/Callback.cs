namespace ContactConnection.Domain.Entities;

/// <summary>
/// A queued caller's request to be called back instead of holding — created by a
/// <c>tf_request_callback</c> telephony flow node (offered on long queue waits / after hours /
/// an IVR "press 1 for a callback" branch). A child of the originating call record (the Single
/// Record of Truth container); the outbound leg placed when the callback fires gets its own
/// call record, linked via <see cref="OutboundCallRecordId"/>.
///
/// Full state tracking per ARCHITECTURE.md §16 — not just requested/completed:
///
///   requested  → row created, no window assigned yet
///   scheduled  → window assigned (<see cref="ScheduledFor"/> … <see cref="ExpiresAt"/>)
///   attempted  → outbound leg placed, ringing the caller
///   completed  → caller answered ✓
///   abandoned  → caller did not answer after the last allowed attempt (IS an abandon —
///                <see cref="CallAbandonType.CallbackAbandon"/>)
///   expired    → the window passed without a single attempt
///   cancelled  → caller phoned back in (or a supervisor cancelled) before the callback fired
///
/// A no-answer on an attempt that still has retries left drops back to <c>scheduled</c> for the
/// worker's next due pass; only the final no-answer lands on <c>abandoned</c>.
/// </summary>
public class Callback
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }

    /// <summary>The inbound call record this request was made on.</summary>
    public Guid CallRecordId { get; private set; }
    public Guid CampaignId { get; private set; }

    /// <summary>E.164 number to dial when the callback fires — the caller's ANI, or a number
    /// the IVR/agent collected.</summary>
    public string CallbackNumber { get; private set; } = string.Empty;

    /// <summary>The number the caller originally dialed (DNIS) — captured when the request node
    /// runs. Used as the outbound caller ID when no <see cref="CallerIdOverride"/> is set, and as
    /// the <c>cc_did</c> that routes the answered callback leg back to the same campaign.</summary>
    public string? Dnis { get; private set; }

    /// <summary>Caller ID to present on the outbound callback leg. Null/blank = <see cref="Dnis"/>
    /// (the number the caller dialed). Resolved to a literal when the request node runs (so a
    /// <c>{{variable}}</c> is frozen here, not re-evaluated when the callback later fires).</summary>
    public string? CallerIdOverride { get; private set; }

    public string Status { get; private set; } = CallbackStatus.Requested;

    public DateTimeOffset RequestedAt { get; private set; }

    /// <summary>Earliest the worker may place the outbound leg. Null while <c>requested</c>.</summary>
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>Window close — after this, an un-attempted callback <c>expires</c>.</summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    public int AttemptCount { get; private set; }
    public int MaxAttempts { get; private set; } = 3;
    public DateTimeOffset? LastAttemptAt { get; private set; }

    /// <summary>Call record of the most recent outbound leg placed for this callback.</summary>
    public Guid? OutboundCallRecordId { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? AbandonedAt { get; private set; }
    public DateTimeOffset? ExpiredAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>Free-text context — cancel reason, last originate error, etc.</summary>
    public string? Detail { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Callback() { }

    /// <param name="delay">How far out the window opens from now (0 = eligible immediately).</param>
    /// <param name="windowMinutes">How long the window stays open before the request expires.</param>
    public static Callback Create(
        Guid tenantId,
        Guid callRecordId,
        Guid campaignId,
        string callbackNumber,
        TimeSpan delay,
        int windowMinutes = 120,
        int maxAttempts = 3,
        string? callerIdOverride = null,
        string? dnis = null)
    {
        if (string.IsNullOrWhiteSpace(callbackNumber))
            throw new ArgumentException("A callback number is required.", nameof(callbackNumber));

        var now = DateTimeOffset.UtcNow;
        var scheduledFor = now + (delay < TimeSpan.Zero ? TimeSpan.Zero : delay);

        return new Callback
        {
            Id               = Guid.NewGuid(),
            TenantId         = tenantId,
            CallRecordId     = callRecordId,
            CampaignId       = campaignId,
            CallbackNumber   = callbackNumber.Trim(),
            Dnis             = string.IsNullOrWhiteSpace(dnis) ? null : dnis.Trim(),
            CallerIdOverride = string.IsNullOrWhiteSpace(callerIdOverride) ? null : callerIdOverride.Trim(),
            Status           = CallbackStatus.Scheduled,
            RequestedAt      = now,
            ScheduledFor     = scheduledFor,
            ExpiresAt        = scheduledFor + TimeSpan.FromMinutes(Math.Max(1, windowMinutes)),
            MaxAttempts      = Math.Max(1, maxAttempts),
            AttemptCount     = 0,
            CreatedAt        = now,
            UpdatedAt        = now,
        };
    }

    /// <summary>True when the worker should place an outbound leg now.</summary>
    public bool IsDue(DateTimeOffset now) =>
        Status == CallbackStatus.Scheduled
        && ScheduledFor is { } s && now >= s
        && ExpiresAt is { } e && now < e
        && AttemptCount < MaxAttempts;

    /// <summary>True when the window has closed without the callback ever being attempted.</summary>
    public bool IsExpired(DateTimeOffset now) =>
        Status is CallbackStatus.Requested or CallbackStatus.Scheduled
        && ExpiresAt is { } e && now >= e;

    /// <summary>Records an outbound leg going out. <paramref name="outboundCallRecordId"/> is the
    /// call record for that leg when it already exists; pass null when the connected call record
    /// is created later (on answer) and linked via <see cref="LinkConnectedCallRecord"/>.</summary>
    public void MarkAttempted(Guid? outboundCallRecordId = null)
    {
        RequireStatus(CallbackStatus.Scheduled);
        Status        = CallbackStatus.Attempted;
        AttemptCount += 1;
        LastAttemptAt = DateTimeOffset.UtcNow;
        if (outboundCallRecordId is { } id && id != Guid.Empty)
            OutboundCallRecordId = id;
        Touch();
    }

    /// <summary>Links the call record created for the connected callback leg (set on answer,
    /// when the leg re-enters the campaign flow). Safe to call once, from <c>attempted</c>.</summary>
    public void LinkConnectedCallRecord(Guid connectedCallRecordId)
    {
        if (connectedCallRecordId != Guid.Empty)
            OutboundCallRecordId = connectedCallRecordId;
        Touch();
    }

    /// <summary>Caller answered the callback.</summary>
    public void MarkCompleted()
    {
        RequireStatus(CallbackStatus.Attempted);
        Status      = CallbackStatus.Completed;
        CompletedAt = DateTimeOffset.UtcNow;
        Touch();
    }

    /// <summary>An attempt that didn't connect. Drops back to <c>scheduled</c> for another pass
    /// while retries remain; the final no-answer lands on <c>abandoned</c>. Returns true when it
    /// abandoned (so the caller can record the abandon in call state history).</summary>
    public bool MarkNoAnswer(string? detail = null)
    {
        RequireStatus(CallbackStatus.Attempted);
        Detail = detail ?? Detail;

        if (AttemptCount >= MaxAttempts)
        {
            Status      = CallbackStatus.Abandoned;
            AbandonedAt = DateTimeOffset.UtcNow;
            Touch();
            return true;
        }

        Status = CallbackStatus.Scheduled;
        Touch();
        return false;
    }

    /// <summary>The window passed without a single attempt.</summary>
    public void MarkExpired(string? detail = null)
    {
        if (Status is not (CallbackStatus.Requested or CallbackStatus.Scheduled))
            throw new InvalidOperationException($"Cannot expire a callback in status '{Status}'.");
        Status    = CallbackStatus.Expired;
        ExpiredAt = DateTimeOffset.UtcNow;
        Detail    = detail ?? Detail;
        Touch();
    }

    /// <summary>Caller phoned back in, or a supervisor cancelled, before the callback fired.</summary>
    public void Cancel(string reason)
    {
        if (Status is not (CallbackStatus.Requested or CallbackStatus.Scheduled))
            throw new InvalidOperationException($"Cannot cancel a callback in status '{Status}'.");
        Status      = CallbackStatus.Cancelled;
        CancelledAt = DateTimeOffset.UtcNow;
        Detail      = reason;
        Touch();
    }

    private void RequireStatus(string expected)
    {
        if (Status != expected)
            throw new InvalidOperationException(
                $"Callback must be '{expected}' for this transition, but is '{Status}'.");
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}

public static class CallbackStatus
{
    public const string Requested = "requested";
    public const string Scheduled = "scheduled";
    public const string Attempted = "attempted";
    public const string Completed = "completed";
    public const string Abandoned = "abandoned";
    public const string Expired   = "expired";
    public const string Cancelled = "cancelled";

    /// <summary>Statuses from which no further transition happens.</summary>
    public static bool IsTerminal(string value) =>
        value is Completed or Abandoned or Expired or Cancelled;

    public static bool IsValid(string value) =>
        value is Requested or Scheduled or Attempted or Completed or Abandoned or Expired or Cancelled;
}
