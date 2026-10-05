using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects;

namespace ContactConnection.Application.Interfaces.Services;

/// <summary>Billable usage for one calendar month in the tenant's time zone (production calls only).</summary>
public interface IUsageMeter
{
    Task<UsageMonth> TallyMonthAsync(Tenant tenant, int year, int month, CancellationToken ct = default);
}

public sealed record UsageMonth(UsageTally Tally, DateTimeOffset From, DateTimeOffset To, string TimeZone, DateOnly FirstDay, DateOnly LastDay);

/// <summary>
/// Tenant invoicing (S179, Sprint 1 item 2): monthly drafts from the usage meter, hand-added lines, issue (number +
/// email), payment, void and credit notes. Rule violations throw <see cref="InvalidOperationException"/> /
/// <see cref="ArgumentException"/> with a message fit to show.
/// </summary>
public interface IInvoiceService
{
    /// <summary>The month's draft — or the existing live invoice for that month (idempotent; the Worker calls it daily).</summary>
    Task<Invoice> CreateMonthlyDraftAsync(Guid tenantId, int year, int month, string? createdBy, CancellationToken ct = default);
    /// <summary>A draft with no usage period — e.g. the setup fee on its own.</summary>
    Task<Invoice> CreateStandaloneDraftAsync(Guid tenantId, string? createdBy, CancellationToken ct = default);
    Task<Invoice> RefreshUsageAsync(Guid invoiceId, CancellationToken ct = default);
    Task<Invoice> AddLineAsync(Guid invoiceId, string kind, string description, decimal quantity, decimal unitPrice,
        string? reason, string? createdBy, CancellationToken ct = default);
    Task<Invoice> RemoveLineAsync(Guid invoiceId, Guid lineId, CancellationToken ct = default);
    Task<Invoice> SetNotesAsync(Guid invoiceId, string? notes, CancellationToken ct = default);
    Task<InvoiceIssueResult> IssueAsync(Guid invoiceId, CancellationToken ct = default);
    Task<Invoice> MarkPaidAsync(Guid invoiceId, DateTimeOffset? paidAt, string? reference, CancellationToken ct = default);
    Task<Invoice> VoidAsync(Guid invoiceId, string reason, CancellationToken ct = default);
    /// <summary>A draft credit note against an issued invoice, with one credit line.</summary>
    Task<Invoice> CreateCreditNoteAsync(Guid invoiceId, decimal amount, string description, string reason, string? createdBy,
        CancellationToken ct = default);
    Task DeleteDraftAsync(Guid invoiceId, CancellationToken ct = default);
    Task<Invoice?> GetAsync(Guid invoiceId, CancellationToken ct = default);
    Task<IReadOnlyList<Invoice>> ListAsync(Guid? tenantId, CancellationToken ct = default);
    /// <summary>The invoice as a standalone HTML document — the emailed copy and the printable view.</summary>
    Task<string?> RenderHtmlAsync(Guid invoiceId, CancellationToken ct = default);
}

/// <summary><paramref name="EmailedTo"/> null = issued but not emailed (no billing email on file, or the send failed —
/// <paramref name="EmailError"/> says which).</summary>
public sealed record InvoiceIssueResult(Invoice Invoice, string? EmailedTo, string? EmailError);
