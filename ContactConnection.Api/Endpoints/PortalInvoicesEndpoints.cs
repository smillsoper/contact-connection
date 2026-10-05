using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// Tenant invoicing in the Portal (S179, Sprint 1 item 2) — see <see cref="IInvoiceService"/>. Platform admins only.
/// </summary>
public static class PortalInvoicesEndpoints
{
    public static IEndpointRouteBuilder MapPortalInvoicesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/portal").RequireAuthorization("PlatformAdmin");
        group.MapGet("invoices", List);
        group.MapPost("tenants/{tenantId:guid}/invoices", Create);
        group.MapGet("invoices/{id:guid}", Get);
        group.MapGet("invoices/{id:guid}/document", Document);
        group.MapPost("invoices/{id:guid}/refresh-usage", (Guid id, IInvoiceService svc, CancellationToken ct) =>
            Run(() => svc.RefreshUsageAsync(id, ct)));
        group.MapPost("invoices/{id:guid}/lines", AddLine);
        group.MapDelete("invoices/{id:guid}/lines/{lineId:guid}", (Guid id, Guid lineId, IInvoiceService svc, CancellationToken ct) =>
            Run(() => svc.RemoveLineAsync(id, lineId, ct)));
        group.MapPut("invoices/{id:guid}/notes", (Guid id, NotesBody body, IInvoiceService svc, CancellationToken ct) =>
            Run(() => svc.SetNotesAsync(id, body.Notes, ct)));
        group.MapPost("invoices/{id:guid}/issue", Issue);
        group.MapPost("invoices/{id:guid}/mark-paid", (Guid id, MarkPaidBody body, IInvoiceService svc, CancellationToken ct) =>
            Run(() => svc.MarkPaidAsync(id, body.PaidAt, body.Reference, ct)));
        group.MapPost("invoices/{id:guid}/void", (Guid id, VoidBody body, IInvoiceService svc, CancellationToken ct) =>
            Run(() => svc.VoidAsync(id, body.Reason ?? "", ct)));
        group.MapPost("invoices/{id:guid}/credit-note", CreditNote);
        group.MapDelete("invoices/{id:guid}", Delete);
        return app;
    }

    private static string? Actor(HttpContext http) => ActorResolver.Resolve(http.User)?.Name;

    private static async Task<IResult> List(Guid? tenantId, IInvoiceService svc, ContactConnectionDbContext db, CancellationToken ct)
    {
        var list = await svc.ListAsync(tenantId, ct);
        var names = await db.Tenants.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.DisplayName ?? t.Name, ct);
        return Results.Ok(list.Select(i => Summary(i, names.GetValueOrDefault(i.TenantId))));
    }

    private static async Task<IResult> Create(Guid tenantId, CreateInvoiceBody body, HttpContext http, IInvoiceService svc, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Month))
            return await Run(() => svc.CreateStandaloneDraftAsync(tenantId, Actor(http), ct));
        if (!DateOnly.TryParseExact(body.Month + "-01", "yyyy-MM-dd", out var first))
            return Results.BadRequest(new { error = "month must be yyyy-MM" });
        return await Run(() => svc.CreateMonthlyDraftAsync(tenantId, first.Year, first.Month, Actor(http), ct));
    }

    // An invoice with its credit notes (S179): issued credits reduce what's owed — the invoice itself stays frozen.
    private static async Task<IResult> Get(Guid id, IInvoiceService svc, ContactConnectionDbContext db, CancellationToken ct)
    {
        if (await svc.GetAsync(id, ct) is not { } invoice) return Results.NotFound();
        var credits = await db.Invoices.AsNoTracking().Where(c => c.CreditsInvoiceId == id).OrderBy(c => c.CreatedAt)
            .Select(c => new { c.Id, c.Number, c.Status, c.Total, c.IssuedAt }).ToListAsync(ct);
        var credited = credits.Where(c => c.Status is InvoiceStatus.Issued or InvoiceStatus.Paid).Sum(c => c.Total);
        string? creditsNumber = invoice.CreditsInvoiceId is { } originalId
            ? await db.Invoices.Where(o => o.Id == originalId).Select(o => o.Number).FirstOrDefaultAsync(ct)
            : null;
        return Results.Ok(new { invoice = Detail(invoice), creditNotes = credits, credited, net = invoice.Total + credited, creditsNumber });
    }

    private static async Task<IResult> Document(Guid id, IInvoiceService svc, CancellationToken ct) =>
        await svc.RenderHtmlAsync(id, ct) is { } html ? Results.Content(html, "text/html") : Results.NotFound();

    private static Task<IResult> AddLine(Guid id, AddLineBody body, HttpContext http, IInvoiceService svc, CancellationToken ct) =>
        Run(() => svc.AddLineAsync(id, body.Kind ?? "", body.Description ?? "", body.Quantity ?? 1, body.UnitPrice ?? 0, body.Reason,
            Actor(http), ct));

    private static async Task<IResult> Issue(Guid id, IInvoiceService svc, IStripeBillingService stripe, ILoggerFactory logs, CancellationToken ct)
    {
        try
        {
            var r = await svc.IssueAsync(id, ct);
            // Autopay (S179): charge the tenant's saved method now. A failure here never undoes the issue.
            InvoicePaymentResult? autopay = null;
            try { autopay = await stripe.AutopayAsync(id, ct); }
            catch (Exception ex) { logs.CreateLogger("Invoices").LogWarning(ex, "Autopay for invoice {Id} failed", id); }
            var invoice = autopay is null ? r.Invoice : await svc.GetAsync(id, ct) ?? r.Invoice;
            return Results.Ok(new { invoice = Detail(invoice), emailedTo = r.EmailedTo, emailError = r.EmailError, autopay });
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { return Results.BadRequest(new { error = ex.Message }); }
        catch (KeyNotFoundException) { return Results.NotFound(); }
    }

    private static Task<IResult> CreditNote(Guid id, CreditNoteBody body, HttpContext http, IInvoiceService svc, CancellationToken ct) =>
        Run(() => svc.CreateCreditNoteAsync(id, body.Amount ?? 0, body.Description ?? "", body.Reason ?? "", Actor(http), ct));

    private static async Task<IResult> Delete(Guid id, IInvoiceService svc, CancellationToken ct)
    {
        try { await svc.DeleteDraftAsync(id, ct); return Results.NoContent(); }
        catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (KeyNotFoundException) { return Results.NotFound(); }
    }

    private static async Task<IResult> Run(Func<Task<Invoice>> action)
    {
        try { return Results.Ok(Detail(await action())); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { return Results.BadRequest(new { error = ex.Message }); }
        catch (KeyNotFoundException ex) { return Results.NotFound(new { error = ex.Message }); }
    }

    private static object Summary(Invoice i, string? tenantName) => new
    {
        i.Id, i.TenantId, tenantName, i.Kind, i.Number, i.CreditsInvoiceId, i.PeriodStart, i.PeriodEnd, i.Status, i.Total,
        i.IssuedAt, i.DueOn, i.PaidAt, i.CreatedAt, i.PaymentState,
    };

    private static object Detail(Invoice i) => new
    {
        i.Id, i.TenantId, i.Kind, i.Number, i.CreditsInvoiceId, i.PeriodStart, i.PeriodEnd, i.Status, i.Total, i.Notes,
        i.BillToName, i.BillToEmail, i.IssuedAt, i.DueOn, i.PaidAt, i.PaymentReference, i.VoidedAt, i.VoidReason,
        i.PaymentState, i.PaymentError, i.PaymentAttempts, i.StripePaymentIntentId,
        i.CreatedBy, i.CreatedAt, i.UpdatedAt,
        lines = i.Lines.OrderBy(l => l.SortOrder).Select(l => new
        {
            l.Id, l.Kind, l.Description, l.Quantity, l.UnitPrice, l.Amount, l.Reason, l.CreatedBy, l.CreatedAt,
            metered = InvoiceLineKind.IsMetered(l.Kind),
        }),
    };

    public record CreateInvoiceBody(string? Month);
    public record AddLineBody(string? Kind, string? Description, decimal? Quantity, decimal? UnitPrice, string? Reason);
    public record NotesBody(string? Notes);
    public record MarkPaidBody(DateTimeOffset? PaidAt, string? Reference);
    public record VoidBody(string? Reason);
    public record CreditNoteBody(decimal? Amount, string? Description, string? Reason);
}
