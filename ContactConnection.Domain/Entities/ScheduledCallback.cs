namespace ContactConnection.Domain.Entities;

/// <summary>
/// A callback booked for a specific future time — created by a <c>tf_scheduled_callback</c>
/// telephony node or a <c>scheduled_callback</c> CRM script node. The tenant's flow captures the
/// desired date/time however it likes (IVR, DTMF, agent entry) and passes it as text; the node
/// validates it against an allowed day/hour window and freezes it here.
///
/// When the time comes, the Worker places an outbound call to <see cref="CallbackNumber"/> with
/// caller ID <see cref="CallerIdOverride"/> (or the <see cref="Dnis"/> the caller originally
/// dialed), and the answered leg is routed into <see cref="TargetFlowId"/> — a designated
/// telephony flow, NOT necessarily the origin campaign's inbound flow (that would risk re-
/// offering the callback and looping the caller).
///
/// This is distinct from a queue callback / virtual hold (hold the caller's place in line,
/// reserve an agent, bridge direct) — that is a separate feature, not this entity.
///
/// State tracking (ARCHITECTURE.md §16):
///
///   scheduled  → booked; waiting for <see cref="ScheduledFor"/>
///   attempted  → outbound leg placed, ringing the caller
///   completed  → caller answered ✓
///   abandoned  → caller did not answer after the last allowed attempt (IS an abandon —
///                <see cref="CallAbandonType.CallbackAbandon"/>)
///   expired    → the attempt window (<see cref="ScheduledFor"/> … <see cref="ExpiresAt"/>)
///                passed without a single successful contact
///   cancelled  → caller reached an agent another way, or a supervisor cancelled, before it fired
///
/// A no-answer on an attempt that still has retries left drops back to <c>scheduled</c> for the
/// worker's next due pass; only the final no-answer lands on <c>abandoned</c>.
/// </summary>
public class ScheduledCallback
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }

    /// <summary>The call record this callback was booked on (an inbound call, or the agent's
    /// active call when booked from a CRM script node).</summary>
    public Guid CallRecordId { get; private set; }

    /// <summary>Campaign the booking was made under (used for reporting / eligibility when
    /// <see cref="TargetCampaignId"/> is not set).</summary>
    public Guid CampaignId { get; private set; }

    /// <summary>E.164 number to dial when the callback fires — the caller's ANI, or a number the
    /// IVR/agent captured.</summary>
    public string CallbackNumber { get; private set; } = string.Empty;

    /// <summary>The number the caller originally dialed (DNIS) — captured when the request node
    /// runs. Default outbound caller ID, and the <c>cc_did</c> that resolves the tenant/campaign
    /// for the answered leg.</summary>
    public string? Dnis { get; private set; }

    /// <summary>Caller ID to present on the outbound leg. Null/blank = <see cref="Dnis"/>.
    /// Resolved to a literal when the request node runs (a <c>{{variable}}</c> is frozen here).</summary>
    public string? CallerIdOverride { get; private set; }

    /// <summary>Telephony flow the answered callback leg runs. Null = the origin campaign's
    /// inbound flow (discouraged — see the class summary).</summary>
    public Guid? TargetFlowId { get; private set; }

    /// <summary>Campaign context for the answered leg (its queue). Null = <see cref="CampaignId"/>.</summary>
    public Guid? TargetCampaignId { get; private set; }

    public string Status { get; private set; } = ScheduledCallbackStatus.Scheduled;

    public DateTimeOffset RequestedAt { get; private set; }

    /// <summary>The booked time — earliest the worker may place the outbound leg.</summary>
    public DateTimeOffset ScheduledFor { get; private set; }

    /// <summary>Attempt-window close — after this an un-connected callback <c>expires</c>.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

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

    private ScheduledCallback() { }

    /// <param name="scheduledFor">The booked time (must be in the future). The node is
    /// responsible for validating it against any allowed day/hour window first.</param>
    /// <param name="windowMinutes">How long past <paramref name="scheduledFor"/> the worker keeps
    /// trying before the callback expires.</param>
    public static ScheduledCallback Create(
        Guid tenantId,
        Guid callRecordId,
        Guid campaignId,
        string callbackNumber,
        DateTimeOffset scheduledFor,
        int windowMinutes = 120,
        int maxAttempts = 3,
        string? callerIdOverride = null,
        string? dnis = null,
        Guid? targetFlowId = null,
        Guid? targetCampaignId = null)
    {
        if (string.IsNullOrWhiteSpace(callbackNumber))
            throw new ArgumentException("A callback number is required.", nameof(callbackNumber));

        var now = DateTimeOffset.UtcNow;

        return new ScheduledCallback
        {
            Id               = Guid.NewGuid(),
            TenantId         = tenantId,
            CallRecordId     = callRecordId,
            CampaignId       = campaignId,
            CallbackNumber   = callbackNumber.Trim(),
            Dnis             = string.IsNullOrWhiteSpace(dnis) ? null : dnis.Trim(),
            CallerIdOverride = string.IsNullOrWhiteSpace(callerIdOverride) ? null : callerIdOverride.Trim(),
            TargetFlowId     = targetFlowId == Guid.Empty ? null : targetFlowId,
            TargetCampaignId = targetCampaignId == Guid.Empty ? null : targetCampaignId,
            Status           = ScheduledCallbackStatus.Scheduled,
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
        Status == ScheduledCallbackStatus.Scheduled
        && now >= ScheduledFor
        && now < ExpiresAt
        && AttemptCount < MaxAttempts;

    /// <summary>True when the attempt window has closed without a successful contact.</summary>
    public bool IsExpired(DateTimeOffset now) =>
        Status == ScheduledCallbackStatus.Scheduled && now >= ExpiresAt;

    /// <summary>Records an outbound leg going out. <paramref name="outboundCallRecordId"/> is the
    /// call record for that leg when it already exists; pass null when the connected call record
    /// is created later (on answer) and linked via <see cref="LinkConnectedCallRecord"/>.</summary>
    public void MarkAttempted(Guid? outboundCallRecordId = null)
    {
        RequireStatus(ScheduledCallbackStatus.Scheduled);
        Status        = ScheduledCallbackStatus.Attempted;
        AttemptCount += 1;
        LastAttemptAt = DateTimeOffset.UtcNow;
        if (outboundCallRecordId is { } id && id != Guid.Empty)
            OutboundCallRecordId = id;
        Touch();
    }

    /// <summary>Links the call record created for the connected callback leg. From <c>attempted</c>.</summary>
    public void LinkConnectedCallRecord(Guid connectedCallRecordId)
    {
        if (connectedCallRecordId != Guid.Empty)
            OutboundCallRecordId = connectedCallRecordId;
        Touch();
    }

    /// <summary>Caller answered the callback.</summary>
    public void MarkCompleted()
    {
        RequireStatus(ScheduledCallbackStatus.Attempted);
        Status      = ScheduledCallbackStatus.Completed;
        CompletedAt = DateTimeOffset.UtcNow;
        Touch();
    }

    /// <summary>An attempt that didn't connect. Drops back to <c>scheduled</c> for another pass
    /// while retries remain; the final no-answer lands on <c>abandoned</c>. Returns true when it
    /// abandoned.</summary>
    public bool MarkNoAnswer(string? detail = null)
    {
        RequireStatus(ScheduledCallbackStatus.Attempted);
        Detail = detail ?? Detail;

        if (AttemptCount >= MaxAttempts)
        {
            Status      = ScheduledCallbackStatus.Abandoned;
            AbandonedAt = DateTimeOffset.UtcNow;
            Touch();
            return true;
        }

        Status = ScheduledCallbackStatus.Scheduled;
        Touch();
        return false;
    }

    /// <summary>The attempt window passed without a successful contact.</summary>
    public void MarkExpired(string? detail = null)
    {
        if (Status != ScheduledCallbackStatus.Scheduled)
            throw new InvalidOperationException($"Cannot expire a scheduled callback in status '{Status}'.");
        Status    = ScheduledCallbackStatus.Expired;
        ExpiredAt = DateTimeOffset.UtcNow;
        Detail    = detail ?? Detail;
        Touch();
    }

    /// <summary>Caller reached an agent another way, or a supervisor cancelled, before it fired.</summary>
    public void Cancel(string reason)
    {
        if (Status != ScheduledCallbackStatus.Scheduled)
            throw new InvalidOperationException($"Cannot cancel a scheduled callback in status '{Status}'.");
        Status      = ScheduledCallbackStatus.Cancelled;
        CancelledAt = DateTimeOffset.UtcNow;
        Detail      = reason;
        Touch();
    }

    private void RequireStatus(string expected)
    {
        if (Status != expected)
            throw new InvalidOperationException(
                $"Scheduled callback must be '{expected}' for this transition, but is '{Status}'.");
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}

public static class ScheduledCallbackStatus
{
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
        value is Scheduled or Attempted or Completed or Abandoned or Expired or Cancelled;
}
