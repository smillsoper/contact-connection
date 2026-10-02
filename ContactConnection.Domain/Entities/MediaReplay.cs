namespace ContactConnection.Domain.Entities;

/// <summary>
/// One change to a phone number's media assignments (S171) — created, edited, deleted, or a side effect
/// (a National ended by a newer one, a default Local switched). <see cref="Before"/> / <see cref="After"/>
/// are JSON snapshots of the assignment. Lets an admin see what changed and when before replaying
/// attribution onto past calls.
/// </summary>
public class MediaAssignmentChange
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid PhoneNumberId { get; private set; }
    public Guid AssignmentId { get; private set; }
    public string Action { get; private set; } = "";
    public string Summary { get; private set; } = "";
    public string? Before { get; private set; }
    public string? After { get; private set; }
    public string? ChangedBy { get; private set; }
    public DateTimeOffset ChangedAt { get; private set; }

    private MediaAssignmentChange() { }

    public static MediaAssignmentChange Create(
        Guid tenantId, Guid phoneNumberId, Guid assignmentId, string action, string summary,
        string? before, string? after, string? changedBy) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, PhoneNumberId = phoneNumberId, AssignmentId = assignmentId,
        Action = action, Summary = summary.Length > 500 ? summary[..500] : summary,
        Before = before, After = after, ChangedBy = changedBy, ChangedAt = DateTimeOffset.UtcNow,
    };
}

public static class MediaAssignmentChangeAction
{
    public const string Created        = "created";
    public const string Edited         = "edited";
    public const string Deleted        = "deleted";
    public const string Ended          = "ended";            // end date moved by a newer National
    public const string DefaultChanged = "default_changed";  // default Local moved to another assignment
}

/// <summary>
/// One "replay media attribution" run (S171): every call that started in [From, To) on the number (or all
/// numbers) is re-attributed against the assignments in effect on the call's date and the caller location
/// it already has (captured zip, else area code). Previewed first; applied by the Worker in chunks. Each
/// changed call gets a call-history entry holding its previous attribution. Status values are
/// <see cref="CommissionRecalcStatus"/>'s.
/// </summary>
public class MediaReplayBatch
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid? PhoneNumberId { get; private set; }
    public DateTimeOffset From { get; private set; }
    public DateTimeOffset To { get; private set; }
    public string Reason { get; private set; } = "";
    public Guid RequestedById { get; private set; }
    public string? RequestedBy { get; private set; }
    public string Status { get; private set; } = CommissionRecalcStatus.Pending;
    public int TotalCalls { get; private set; }
    public int ProcessedCalls { get; private set; }
    public int ChangedCalls { get; private set; }
    public string? Error { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    private MediaReplayBatch() { }

    public static MediaReplayBatch Create(
        Guid tenantId, Guid? phoneNumberId, DateTimeOffset from, DateTimeOffset to, string reason,
        Guid requestedById, string? requestedBy)
    {
        if (to <= from) throw new ArgumentException("The end has to be after the start.");
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Give a reason; it's noted on every call that changes.");
        return new MediaReplayBatch
        {
            Id = Guid.NewGuid(), TenantId = tenantId, PhoneNumberId = phoneNumberId, From = from, To = to,
            Reason = reason.Trim(), RequestedById = requestedById, RequestedBy = requestedBy, CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>Begins (or after a Worker restart re-begins) the run; a re-run only rewrites what still differs.</summary>
    public void Start(int totalCalls)
    {
        Status = CommissionRecalcStatus.Running;
        TotalCalls = totalCalls;
        ProcessedCalls = 0;
        ChangedCalls = 0;
        StartedAt = DateTimeOffset.UtcNow;
    }

    public void Progress(int processed, int changed) { ProcessedCalls += processed; ChangedCalls += changed; }
    public void Complete() { Status = CommissionRecalcStatus.Completed; CompletedAt = DateTimeOffset.UtcNow; }
    public void Fail(string error) { Status = CommissionRecalcStatus.Failed; Error = error; CompletedAt = DateTimeOffset.UtcNow; }
}
