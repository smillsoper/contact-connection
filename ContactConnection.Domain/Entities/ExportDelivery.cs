namespace ContactConnection.Domain.Entities;

/// <summary>
/// Sending one export file to one delivery target (S180, Export Worker session 2) — a durable queue entry like
/// <see cref="ExportRun"/>, claimed by the Worker with <c>FOR UPDATE SKIP LOCKED</c>. A scheduled or Run-now file queues
/// one per enabled target; a test file, or re-sending a past file, queues the targets someone picked.
///
///   queued → running → succeeded | failed   (retried with a growing backoff up to MaxAttempts — vendor servers have bad nights)
///
/// The target's settings are read when it's sent (not copied here), so fixing a wrong password and pressing Retry works.
/// </summary>
public class ExportDelivery
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid RunId { get; private set; }
    public Guid DefinitionId { get; private set; }
    public Guid TargetId { get; private set; }
    public string TargetName { get; private set; } = string.Empty;
    public string TargetType { get; private set; } = string.Empty;
    public bool IsTest { get; private set; }

    public string Status { get; private set; } = ExportDeliveryStatus.Queued;
    public int Attempts { get; private set; }
    public int MaxAttempts { get; private set; } = 5;
    public DateTimeOffset NextAttemptAt { get; private set; }
    public string? Error { get; private set; }

    /// <summary>What it was sent as (after encryption), e.g. <c>/incoming/NERQ_TMS_100526.txt.pgp</c> or the email recipients.</summary>
    public string? SentAs { get; private set; }
    public string? RequestedByName { get; private set; }
    public DateTimeOffset QueuedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }

    private ExportDelivery() { }

    public static ExportDelivery Queue(ExportRun run, Guid targetId, string targetName, string targetType, string? requestedByName)
    {
        var now = DateTimeOffset.UtcNow;
        return new ExportDelivery
        {
            Id = Guid.NewGuid(), TenantId = run.TenantId, RunId = run.Id, DefinitionId = run.DefinitionId,
            TargetId = targetId, TargetName = targetName, TargetType = targetType, IsTest = run.IsTest,
            Status = ExportDeliveryStatus.Queued, NextAttemptAt = now, QueuedAt = now, RequestedByName = requestedByName,
        };
    }

    public void Succeed(string sentAs)
    {
        Status = ExportDeliveryStatus.Succeeded;
        SentAs = sentAs;
        Error = null;
        DeliveredAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Back to the queue after a growing backoff (2, 4, 8, 16 minutes…), or failed at the limit — or at once when
    /// <paramref name="permanent"/> (a missing target or credential won't fix itself).</summary>
    public void Fail(string error, bool permanent = false)
    {
        Error = error.Length > 2000 ? error[..2000] : error;
        if (permanent || Attempts >= MaxAttempts)
        {
            Status = ExportDeliveryStatus.Failed;
            return;
        }
        Status = ExportDeliveryStatus.Queued;
        NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(Math.Pow(2, Math.Max(1, Attempts)));
    }

    /// <summary>Someone pressed Retry (a failed delivery: back to the queue with fresh attempts) or Retry now (one waiting
    /// out its backoff: due immediately, attempts kept).</summary>
    public void Retry(string? requestedByName)
    {
        switch (Status)
        {
            case ExportDeliveryStatus.Failed:
                Status = ExportDeliveryStatus.Queued;
                Attempts = 0;
                Error = null;
                break;
            case ExportDeliveryStatus.Queued:
                break;
            default:
                throw new InvalidOperationException(Status == ExportDeliveryStatus.Running
                    ? "It's being sent right now." : "It was already sent.");
        }
        NextAttemptAt = DateTimeOffset.UtcNow;
        RequestedByName = requestedByName;
    }
}

public static class ExportDeliveryStatus
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
}

/// <summary>Who did what with an export's files (S180): downloads, sends, failures, retention. Append-only.</summary>
public class ExportAuditEntry
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid DefinitionId { get; private set; }
    public Guid? RunId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string? ActorName { get; private set; }
    public string? Detail { get; private set; }
    public DateTimeOffset At { get; private set; }

    private ExportAuditEntry() { }

    public static ExportAuditEntry Record(Guid tenantId, Guid definitionId, Guid? runId, string action, string? actorName, string? detail) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, DefinitionId = definitionId, RunId = runId, Action = action,
        ActorName = actorName, Detail = detail is { Length: > 1000 } ? detail[..1000] : detail, At = DateTimeOffset.UtcNow,
    };
}

public static class ExportAuditAction
{
    public const string Downloaded = "downloaded";
    public const string Delivered = "delivered";
    public const string DeliveryFailed = "delivery_failed";
    public const string SendRequested = "send_requested";
    /// <summary>An SFTP target's host key was pinned on its first connection (accept-new).</summary>
    public const string HostKeyPinned = "host_key_pinned";
    public const string FileExpired = "file_expired";
    /// <summary>S182: the calls' card data was wiped after every target confirmed the card-data file.</summary>
    public const string CardDataWiped = "card_data_wiped";
    public const string CardDataGenerated = "card_data_generated";
}
