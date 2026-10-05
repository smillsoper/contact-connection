using System.Globalization;
using System.Net;
using System.Text;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Billing;

/// <summary>See <see cref="IInvoiceService"/>. Platform data — runs against the public schema.</summary>
public class InvoiceService(
    ContactConnectionDbContext db,
    IUsageMeter meter,
    IServiceProvider services,
    IConfiguration config,
    ILogger<InvoiceService> logger) : IInvoiceService
{
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");
    private int DueDays => int.TryParse(config["Billing:DueDays"], out var d) && d >= 0 ? d : 15;

    public async Task<Invoice> CreateMonthlyDraftAsync(Guid tenantId, int year, int month, string? createdBy, CancellationToken ct = default)
    {
        var tenant = await TenantAsync(tenantId, ct);
        var periodStart = new DateOnly(year, month, 1);
        var existing = await Load().FirstOrDefaultAsync(i => i.TenantId == tenantId && i.Kind == InvoiceKind.Invoice
            && i.PeriodStart == periodStart && i.Status != InvoiceStatus.Void, ct);
        if (existing is not null) return existing;

        var usage = await meter.TallyMonthAsync(tenant, year, month, ct);
        var invoice = Invoice.CreateDraft(tenantId, usage.FirstDay, usage.LastDay, createdBy);
        invoice.ReplaceUsageLines(UsageLines(tenant, usage));
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(ct);
        return invoice;
    }

    public async Task<Invoice> CreateStandaloneDraftAsync(Guid tenantId, string? createdBy, CancellationToken ct = default)
    {
        await TenantAsync(tenantId, ct);
        var invoice = Invoice.CreateDraft(tenantId, null, null, createdBy);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(ct);
        return invoice;
    }

    public async Task<Invoice> RefreshUsageAsync(Guid invoiceId, CancellationToken ct = default)
    {
        var invoice = await RequireAsync(invoiceId, ct);
        if (invoice.PeriodStart is not { } start) throw new InvalidOperationException("This invoice has no usage period.");
        var tenant = await TenantAsync(invoice.TenantId, ct);
        var usage = await meter.TallyMonthAsync(tenant, start.Year, start.Month, ct);
        var lines = UsageLines(tenant, usage).ToList();
        invoice.ReplaceUsageLines(lines);
        // New lines carry their own ids, so EF would take them for existing rows (an UPDATE of nothing) — say they're new.
        db.InvoiceLines.AddRange(lines);
        await SaveAsync(ct);
        return invoice;
    }

    public async Task<Invoice> AddLineAsync(Guid invoiceId, string kind, string description, decimal quantity, decimal unitPrice,
        string? reason, string? createdBy, CancellationToken ct = default)
    {
        if (InvoiceLineKind.IsMetered(kind)) throw new ArgumentException("Usage lines come from the meter — use Refresh usage.");
        var invoice = await RequireAsync(invoiceId, ct);
        db.InvoiceLines.Add(invoice.AddLine(kind, description, quantity, unitPrice, reason, createdBy));
        await SaveAsync(ct);
        return invoice;
    }

    public async Task<Invoice> RemoveLineAsync(Guid invoiceId, Guid lineId, CancellationToken ct = default)
    {
        var invoice = await RequireAsync(invoiceId, ct);
        invoice.RemoveLine(lineId);
        await SaveAsync(ct);
        return invoice;
    }

    public async Task<Invoice> SetNotesAsync(Guid invoiceId, string? notes, CancellationToken ct = default)
    {
        var invoice = await RequireAsync(invoiceId, ct);
        invoice.SetNotes(notes);
        await SaveAsync(ct);
        return invoice;
    }

    public async Task<InvoiceIssueResult> IssueAsync(Guid invoiceId, CancellationToken ct = default)
    {
        var invoice = await RequireAsync(invoiceId, ct);
        if (!invoice.IsDraft) throw new InvalidOperationException("This invoice was already issued.");
        var tenant = await TenantAsync(invoice.TenantId, ct);
        if (invoice.Kind == InvoiceKind.CreditNote)
        {
            var original = await RequireAsync(invoice.CreditsInvoiceId!.Value, ct);
            var alreadyCredited = await db.Invoices.Where(i => i.CreditsInvoiceId == original.Id && i.Status != InvoiceStatus.Draft
                    && i.Status != InvoiceStatus.Void).SumAsync(i => (decimal?)i.Total, ct) ?? 0m;
            if (-(invoice.Total + alreadyCredited) > original.Total)
                throw new InvalidOperationException($"Credits can't exceed the invoice total ({original.Total.ToString("C", Us)}).");
        }

        var recipients = BillingEmails(tenant);
        var now = DateTimeOffset.UtcNow;
        var number = await NextNumberAsync(invoice.Kind == InvoiceKind.CreditNote ? "CN" : "INV", now.Year, ct);
        invoice.Issue(number, tenant.DisplayName ?? tenant.Name, recipients.Count > 0 ? string.Join(", ", recipients) : null, now, DueDays);
        await SaveAsync(ct);

        if (recipients.Count == 0)
            return new InvoiceIssueResult(invoice, null, "No billing email on file — set one on the tenant, then resend.");
        try
        {
            // Resolved only here: drafting (the Worker) never emails, and the Worker has no mail credentials.
            var email = (IEmailService)services.GetService(typeof(IEmailService))!;
            await email.SendAsync(new EmailMessage
            {
                To = recipients,
                FromName = IssuerName,
                ReplyTo = IssuerEmail,
                Subject = invoice.Kind == InvoiceKind.CreditNote
                    ? $"Credit note {number} from {IssuerName}"
                    : $"Invoice {number} from {IssuerName} — {invoice.Total.ToString("C", Us)} due {invoice.DueOn:MMM d, yyyy}",
                HtmlBody = await RenderAsync(invoice, tenant, ct),
            }, ct);
            return new InvoiceIssueResult(invoice, string.Join(", ", recipients), null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Invoice {Number} issued but the email failed", number);
            return new InvoiceIssueResult(invoice, null, $"Issued, but the email didn't send: {ex.Message}");
        }
    }

    public async Task<Invoice> MarkPaidAsync(Guid invoiceId, DateTimeOffset? paidAt, string? reference, CancellationToken ct = default)
    {
        var invoice = await RequireAsync(invoiceId, ct);
        invoice.MarkPaid(paidAt ?? DateTimeOffset.UtcNow, reference);
        await SaveAsync(ct);
        return invoice;
    }

    public async Task<Invoice> VoidAsync(Guid invoiceId, string reason, CancellationToken ct = default)
    {
        var invoice = await RequireAsync(invoiceId, ct);
        invoice.Void(reason, DateTimeOffset.UtcNow);
        await SaveAsync(ct);
        return invoice;
    }

    public async Task<Invoice> CreateCreditNoteAsync(Guid invoiceId, decimal amount, string description, string reason, string? createdBy,
        CancellationToken ct = default)
    {
        if (amount <= 0) throw new ArgumentException("Enter the credit as a positive amount.");
        var original = await RequireAsync(invoiceId, ct);
        var note = Invoice.CreateCreditNote(original, createdBy);
        note.AddLine(InvoiceLineKind.Credit,
            string.IsNullOrWhiteSpace(description) ? $"Credit against invoice {original.Number}" : description, 1, amount, reason, createdBy);
        db.Invoices.Add(note);
        await db.SaveChangesAsync(ct);
        return note;
    }

    public async Task DeleteDraftAsync(Guid invoiceId, CancellationToken ct = default)
    {
        var invoice = await RequireAsync(invoiceId, ct);
        if (!invoice.IsDraft) throw new InvalidOperationException("Only a draft can be deleted — void or credit an issued invoice.");
        db.Invoices.Remove(invoice);
        await db.SaveChangesAsync(ct);
    }

    public Task<Invoice?> GetAsync(Guid invoiceId, CancellationToken ct = default) =>
        Load().FirstOrDefaultAsync(i => i.Id == invoiceId, ct);

    public async Task<IReadOnlyList<Invoice>> ListAsync(Guid? tenantId, CancellationToken ct = default)
    {
        var q = Load().AsNoTracking();
        if (tenantId is { } t) q = q.Where(i => i.TenantId == t);
        return await q.OrderByDescending(i => i.PeriodStart ?? DateOnly.MinValue).ThenByDescending(i => i.CreatedAt).ToListAsync(ct);
    }

    public async Task<string?> RenderHtmlAsync(Guid invoiceId, CancellationToken ct = default)
    {
        var invoice = await GetAsync(invoiceId, ct);
        if (invoice is null) return null;
        return await RenderAsync(invoice, await TenantAsync(invoice.TenantId, ct), ct);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private IQueryable<Invoice> Load() => db.Invoices.Include(i => i.Lines);

    private async Task<Invoice> RequireAsync(Guid id, CancellationToken ct) =>
        await Load().FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new KeyNotFoundException("Invoice not found.");

    private async Task<Tenant> TenantAsync(Guid id, CancellationToken ct) =>
        await db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new KeyNotFoundException("Tenant not found.");

    /// <summary>Line edits change the invoice's total too; EF tracks both.</summary>
    private Task SaveAsync(CancellationToken ct) => db.SaveChangesAsync(ct);

    private static IEnumerable<InvoiceLine> UsageLines(Tenant tenant, UsageMonth usage)
    {
        var label = usage.FirstDay.ToString("MMMM yyyy", Us);
        return InvoiceUsageLines.Build(usage.Tally,
                tenant.BillingRatePerMinute ?? Tenant.DefaultRatePerMinute,
                tenant.BillingTollFreeSurcharge ?? Tenant.DefaultTollFreeSurcharge,
                tenant.BillingMonthlyMinimum ?? 0m, label)
            .Select(l => InvoiceLine.Create(Guid.Empty, l.Kind, l.Description, l.Quantity, l.UnitPrice, null, "usage meter"));
    }

    /// <summary>The tenant's billing email(s): Settings.BillingEmail, plus the billing contact when it's an address.</summary>
    private static List<string> BillingEmails(Tenant tenant)
    {
        var list = new List<string>();
        foreach (var candidate in new[] { tenant.Settings.BillingEmail, tenant.BillingContact })
            foreach (var part in (candidate ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (part.Contains('@') && !list.Contains(part, StringComparer.OrdinalIgnoreCase)) list.Add(part);
        return list;
    }

    /// <summary>INV-2026-0001 — one platform-wide sequence per prefix per year, incremented atomically in the database.</summary>
    private async Task<string> NextNumberAsync(string prefix, int year, CancellationToken ct)
    {
        var key = $"{prefix}-{year}";
        var value = await db.Database.SqlQueryRaw<int>(
            "INSERT INTO public.billing_counters (key, value) VALUES ({0}, 1) " +
            "ON CONFLICT (key) DO UPDATE SET value = public.billing_counters.value + 1 RETURNING value AS \"Value\"", key)
            .ToListAsync(ct);
        return $"{key}-{value[0]:D4}";
    }

    private string IssuerName => config["Billing:Issuer:Name"] is { Length: > 0 } n ? n : "ContactConnection";
    private string? IssuerEmail => config["Billing:Issuer:Email"] is { Length: > 0 } e ? e : null;
    private string? IssuerAddress => config["Billing:Issuer:Address"] is { Length: > 0 } a ? a : null;

    private async Task<string> RenderAsync(Invoice invoice, Tenant tenant, CancellationToken ct)
    {
        string? creditsNumber = null;
        if (invoice.CreditsInvoiceId is { } originalId)
            creditsNumber = await db.Invoices.Where(i => i.Id == originalId).Select(i => i.Number).FirstOrDefaultAsync(ct);
        return InvoiceDocument.Render(invoice, tenant.DisplayName ?? tenant.Name, BillingEmails(tenant),
            IssuerName, IssuerAddress, IssuerEmail, creditsNumber);
    }
}

/// <summary>The invoice as one self-contained HTML document (inline styles — email clients ignore &lt;style&gt; blocks).</summary>
public static class InvoiceDocument
{
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");
    private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
    private static string Money(decimal d) => d.ToString("C", Us);

    public static string Render(Invoice inv, string tenantName, IReadOnlyList<string> billTo, string issuerName,
        string? issuerAddress, string? issuerEmail, string? creditsNumber)
    {
        var isCredit = inv.Kind == InvoiceKind.CreditNote;
        var title = isCredit ? "Credit note" : "Invoice";
        var number = inv.Number ?? "DRAFT";
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html><head><meta charset=\"utf-8\"><title>").Append(E($"{title} {number}")).Append("</title></head>");
        sb.Append("<body style=\"margin:0;padding:24px;background:#ffffff;color:#111827;font-family:Arial,Helvetica,sans-serif;font-size:14px\">");
        sb.Append("<div style=\"max-width:720px;margin:0 auto\">");

        if (inv.IsDraft)
            sb.Append("<div style=\"background:#fef3c7;color:#92400e;padding:8px 12px;border-radius:6px;margin-bottom:16px;font-weight:bold\">DRAFT — not yet issued</div>");
        if (inv.Status == InvoiceStatus.Void)
            sb.Append("<div style=\"background:#fee2e2;color:#991b1b;padding:8px 12px;border-radius:6px;margin-bottom:16px;font-weight:bold\">VOID — ")
              .Append(E(inv.VoidReason)).Append("</div>");

        sb.Append("<table style=\"width:100%;border-collapse:collapse\"><tr><td style=\"vertical-align:top\">");
        sb.Append("<div style=\"font-size:20px;font-weight:bold\">").Append(E(issuerName)).Append("</div>");
        if (issuerAddress is not null)
            sb.Append("<div style=\"color:#4b5563;white-space:pre-line\">").Append(E(issuerAddress)).Append("</div>");
        if (issuerEmail is not null) sb.Append("<div style=\"color:#4b5563\">").Append(E(issuerEmail)).Append("</div>");
        sb.Append("</td><td style=\"vertical-align:top;text-align:right\">");
        sb.Append("<div style=\"font-size:24px;font-weight:bold\">").Append(title).Append("</div>");
        sb.Append("<div style=\"font-size:16px\">").Append(E(number)).Append("</div>");
        if (creditsNumber is not null) sb.Append("<div style=\"color:#4b5563\">Credits invoice ").Append(E(creditsNumber)).Append("</div>");
        sb.Append("</td></tr></table>");

        sb.Append("<table style=\"width:100%;border-collapse:collapse;margin-top:24px\"><tr><td style=\"vertical-align:top\">");
        sb.Append("<div style=\"color:#6b7280;font-size:12px;text-transform:uppercase\">Bill to</div>");
        sb.Append("<div style=\"font-weight:bold\">").Append(E(tenantName)).Append("</div>");
        foreach (var e in billTo) sb.Append("<div style=\"color:#4b5563\">").Append(E(e)).Append("</div>");
        sb.Append("</td><td style=\"vertical-align:top;text-align:right\">");
        if (inv.IssuedAt is { } issued) Row(sb, "Issued", issued.UtcDateTime.ToString("MMM d, yyyy", Us));
        if (inv.PeriodStart is { } ps && inv.PeriodEnd is { } pe) Row(sb, "Period", $"{ps.ToString("MMM d", Us)} – {pe.ToString("MMM d, yyyy", Us)}");
        if (!isCredit && inv.DueOn is { } due) Row(sb, "Due", due.ToString("MMM d, yyyy", Us));
        if (inv.Status == InvoiceStatus.Paid && inv.PaidAt is { } paid) Row(sb, "Paid", paid.UtcDateTime.ToString("MMM d, yyyy", Us));
        sb.Append("</td></tr></table>");

        sb.Append("<table style=\"width:100%;border-collapse:collapse;margin-top:24px\">");
        sb.Append("<tr style=\"background:#f3f4f6\">")
          .Append(Th("Description", "left")).Append(Th("Qty", "right")).Append(Th("Rate", "right")).Append(Th("Amount", "right")).Append("</tr>");
        foreach (var l in inv.Lines.OrderBy(l => l.SortOrder))
        {
            var isMinutes = l.Kind is InvoiceLineKind.UsageLocal or InvoiceLineKind.UsageTollFree or InvoiceLineKind.UsageOutbound;
            sb.Append("<tr>");
            sb.Append("<td style=\"padding:8px;border-bottom:1px solid #e5e7eb\">").Append(E(l.Description));
            if (l.Reason is not null) sb.Append("<div style=\"color:#6b7280;font-size:12px\">").Append(E(l.Reason)).Append("</div>");
            sb.Append("</td>");
            sb.Append(Td(isMinutes ? $"{l.Quantity:N0} min" : l.Quantity.ToString("0.##", Us)));
            sb.Append(Td(isMinutes ? l.UnitPrice.ToString("$0.0000", Us) : Money(l.UnitPrice)));
            sb.Append(Td(Money(l.Amount)));
            sb.Append("</tr>");
        }
        sb.Append("<tr><td colspan=\"3\" style=\"padding:12px 8px;text-align:right;font-weight:bold\">")
          .Append(isCredit ? "Total credit" : "Total due").Append("</td>")
          .Append("<td style=\"padding:12px 8px;text-align:right;font-weight:bold;font-size:16px\">").Append(Money(inv.Total)).Append("</td></tr>");
        sb.Append("</table>");

        if (inv.Notes is not null)
            sb.Append("<div style=\"margin-top:16px;color:#374151;white-space:pre-line\">").Append(E(inv.Notes)).Append("</div>");
        if (!isCredit && inv.Status == InvoiceStatus.Issued)
            sb.Append("<div style=\"margin-top:24px;color:#4b5563\">Payable by ACH. Questions about this invoice? Reply to this email.</div>");
        sb.Append("</div></body></html>");
        return sb.ToString();

        static void Row(StringBuilder b, string label, string value) =>
            b.Append("<div><span style=\"color:#6b7280\">").Append(label).Append(":</span> ").Append(E(value)).Append("</div>");
        static string Th(string t, string align) =>
            $"<th style=\"padding:8px;text-align:{align};font-size:12px;color:#374151\">{t}</th>";
        static string Td(string t) => $"<td style=\"padding:8px;text-align:right;border-bottom:1px solid #e5e7eb;white-space:nowrap\">{E(t)}</td>";
    }
}
