using System.Globalization;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stripe;
using Invoice = ContactConnection.Domain.Entities.Invoice;
using Tenant = ContactConnection.Domain.Entities.Tenant;

namespace ContactConnection.Infrastructure.Billing;

/// <summary>See <see cref="IStripeBillingService"/>. Keys: Stripe:SecretKey, Stripe:PublishableKey, Stripe:WebhookSecret.</summary>
public class StripeBillingService(
    ContactConnectionDbContext db,
    IConfiguration config,
    IServiceProvider services,
    ILogger<StripeBillingService> logger) : IStripeBillingService
{
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");
    private string? SecretKey => config["Stripe:SecretKey"] is { Length: > 0 } k ? k : null;
    private string? WebhookSecret => config["Stripe:WebhookSecret"] is { Length: > 0 } k ? k : null;

    public bool IsConfigured => SecretKey is not null;
    public string? PublishableKey => config["Stripe:PublishableKey"] is { Length: > 0 } k ? k : null;

    private StripeClient Client => new(SecretKey ?? throw new InvalidOperationException("Stripe isn't configured on this server."));

    // ── Payment method ─────────────────────────────────────────────────────

    public async Task<TenantPaymentMethod> GetPaymentMethodAsync(Guid tenantId, CancellationToken ct = default) =>
        View(await TenantAsync(tenantId, ct));

    public async Task<string> CreateSetupIntentAsync(Guid tenantId, CancellationToken ct = default)
    {
        var tenant = await TenantAsync(tenantId, ct);
        var customerId = await EnsureCustomerAsync(tenant, ct);
        var intent = await new SetupIntentService(Client).CreateAsync(new SetupIntentCreateOptions
        {
            Customer = customerId,
            Usage = "off_session",   // we charge it later, when an invoice is issued / paid
            AllowedPaymentMethodTypes = ["us_bank_account", "card"],
            PaymentMethodOptions = new SetupIntentPaymentMethodOptionsOptions
            {
                UsBankAccount = new SetupIntentPaymentMethodOptionsUsBankAccountOptions
                {
                    FinancialConnections = new SetupIntentPaymentMethodOptionsUsBankAccountFinancialConnectionsOptions
                    {
                        Permissions = ["payment_method"],
                    },
                    VerificationMethod = "automatic",
                },
            },
            Metadata = new() { ["tenant_id"] = tenant.Id.ToString() },
        }, cancellationToken: ct);
        return intent.ClientSecret;
    }

    public async Task<TenantPaymentMethod> SavePaymentMethodAsync(Guid tenantId, string setupIntentId, CancellationToken ct = default)
    {
        var tenant = await TenantAsync(tenantId, ct);
        var intent = await new SetupIntentService(Client).GetAsync(setupIntentId, new SetupIntentGetOptions { Expand = ["payment_method"] },
            cancellationToken: ct);
        if (intent.CustomerId != tenant.StripeCustomerId) throw new InvalidOperationException("That setup isn't for this account.");
        if (intent.Status != "succeeded")
            throw new InvalidOperationException(intent.Status == "requires_action"
                ? "Your bank account still needs verifying — follow the steps Stripe gave you, then try again."
                : $"The payment method wasn't saved ({intent.Status}).");

        var pm = intent.PaymentMethod ?? throw new InvalidOperationException("No payment method came back from Stripe.");
        await MakeDefaultAsync(tenant, pm, ct);
        return View(tenant);
    }

    private async Task MakeDefaultAsync(Tenant tenant, PaymentMethod pm, CancellationToken ct)
    {
        await new CustomerService(Client).UpdateAsync(tenant.StripeCustomerId!, new CustomerUpdateOptions
        {
            InvoiceSettings = new CustomerInvoiceSettingsOptions { DefaultPaymentMethod = pm.Id },
        }, cancellationToken: ct);
        tenant.SetPaymentMethod(pm.Id, pm.Type, Label(pm));
        await db.SaveChangesAsync(ct);
    }

    public async Task<TenantPaymentMethod> SetAutopayAsync(Guid tenantId, bool enabled, CancellationToken ct = default)
    {
        var tenant = await TenantAsync(tenantId, ct);
        tenant.SetAutopay(enabled);
        await db.SaveChangesAsync(ct);
        return View(tenant);
    }

    // ── Paying invoices ────────────────────────────────────────────────────

    public async Task<InvoicePaymentResult> PayInvoiceAsync(Guid tenantId, Guid invoiceId, CancellationToken ct = default)
    {
        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId && i.TenantId == tenantId, ct)
            ?? throw new KeyNotFoundException("Invoice not found.");
        return await ChargeAsync(await TenantAsync(tenantId, ct), invoice, ct);
    }

    public async Task<InvoicePaymentResult?> AutopayAsync(Guid invoiceId, CancellationToken ct = default)
    {
        if (!IsConfigured) return null;
        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId, ct);
        if (invoice is null || invoice.Kind != InvoiceKind.Invoice || invoice.Status != InvoiceStatus.Issued) return null;
        var tenant = await TenantAsync(invoice.TenantId, ct);
        if (!tenant.AutopayEnabled || tenant.PaymentMethodId is null) return null;
        try { return await ChargeAsync(tenant, invoice, ct); }
        catch (InvalidOperationException ex) { return new InvoicePaymentResult("failed", ex.Message); }
    }

    private async Task<InvoicePaymentResult> ChargeAsync(Tenant tenant, Invoice invoice, CancellationToken ct)
    {
        if (tenant.PaymentMethodId is null || tenant.StripeCustomerId is null)
            throw new InvalidOperationException("Add a payment method on the Billing page first.");

        // What's still owed: the invoice less any credit notes issued against it.
        var credited = await db.Invoices.Where(c => c.CreditsInvoiceId == invoice.Id
                && (c.Status == InvoiceStatus.Issued || c.Status == InvoiceStatus.Paid))
            .SumAsync(c => (decimal?)c.Total, ct) ?? 0m;
        var owed = invoice.Total + credited;
        if (owed <= 0) throw new InvalidOperationException("Nothing is owed on this invoice.");

        var attempt = invoice.BeginPayment();
        await db.SaveChangesAsync(ct);

        PaymentIntent intent;
        try
        {
            intent = await new PaymentIntentService(Client).CreateAsync(new PaymentIntentCreateOptions
            {
                Amount = (long)Math.Round(owed * 100m, MidpointRounding.AwayFromZero),
                Currency = "usd",
                Customer = tenant.StripeCustomerId,
                PaymentMethod = tenant.PaymentMethodId,
                AllowedPaymentMethodTypes = [tenant.PaymentMethodType ?? "card"],
                Confirm = true,
                OffSession = true,
                Description = $"Invoice {invoice.Number}",
                Metadata = new()
                {
                    ["invoice_id"] = invoice.Id.ToString(),
                    ["invoice_number"] = invoice.Number ?? "",
                    ["tenant_id"] = tenant.Id.ToString(),
                },
            }, new RequestOptions { IdempotencyKey = $"invoice-{invoice.Id}-attempt-{attempt}" }, ct);
        }
        catch (StripeException ex)
        {
            // Card declines arrive as exceptions; the PaymentIntent (if one was made) is on the error.
            var message = ex.StripeError?.Message ?? ex.Message;
            invoice.PaymentFailed(ex.StripeError?.PaymentIntent?.Id, message);
            await db.SaveChangesAsync(ct);
            logger.LogWarning("Stripe payment for invoice {Number} failed: {Message}", invoice.Number, message);
            return new InvoicePaymentResult("failed", message);
        }

        var result = Apply(invoice, intent);
        await db.SaveChangesAsync(ct);
        return result;
    }

    /// <summary>Records a PaymentIntent's state on the invoice (shared by the charge and the webhooks).</summary>
    private static InvoicePaymentResult Apply(Invoice invoice, PaymentIntent intent)
    {
        switch (intent.Status)
        {
            case "succeeded":
                invoice.PaymentSucceeded(intent.Id, DateTimeOffset.UtcNow);
                return new("paid", null);
            case "processing":
                invoice.PaymentProcessing(intent.Id);
                return new("processing", "Bank payment submitted — it usually clears in about 4 business days.");
            default:
                var message = intent.LastPaymentError?.Message
                    ?? (intent.Status == "requires_action" ? "This card needs the cardholder to authenticate — use a different payment method."
                        : $"Payment didn't go through ({intent.Status}).");
                invoice.PaymentFailed(intent.Id, message);
                return new("failed", message);
        }
    }

    public async Task<string> RefundAsync(string paymentIntentId, decimal amount, Guid creditNoteId, CancellationToken ct = default)
    {
        var refund = await new RefundService(Client).CreateAsync(new RefundCreateOptions
        {
            PaymentIntent = paymentIntentId,
            Amount = (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero),
            Metadata = new() { ["credit_note_id"] = creditNoteId.ToString() },
        }, new RequestOptions { IdempotencyKey = $"refund-{creditNoteId}" }, ct);
        if (refund.Status is "failed" or "canceled")
            throw new InvalidOperationException($"Stripe didn't refund it ({refund.Status}{(refund.FailureReason is { } r ? $": {r}" : "")}).");
        return refund.Id;
    }

    // ── Webhooks ──────────────────────────────────────────────────────────

    public async Task<bool> HandleWebhookAsync(string json, string? signature, CancellationToken ct = default)
    {
        if (WebhookSecret is null) throw new InvalidOperationException("Stripe:WebhookSecret isn't configured.");
        Event stripeEvent;
        try { stripeEvent = EventUtility.ConstructEvent(json, signature, WebhookSecret, throwOnApiVersionMismatch: false); }
        catch (StripeException) { return false; }

        switch (stripeEvent.Type)
        {
            case EventTypes.PaymentIntentSucceeded:
            case EventTypes.PaymentIntentProcessing:
            case EventTypes.PaymentIntentPaymentFailed:
                if (stripeEvent.Data.Object is PaymentIntent intent && await InvoiceForAsync(intent.Id, intent.Metadata, ct) is { } invoice)
                {
                    var wasPaid = invoice.Status == InvoiceStatus.Paid;
                    var result = Apply(invoice, intent);
                    await db.SaveChangesAsync(ct);
                    logger.LogInformation("Stripe {Type}: invoice {Number} → {State}", stripeEvent.Type, invoice.Number, result.State);
                    if (result.State == "failed" && !wasPaid) await EmailFailureAsync(invoice, result.Message, ct);
                }
                break;
            case EventTypes.SetupIntentSucceeded:
                // A bank account entered by hand is verified later (micro-deposits) — this is when it becomes the tenant's
                // payment method. Also harmless after an instant save (same method, already the default).
                if (stripeEvent.Data.Object is SetupIntent setup && setup.CustomerId is { } customerId && setup.PaymentMethodId is { } pmId
                    && await db.Tenants.FirstOrDefaultAsync(t => t.StripeCustomerId == customerId, ct) is { } owner
                    && owner.PaymentMethodId != pmId)
                {
                    var method = await new PaymentMethodService(Client).GetAsync(pmId, cancellationToken: ct);
                    await MakeDefaultAsync(owner, method, ct);
                    logger.LogInformation("Stripe setup_intent.succeeded: {Tenant} now pays with {Label}", owner.Subdomain, owner.PaymentMethodLabel);
                }
                break;
            case EventTypes.ChargeDisputeCreated:
                if (stripeEvent.Data.Object is Dispute dispute && dispute.PaymentIntentId is { } piId
                    && await InvoiceForAsync(piId, null, ct) is { } disputed)
                {
                    disputed.PaymentDisputed(dispute.Reason ?? "unknown");
                    await db.SaveChangesAsync(ct);
                    logger.LogWarning("Stripe dispute on invoice {Number}: {Reason}", disputed.Number, dispute.Reason);
                }
                break;
        }
        return true;
    }

    private async Task<Invoice?> InvoiceForAsync(string paymentIntentId, Dictionary<string, string>? metadata, CancellationToken ct)
    {
        if (metadata is not null && metadata.TryGetValue("invoice_id", out var idText) && Guid.TryParse(idText, out var id))
            return await db.Invoices.FirstOrDefaultAsync(i => i.Id == id, ct);
        return await db.Invoices.FirstOrDefaultAsync(i => i.StripePaymentIntentId == paymentIntentId, ct);
    }

    private async Task EmailFailureAsync(Invoice invoice, string? reason, CancellationToken ct)
    {
        var tenant = await TenantAsync(invoice.TenantId, ct);
        var to = InvoiceService.BillingEmails(tenant);
        if (to.Count == 0) return;
        try
        {
            var email = (IEmailService)services.GetRequiredService(typeof(IEmailService));
            var issuer = config["Billing:Issuer:Name"] is { Length: > 0 } n ? n : "ContactConnection";
            await email.SendAsync(new EmailMessage
            {
                To = to,
                FromName = issuer,
                ReplyTo = config["Billing:Issuer:Email"],
                Subject = $"Payment for invoice {invoice.Number} didn't go through",
                HtmlBody = $"<p>We couldn't collect payment for invoice <b>{invoice.Number}</b> ({invoice.Total.ToString("C", Us)}).</p>"
                         + $"<p>Reason: {System.Net.WebUtility.HtmlEncode(reason ?? "not given")}</p>"
                         + "<p>Please update your payment method on the Billing page, or reply to this email.</p>",
            }, ct);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Payment-failed email for invoice {Number} didn't send", invoice.Number); }
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private async Task<Tenant> TenantAsync(Guid id, CancellationToken ct) =>
        await db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new KeyNotFoundException("Tenant not found.");

    private async Task<string> EnsureCustomerAsync(Tenant tenant, CancellationToken ct)
    {
        if (tenant.StripeCustomerId is { } existing) return existing;
        var emails = InvoiceService.BillingEmails(tenant);
        var customer = await new CustomerService(Client).CreateAsync(new CustomerCreateOptions
        {
            Name = tenant.DisplayName ?? tenant.Name,
            Email = emails.FirstOrDefault(),
            Metadata = new() { ["tenant_id"] = tenant.Id.ToString(), ["subdomain"] = tenant.Subdomain },
        }, new RequestOptions { IdempotencyKey = $"customer-{tenant.Id}" }, ct);
        tenant.SetStripeCustomer(customer.Id);
        await db.SaveChangesAsync(ct);
        return customer.Id;
    }

    private static TenantPaymentMethod View(Tenant t) =>
        new(t.PaymentMethodId is not null, t.PaymentMethodType, t.PaymentMethodLabel, t.AutopayEnabled);

    private static string Label(PaymentMethod pm) => pm.Type switch
    {
        "card" when pm.Card is { } c => $"{Title(c.Brand)} •••• {c.Last4}",
        "us_bank_account" when pm.UsBankAccount is { } b => $"{b.BankName ?? "Bank account"} •••• {b.Last4}",
        _ => pm.Type,
    };

    private static string Title(string? s) => string.IsNullOrEmpty(s) ? "Card" : char.ToUpperInvariant(s[0]) + s[1..];
}
