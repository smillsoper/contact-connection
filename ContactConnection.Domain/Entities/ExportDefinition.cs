using ContactConnection.Domain.ValueObjects.Exports;

namespace ContactConnection.Domain.Entities;

/// <summary>
/// A scheduled data export (S180, Export Worker — e.g. Life Seasons' nightly Cannella LF / SF files). The
/// <see cref="Spec"/> says what's in the file; this entity adds the name and the vendor lifecycle:
///
///   draft → testing → approved → live (⇄ paused)
///
/// Vendors (media agencies, fulfillment houses) approve a test file before they take production data — sometimes before
/// they'll even release their delivery details — so test files can be generated at every stage, and the approval (who at
/// the vendor, when, which test run, a note) is recorded here and in version history. Only a live export runs on its
/// schedule.
///
/// <see cref="SpecRevision"/> counts layout/data changes; an approved/live export whose spec changed after approval shows
/// "changed since the vendor approved it" (<see cref="ChangedSinceApproval"/>) rather than silently dropping out of
/// production — the person editing decides whether to re-test.
/// </summary>
public class ExportDefinition
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string Status { get; private set; } = ExportStatus.Draft;
    public ExportSpec Spec { get; private set; } = new();
    public int SpecRevision { get; private set; } = 1;

    // ── Vendor approval ────────────────────────────────────────────────────────
    public DateTimeOffset? ApprovedAt { get; private set; }
    /// <summary>Who at the vendor approved it (free text — "Jane at Cannella").</summary>
    public string? ApprovedByVendorContact { get; private set; }
    /// <summary>Who recorded the approval in ContactConnection.</summary>
    public string? ApprovalRecordedByName { get; private set; }
    public string? ApprovalNote { get; private set; }
    /// <summary>The test run whose file the vendor approved.</summary>
    public Guid? ApprovedRunId { get; private set; }
    public int? ApprovedSpecRevision { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool ChangedSinceApproval => ApprovedSpecRevision is { } r && r != SpecRevision;

    private ExportDefinition() { }

    public static ExportDefinition Create(Guid tenantId, string name, string? description, ExportSpec spec)
    {
        var now = DateTimeOffset.UtcNow;
        return new ExportDefinition
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Name = name.Trim(), Description = Blank(description),
            Spec = spec, Status = ExportStatus.Draft, CreatedAt = now, UpdatedAt = now,
        };
    }

    public void Rename(string name, string? description)
    {
        Name = name.Trim();
        Description = Blank(description);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Replaces the spec; bumps <see cref="SpecRevision"/> only when something actually changed.</summary>
    public bool UpdateSpec(ExportSpec spec)
    {
        if (SpecEquals(Spec, spec)) return false;
        Spec = spec;
        SpecRevision++;
        UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    public void StartTesting() => Move(ExportStatus.Testing, from: [ExportStatus.Draft, ExportStatus.Approved, ExportStatus.Live, ExportStatus.Paused]);

    public void Approve(string vendorContact, string recordedByName, string? note, Guid testRunId)
    {
        Move(ExportStatus.Approved, from: [ExportStatus.Testing]);
        ApprovedAt = DateTimeOffset.UtcNow;
        ApprovedByVendorContact = vendorContact.Trim();
        ApprovalRecordedByName = recordedByName;
        ApprovalNote = Blank(note);
        ApprovedRunId = testRunId;
        ApprovedSpecRevision = SpecRevision;
    }

    public void GoLive() => Move(ExportStatus.Live, from: [ExportStatus.Approved, ExportStatus.Paused]);
    public void Pause() => Move(ExportStatus.Paused, from: [ExportStatus.Live]);
    public void BackToDraft() => Move(ExportStatus.Draft, from: [ExportStatus.Testing, ExportStatus.Approved, ExportStatus.Paused]);

    private void Move(string to, string[] from)
    {
        if (!from.Contains(Status))
            throw new InvalidOperationException($"An export that is {Status} can't move to {to}.");
        Status = to;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static bool SpecEquals(ExportSpec a, ExportSpec b) =>
        System.Text.Json.JsonSerializer.Serialize(a) == System.Text.Json.JsonSerializer.Serialize(b);

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

public static class ExportStatus
{
    public const string Draft = "draft";
    public const string Testing = "testing";
    public const string Approved = "approved";
    public const string Live = "live";
    public const string Paused = "paused";
}
