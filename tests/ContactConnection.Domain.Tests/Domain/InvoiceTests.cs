using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects;
using Xunit;

namespace ContactConnection.Domain.Tests.Domain;

/// <summary>Invoices (S179, Sprint 1 item 2): meter lines, per-line rounding, draft → issue → paid / void, credit notes.</summary>
public class InvoiceTests
{
    private static MeteredCall Call(string source, string caller, string? dnis, int seconds)
    {
        var start = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
        return new MeteredCall(source, caller, dnis, start, start.AddSeconds(seconds), true);
    }

    private static UsageTally Tally()
    {
        var t = new UsageTally();
        t.Add(Call("inbound", "+15416704541", "+15416413898", 590));   // 10 billed min local
        t.Add(Call("outbound", "+15416704541", null, 61));              // 2 billed min outbound
        t.Add(Call("inbound", "+15416704541", "+18001234567", 300));   // 5 billed min toll-free
        return t;
    }

    [Fact]
    public void UsageLines_OnePerCategory_RatesCopied_LinesAddUpToTheCharges()
    {
        var lines = InvoiceUsageLines.Build(Tally(), 0.035m, 0.01m, 0m, "September 2026");

        Assert.Equal(["usage_local", "usage_tollfree", "usage_outbound"], lines.Select(l => l.Kind));
        Assert.Equal(10, lines[0].Quantity);
        Assert.Equal(0.045m, lines[1].UnitPrice);   // base + toll-free surcharge

        var invoice = Invoice.CreateDraft(Guid.NewGuid(), new(2026, 9, 1), new(2026, 9, 30), null);
        invoice.ReplaceUsageLines(lines.Select(l => InvoiceLine.Create(Guid.Empty, l.Kind, l.Description, l.Quantity, l.UnitPrice, null, null)));
        var charges = UsageCharges.Calculate(Tally(), 0.035m, 0.01m, 0m);
        Assert.Equal(charges.Total, invoice.Total);                        // 0.35 + 0.23 + 0.07
        Assert.Equal(0.65m, invoice.Total);
        Assert.Equal(invoice.Lines.Sum(l => l.Amount), invoice.Total);
    }

    [Fact]
    public void Charges_RoundEachLine_ThenAdd()
    {
        // 7 local min + 7 outbound min at $0.0355: each line 0.2485 → 0.25, so 0.50 — rounding them combined would give 0.497 → 0.50 here
        // but the point is the total equals the sum of the rounded lines, whatever the minutes.
        var t = new UsageTally();
        t.Add(Call("inbound", "+15416704541", "+15416413898", 7 * 60));
        t.Add(Call("outbound", "+15416704541", null, 7 * 60));
        var c = UsageCharges.Calculate(t, 0.0355m, 0.01m, 0m);
        Assert.Equal(c.Local + c.TollFree + c.Outbound, c.Usage);
        Assert.Equal(0.25m, c.Local);
        Assert.Equal(0.25m, c.Outbound);
    }

    [Fact]
    public void MinimumTopUp_WhenUsageFallsShort()
    {
        var lines = InvoiceUsageLines.Build(Tally(), 0.035m, 0.01m, 100m, "September 2026");
        var minimum = Assert.Single(lines, l => l.Kind == "minimum");
        Assert.Equal(100m - 0.65m, minimum.UnitPrice);
        Assert.Equal(100m, lines.Sum(l => InvoiceLine.Create(Guid.Empty, l.Kind, l.Description, l.Quantity, l.UnitPrice, null, null).Amount));

        Assert.DoesNotContain(InvoiceUsageLines.Build(Tally(), 0.035m, 0.01m, 0.5m, "x"), l => l.Kind == "minimum");
    }

