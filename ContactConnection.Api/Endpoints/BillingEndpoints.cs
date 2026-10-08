using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// The tenant's Billing page (S179, Sprint 1 item 4): their payment method (Stripe Payment Element via SetupIntent), autopay,
/// their invoices and Pay now. Needs billing.manage. Plus Stripe's webhook (anonymous, signature-verified).
/// </summary>
public static class BillingEndpoints
{
    public static IEndpointRouteBuilder MapBillingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/billing").RequireAuthorization().AddEndpointFilter(RequireBillingPermission);
        group.MapGet("config", (IStripeBillingService stripe) =>
            Results.Ok(new { configured = stripe.IsConfigured, publishableKey = stripe.PublishableKey }));
        group.MapGet("payment-method", (TenantContext t, IStripeBillingService stripe, CancellationToken ct) =>
            Run(() => stripe.GetPaymentMethodAsync(t.Current!.Id, ct)));
        group.MapPost("payment-method/setup-intent", async (TenantContext t, IStripeBillingService stripe, CancellationToken ct) =>
            await Run(async () => new { clientSecret = await stripe.CreateSetupIntentAsync(t.Current!.Id, ct) }));
        group.MapPost("payment-method", (SavePaymentMethodBody body, TenantContext t, IStripeBillingService stripe, CancellationToken ct) =>
            Run(() => stripe.SavePaymentMethodAsync(t.Current!.Id, body.SetupIntentId ?? "", ct)));
        group.MapPut("autopay", (AutopayBody body, TenantContext t, IStripeBillingService stripe, CancellationToken ct) =>
            Run(() => stripe.SetAutopayAsync(t.Current!.Id, body.Enabled, ct)));
        group.MapGet("invoices", Invoices);
        group.MapGet("invoices/{id:guid}/document", Document);
        group.MapPost("invoices/{id:guid}/pay", (Guid id, TenantContext t, IStripeBillingService stripe, CancellationToken ct) =>
            Run(() => stripe.PayInvoiceAsync(t.Current!.Id, id, ct)));

        // Stripe → us. Anonymous; the Stripe-Signature header is verified against Stripe:WebhookSecret.
        app.MapPost("/api/v1/stripe/webhook", Webhook).AllowAnonymous();
        return app;
    }

    private static async ValueTask<object?> RequireBillingPermission(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var tenant = http.RequestServices.GetRequiredService<TenantContext>();
        if (!tenant.HasTenant) return Results.Unauthorized();
        var permissions = (http.User.FindFirst("permissions")?.Value ?? "").Split(',');
        if (!permissions.Contains(Permission.BillingManage, StringComparer.OrdinalIgnoreCase)) return Results.Forbid();
        return await next(context);
    }

    /// <summary>Issued invoices and credit notes (never drafts), with what's still owed after credits.</summary>
    private static async Task<IResult> Invoices(TenantContext t, ContactConnectionDbContext db, CancellationToken ct)
    {
        var tenantId = t.Current!.Id;
        var list = await db.Invoices.AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.Status != InvoiceStatus.Draft)
            .OrderByDescending(i => i.IssuedAt).ToListAsync(ct);
        var credits = list.Where(i => i.Kind == InvoiceKind.CreditNote && i.Status != InvoiceStatus.Void && i.CreditsInvoiceId != null)
            .GroupBy(i => i.CreditsInvoiceId!.Value).ToDictionary(g => g.Key, g => g.Sum(c => c.Total));
        return Results.Ok(list.Select(i => new
        {
            i.Id, i.Kind, i.Number, i.PeriodStart, i.PeriodEnd, i.Status, i.Total, i.IssuedAt, i.DueOn, i.PaidAt,
            i.PaymentState, i.PaymentError, i.CreditsInvoiceId, i.CreditDisposition,
            credited = credits.GetValueOrDefault(i.Id),
            owed = i.Kind == InvoiceKind.Invoice && i.Status == InvoiceStatus.Issued ? Math.Max(0, i.Total + credits.GetValueOrDefault(i.Id)) : 0m,
        }));
    }

    private static async Task<IResult> Document(Guid id, TenantContext t, IInvoiceService invoices, CancellationToken ct)
    {
        var invoice = await invoices.GetAsync(id, ct);
        if (invoice is null || invoice.TenantId != t.Current!.Id || invoice.IsDraft) return Results.NotFound();
        return await invoices.RenderHtmlAsync(id, ct) is { } html ? Results.Content(html, "text/html") : Results.NotFound();
    }

    private static async Task<IResult> Webhook(HttpContext http, IStripeBillingService stripe, ILoggerFactory logs,
        ContactConnection.Infrastructure.Health.IIntegrationHealth health, CancellationToken ct)
    {
        using var reader = new StreamReader(http.Request.Body);
        var json = await reader.ReadToEndAsync(ct);
        try
        {
            var ok = await stripe.HandleWebhookAsync(json, http.Request.Headers["Stripe-Signature"].ToString(), ct);
            // S184 health: a bad signature usually means our webhook secret no longer matches Stripe's.
            health.Record("stripe-webhook", ok, ok ? null : "Signature did not match - check Stripe:WebhookSecret");
            return ok ? Results.Ok() : Results.BadRequest(new { error = "Invalid signature." });
        }
        catch (InvalidOperationException ex)
        {
            health.Record("stripe-webhook", false, ex.Message);
            logs.CreateLogger("StripeWebhook").LogWarning("Stripe webhook not processed: {Message}", ex.Message);
            return Results.StatusCode(503);
        }
    }

    private static async Task<IResult> Run<T>(Func<Task<T>> action)
    {
        try { return Results.Ok(await action()); }
        catch (Stripe.StripeException ex) { return Results.BadRequest(new { error = ex.StripeError?.Message ?? ex.Message }); }
        catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (KeyNotFoundException ex) { return Results.NotFound(new { error = ex.Message }); }
    }

    public record SavePaymentMethodBody(string? SetupIntentId);
    public record AutopayBody(bool Enabled);
}
