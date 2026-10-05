namespace ContactConnection.Domain.Entities;

/// <summary>
/// A bill to a tenant (S179, Sprint 1 item 2) — platform (public schema). Built as a <b>draft</b> from the usage meter
/// (the Worker does it on the 1st for last month), reviewed in the Portal where adjustment / credit / setup-fee lines can
/// be added, then <b>issued</b>: numbered <c>INV-YYYY-NNNN</c>, frozen and emailed. An issued invoice is never edited;
/// corrections go out as a <b>credit note</b> (<see cref="InvoiceKind.CreditNote"/>, <c>CN-YYYY-NNNN</c>) against it.
/// Rates are copied onto the lines when they're built, so later rate changes never alter a past invoice.
/// </summary>
public class Invoice
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Kind { get; private set; } = InvoiceKind.Invoice;
    /// <summary>Null while a draft; assigned at issue.</summary>
    public string? Number { get; private set; }
    /// <summary>A credit note's original invoice.</summary>
    public Guid? CreditsInvoiceId { get; private set; }
    /// <summary>Usage period (first and last day, tenant's calendar). Null for a standalone invoice (e.g. the setup fee).</summary>
    public DateOnly? PeriodStart { get; private set; }
    public DateOnly? PeriodEnd { get; private set; }
    public string Status { get; private set; } = InvoiceStatus.Draft;
    public decimal Total { get; private set; }
    public string? Notes { get; private set; }
    /// <summary>Who it was sent to, captured at issue.</summary>
    public string? BillToName { get; private set; }
    public string? BillToEmail { get; private set; }
    public DateTimeOffset? IssuedAt { get; private set; }
    public DateOnly? DueOn { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }
    public string? PaymentReference { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }
    public string? VoidReason { get; private set; }
    /// <summary>Stripe (Sprint 1 item 4) — null until then.</summary>
    public string? StripeInvoiceId { get; private set; }
    public string? CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private readonly List<InvoiceLine> _lines = [];
    public IReadOnlyList<InvoiceLine> Lines => _lines;

    public bool IsDraft => Status == InvoiceStatus.Draft;

    private Invoice() { }

    public static Invoice CreateDraft(Guid tenantId, DateOnly? periodStart, DateOnly? periodEnd, string? createdBy)
    {
        if ((periodStart is null) != (periodEnd is null)) throw new ArgumentException("A period needs both a start and an end.");
        var now = DateTimeOffset.UtcNow;
        return new Invoice
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Kind = InvoiceKind.Invoice,
            PeriodStart = periodStart, PeriodEnd = periodEnd, Status = InvoiceStatus.Draft,
            CreatedBy = createdBy, CreatedAt = now, UpdatedAt = now,
        };
    }

    /// <summary>A draft credit note against an issued (or paid) invoice.</summary>
    public static Invoice CreateCreditNote(Invoice original, string? createdBy)
    {
        if (original.Kind != InvoiceKind.Invoice) throw new InvalidOperationException("A credit note is issued against an invoice.");
        if (original.Status is not (InvoiceStatus.Issued or InvoiceStatus.Paid))
            throw new InvalidOperationException("Only an issued invoice can be credited — edit a draft directly.");
        var now = DateTimeOffset.UtcNow;
        return new Invoice
        {
            Id = Guid.NewGuid(), TenantId = original.TenantId, Kind = InvoiceKind.CreditNote, CreditsInvoiceId = original.Id,
            PeriodStart = original.PeriodStart, PeriodEnd = original.PeriodEnd, Status = InvoiceStatus.Draft,
            CreatedBy = createdBy, CreatedAt = now, UpdatedAt = now,
        };
    }

    public InvoiceLine AddLine(string kind, string description, decimal quantity, decimal unitPrice, string? reason, string? createdBy)
    {
        EnsureDraft();
        if (!InvoiceLineKind.IsValid(kind)) throw new ArgumentException($"Unknown line kind '{kind}'.");
        if (Kind == InvoiceKind.CreditNote && kind != InvoiceLineKind.Credit)
            throw new ArgumentException("A credit note only has credit lines.");
        if (InvoiceLineKind.NeedsReason(kind) && string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Adjustments and credits need a reason.");
        var line = InvoiceLine.Create(Id, kind, description, quantity, unitPrice, reason, createdBy, _lines.Count);
        _lines.Add(line);
        Recalculate();
        return line;
    }

    public void RemoveLine(Guid lineId)
    {
        EnsureDraft();
        _lines.RemoveAll(l => l.Id == lineId);
        Recalculate();
    }

    /// <summary>Replaces the meter-built lines (usage + monthly minimum) on a draft; hand-added lines stay.</summary>
    public void ReplaceUsageLines(IEnumerable<InvoiceLine> usageLines)
    {
        EnsureDraft();
        _lines.RemoveAll(l => InvoiceLineKind.IsMetered(l.Kind));
        var order = 0;
        foreach (var l in usageLines) { l.AttachTo(Id, order++); _lines.Add(l); }
        // Hand-added lines after the metered ones.
        foreach (var l in _lines.Where(l => !InvoiceLineKind.IsMetered(l.Kind))) l.AttachTo(Id, order++);
        Recalculate();
    }

    public void SetNotes(string? notes) { EnsureDraft(); Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(); UpdatedAt = DateTimeOffset.UtcNow; }

    /// <summary>Numbers and freezes it. <paramref name="dueDays"/> = payment terms (net N days).</summary>
    public void Issue(string number, string? billToName, string? billToEmail, DateTimeOffset now, int dueDays)
    {
        EnsureDraft();
        if (_lines.Count == 0) throw new InvalidOperationException("An invoice needs at least one line.");
        if (Kind == InvoiceKind.Invoice && Total < 0) throw new InvalidOperationException("An invoice can't total less than zero — use a credit note.");
        Number = number;
        BillToName = billToName;
        BillToEmail = billToEmail;
        IssuedAt = now;
        DueOn = DateOnly.FromDateTime(now.UtcDateTime).AddDays(Math.Max(0, dueDays));
        Status = InvoiceStatus.Issued;
        UpdatedAt = now;
    }

    public void MarkPaid(DateTimeOffset at, string? reference)
    {
        if (Status != InvoiceStatus.Issued) throw new InvalidOperationException("Only an issued invoice can be marked paid.");
        Status = InvoiceStatus.Paid;
        PaidAt = at;
        PaymentReference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Cancels an issued, unpaid invoice (sent in error). The number stays used; nothing is reissued under it.</summary>
    public void Void(string reason, DateTimeOffset now)
    {
        if (Status != InvoiceStatus.Issued) throw new InvalidOperationException("Only an issued, unpaid invoice can be voided — delete a draft, credit a paid one.");
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Give a reason for voiding.");
        Status = InvoiceStatus.Void;
        VoidedAt = now;
        VoidReason = reason.Trim();
        UpdatedAt = now;
    }

    /// <summary>Total = the sum of the already-rounded lines (never re-rounded as a whole).</summary>
    private void Recalculate()
    {
        Total = _lines.Sum(l => l.Amount);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private void EnsureDraft()
    {
        if (!IsDraft) throw new InvalidOperationException("An issued invoice can't be changed — use a credit note.");
    }
}

public class InvoiceLine
{
    public Guid Id { get; private set; }
    public Guid InvoiceId { get; private set; }
    public int SortOrder { get; private set; }
    public string Kind { get; private set; } = InvoiceLineKind.Adjustment;
    public string Description { get; private set; } = string.Empty;
    /// <summary>Billed minutes for usage lines; 1 for flat lines.</summary>
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    /// <summary>Quantity × unit price, rounded to the cent (half away from zero). Credits are negative.</summary>
    public decimal Amount { get; private set; }
    public string? Reason { get; private set; }
    public string? CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private InvoiceLine() { }

    public static InvoiceLine Create(Guid invoiceId, string kind, string description, decimal quantity, decimal unitPrice,
        string? reason, string? createdBy, int sortOrder = 0)
    {
        if (string.IsNullOrWhiteSpace(description)) throw new ArgumentException("A line needs a description.");
        if (quantity < 0) throw new ArgumentException("Quantity can't be negative.");
        var amount = Math.Round(quantity * unitPrice, 2, MidpointRounding.AwayFromZero);
        if (kind == InvoiceLineKind.Credit && amount > 0) amount = -amount;   // a credit always reduces the bill
        return new InvoiceLine
        {
            Id = Guid.NewGuid(), InvoiceId = invoiceId, SortOrder = sortOrder, Kind = kind,
            Description = description.Trim(), Quantity = quantity, UnitPrice = unitPrice, Amount = amount,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(), CreatedBy = createdBy,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    internal void AttachTo(Guid invoiceId, int sortOrder) { InvoiceId = invoiceId; SortOrder = sortOrder; }
}

public static class InvoiceKind
{
    public const string Invoice = "invoice";
    public const string CreditNote = "credit_note";
}

public static class InvoiceStatus
{
    public const string Draft = "draft";
    public const string Issued = "issued";
    public const string Paid = "paid";
    public const string Void = "void";
}

public static class InvoiceLineKind
{
    public const string UsageLocal = "usage_local";
    public const string UsageTollFree = "usage_tollfree";
    public const string UsageOutbound = "usage_outbound";
    public const string Minimum = "minimum";
    public const string SetupFee = "setup_fee";
    public const string Adjustment = "adjustment";
    public const string Credit = "credit";

    public static bool IsValid(string kind) =>
        kind is UsageLocal or UsageTollFree or UsageOutbound or Minimum or SetupFee or Adjustment or Credit;

    /// <summary>Built from the usage meter (replaced when a draft's usage is refreshed).</summary>
    public static bool IsMetered(string kind) => kind is UsageLocal or UsageTollFree or UsageOutbound or Minimum;

    public static bool NeedsReason(string kind) => kind is Adjustment or Credit;
}