    [Fact]
    public void RefreshUsage_KeepsHandAddedLines()
    {
        var invoice = Invoice.CreateDraft(Guid.NewGuid(), new(2026, 9, 1), new(2026, 9, 30), null);
        invoice.AddLine(InvoiceLineKind.SetupFee, "Implementation", 1, 5000m, null, "stephen");
        invoice.ReplaceUsageLines([InvoiceLine.Create(Guid.Empty, InvoiceLineKind.UsageLocal, "Local", 10, 0.035m, null, null)]);
        invoice.ReplaceUsageLines([InvoiceLine.Create(Guid.Empty, InvoiceLineKind.UsageLocal, "Local", 20, 0.035m, null, null)]);

        Assert.Equal(2, invoice.Lines.Count);
        Assert.Equal(5000.70m, invoice.Total);
        Assert.Equal(InvoiceLineKind.UsageLocal, invoice.Lines.OrderBy(l => l.SortOrder).First().Kind);
    }

    [Fact]
    public void Credits_AreNegative_AndNeedAReason()
    {
        var invoice = Invoice.CreateDraft(Guid.NewGuid(), null, null, null);
        invoice.AddLine(InvoiceLineKind.SetupFee, "Implementation", 1, 5000m, null, null);
        Assert.Throws<ArgumentException>(() => invoice.AddLine(InvoiceLineKind.Credit, "Goodwill", 1, 100m, null, null));
        invoice.AddLine(InvoiceLineKind.Credit, "Goodwill", 1, 100m, "Launch delay", null);
        Assert.Equal(4900m, invoice.Total);
    }

    [Fact]
    public void Issued_IsFrozen_PaidOrVoided()
    {
        var invoice = Invoice.CreateDraft(Guid.NewGuid(), null, null, null);
        Assert.Throws<InvalidOperationException>(() => invoice.Issue("INV-2026-0001", "LS", "a@b.c", DateTimeOffset.UtcNow, 15));   // no lines
        invoice.AddLine(InvoiceLineKind.SetupFee, "Implementation", 1, 5000m, null, null);
        var now = new DateTimeOffset(2026, 10, 1, 17, 0, 0, TimeSpan.Zero);
        invoice.Issue("INV-2026-0001", "Life Seasons", "billing@example.com", now, 15);

        Assert.Equal(InvoiceStatus.Issued, invoice.Status);
        Assert.Equal(new DateOnly(2026, 10, 16), invoice.DueOn);
        Assert.Throws<InvalidOperationException>(() => invoice.AddLine(InvoiceLineKind.Adjustment, "x", 1, 1m, "r", null));
        Assert.Throws<InvalidOperationException>(() => invoice.RemoveLine(invoice.Lines[0].Id));

        invoice.MarkPaid(now.AddDays(3), "ACH 1234");
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Throws<InvalidOperationException>(() => invoice.Void("oops", now));   // paid → credit it instead
    }

    [Fact]
    public void CreditNote_OnlyAgainstAnIssuedInvoice_CreditLinesOnly()
    {
        var invoice = Invoice.CreateDraft(Guid.NewGuid(), null, null, null);
        invoice.AddLine(InvoiceLineKind.SetupFee, "Implementation", 1, 5000m, null, null);
        Assert.Throws<InvalidOperationException>(() => Invoice.CreateCreditNote(invoice, null));   // still a draft

        invoice.Issue("INV-2026-0001", null, null, DateTimeOffset.UtcNow, 15);
        var note = Invoice.CreateCreditNote(invoice, null);
        Assert.Equal(invoice.Id, note.CreditsInvoiceId);
        Assert.Throws<ArgumentException>(() => note.AddLine(InvoiceLineKind.Adjustment, "x", 1, 10m, "r", null));
        note.AddLine(InvoiceLineKind.Credit, "Billing correction", 1, 250m, "Double-billed minutes", null);
        Assert.Equal(-250m, note.Total);
        note.Issue("CN-2026-0001", null, null, DateTimeOffset.UtcNow, 15);   // a credit note may total below zero
        Assert.Equal(InvoiceStatus.Issued, note.Status);
    }

    [Fact]
    public void AnInvoice_CantTotalBelowZero()
    {
        var invoice = Invoice.CreateDraft(Guid.NewGuid(), null, null, null);
        invoice.AddLine(InvoiceLineKind.Credit, "Goodwill", 1, 10m, "r", null);
        Assert.Throws<InvalidOperationException>(() => invoice.Issue("INV-2026-0002", null, null, DateTimeOffset.UtcNow, 15));
    }
}
