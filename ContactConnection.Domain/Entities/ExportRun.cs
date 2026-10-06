using ContactConnection.Domain.ValueObjects.Exports;

namespace ContactConnection.Domain.Entities;

/// <summary>
/// One generation of an export file (S180, Export Worker) — and the durable queue entry that drives it. Queued by Run now,
/// Generate test file, a re-run of a past window, or (session 2) the schedule; claimed by the Worker's
/// <c>ExportRunService</c> (<c>FOR UPDATE SKIP LOCKED</c>, at most 5 at once), rendered from the spec <b>copied onto the
/// run when it was queued</b> (<see cref="Spec"/>), written to blob storage and kept for download / re-delivery.
///
///   queued → running → succeeded | failed   (a failed attempt below MaxAttempts goes back to queued with a backoff)
///
/// Test runs (<see cref="IsTest"/>) are for vendor approval: their file name carries the test suffix, templates see
/// <c>export.is_test</c>, and they never count as a real run (the "since the last successful run" window ignores them).
/// They can read practice calls (training / sandbox) for a vendor that wants a sample before there's production data.
/// </summary>
public class ExportRun
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid DefinitionId { get; private set; }
    /// <summary>Denormalized so history reads right after a rename.</summary>
    public string DefinitionName { get; private set; } = string.Empty;
    public int SpecRevision { get; private set; }
    public ExportSpec Spec { get; private set; } = new();

    public string Kind { get; private set; } = ExportRunKind.Manual;
    public bool IsTest { get; private set; }
    /// <summary><c>production</c> or <c>practice</c> (training + sandbox calls; test runs only).</summary>
    public string DataSource { get; private set; } = ExportDataSource.Production;
    public DateTimeOffset WindowStart { get; private set; }
    public DateTimeOffset WindowEnd { get; private set; }
    /// <summary>Scheduled runs: the run time this file is for (unique per export — the scheduler can't queue it twice).</summary>
    public DateTimeOffset? ScheduledFor { get; private set; }
    /// <summary>Send to the export's delivery targets once generated.</summary>
    public bool Deliver { get; private set; }

    public string Status { get; private set; } = ExportRunStatus.Queued;
    public int Attempts { get; private set; }
    public int MaxAttempts { get; private set; } = 3;
    public DateTimeOffset NextAttemptAt { get; private set; }
    public string? Error { get; private set; }

    public Guid? RequestedById { get; private set; }
    public string? RequestedByName { get; private set; }

    // ── Result ─────────────────────────────────────────────────────────────────
    public int? RowCount { get; private set; }
    public int? CallCount { get; private set; }
    public string? FileName { get; private set; }
    public string? BlobKey { get; private set; }
    public string? ContentType { get; private set; }
    public long? FileSize { get; private set; }
    public string? Sha256 { get; private set; }
    /// <summary>The stored file was removed by retention (the vendor-approved test file never is).</summary>
    public DateTimeOffset? FileDeletedAt { get; private set; }

    public DateTimeOffset QueuedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }

    private ExportRun() { }

    public static ExportRun Queue(
        ExportDefinition definition, string kind, bool isTest, string dataSource,
        DateTimeOffset windowStart, DateTimeOffset windowEnd, Guid? requestedById, string? requestedByName,
        bool deliver = false, DateTimeOffset? scheduledFor = null)
    {
        if (!ExportRunKind.IsValid(kind)) throw new ArgumentException($"Unknown run kind '{kind}'.", nameof(kind));
        if (dataSource == ExportDataSource.Practice && !isTest)
            throw new ArgumentException("Practice calls can only go into a test file.", nameof(dataSource));
        if (windowEnd <= windowStart) throw new ArgumentException("The window must end after it starts.");
        var now = DateTimeOffset.UtcNow;
        return new ExportRun
        {
            Id = Guid.NewGuid(), TenantId = definition.TenantId, DefinitionId = definition.Id,
            DefinitionName = definition.Name, SpecRevision = definition.SpecRevision, Spec = definition.Spec,
            Kind = kind, IsTest = isTest, DataSource = dataSource,
            WindowStart = windowStart.ToUniversalTime(), WindowEnd = windowEnd.ToUniversalTime(),
            Status = ExportRunStatus.Queued, NextAttemptAt = now, QueuedAt = now,
            RequestedById = requestedById, RequestedByName = requestedByName,
            Deliver = deliver && !isTest, ScheduledFor = scheduledFor?.ToUniversalTime(),
        };
    }

    public void Succeed(int rowCount, int callCount, string fileName, string blobKey, string contentType, long size, string sha256)
    {
        Status = ExportRunStatus.Succeeded;
        RowCount = rowCount; CallCount = callCount;
        FileName = fileName; BlobKey = blobKey; ContentType = contentType; FileSize = size; Sha256 = sha256;
        Error = null;
        FinishedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>A failed attempt: back to the queue after <paramref name="backoff"/>, or failed for good at the limit
    /// (or straight away when <paramref name="permanent"/> — a template error won't fix itself on retry).</summary>
    public void Fail(string error, TimeSpan backoff, bool permanent = false)
    {
        Error = error.Length > 2000 ? error[..2000] : error;
        if (permanent || Attempts >= MaxAttempts)
        {
            Status = ExportRunStatus.Failed;
            FinishedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            Status = ExportRunStatus.Queued;
            NextAttemptAt = DateTimeOffset.UtcNow + backoff;
        }
    }

    public void MarkFileDeleted() => FileDeletedAt = DateTimeOffset.UtcNow;

    /// <summary>The file name for this run: test files get the spec's test suffix before the extension.</summary>
    public static string ApplyTestSuffix(string fileName, string? suffix)
    {
        if (string.IsNullOrEmpty(suffix)) return fileName;
        var ext = Path.GetExtension(fileName);
        return Path.GetFileNameWithoutExtension(fileName) + suffix + ext;
    }
}

public static class ExportRunKind
{
    public const string Manual = "manual";
    public const string Test = "test";
    public const string Rerun = "rerun";
    public const string Scheduled = "scheduled";
    public static bool IsValid(string? v) => v is Manual or Test or Rerun or Scheduled;
}

public static class ExportRunStatus
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
}

public static class ExportDataSource
{
    public const string Production = "production";
    /// <summary>Training + sandbox calls — for a test file before there's production data.</summary>
    public const string Practice = "practice";
    public static bool IsValid(string? v) => v is Production or Practice;
}
