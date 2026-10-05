namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// Tenants paying our invoices through Stripe (S179, Sprint 1 item 4). Stripe holds the card / bank details; we keep the
/// customer id, the default payment method's id and a display label. Payments are PaymentIntents against the saved method
/// (off-session, idempotent per attempt); the outcome arrives by webhook — ACH takes days, so an invoice is only Paid
/// once Stripe says the money cleared. Rule violations throw <see cref="InvalidOperationException"/>.
/// </summary>
public interface IStripeBillingService
{
    bool IsConfigured { get; }
    string? PublishableKey { get; }

    Task<TenantPaymentMethod> GetPaymentMethodAsync(Guid tenantId, CancellationToken ct = default);
    /// <summary>Starts saving a payment method — the client secret for Stripe's Payment Element.</summary>
    Task<string> CreateSetupIntentAsync(Guid tenantId, CancellationToken ct = default);
    /// <summary>After the browser confirmed the SetupIntent: make its method the tenant's default.</summary>
    Task<TenantPaymentMethod> SavePaymentMethodAsync(Guid tenantId, string setupIntentId, CancellationToken ct = default);
    Task<TenantPaymentMethod> SetAutopayAsync(Guid tenantId, bool enabled, CancellationToken ct = default);

    /// <summary>Charges the saved method for what's still owed on the invoice (total less issued credits).</summary>
    Task<InvoicePaymentResult> PayInvoiceAsync(Guid tenantId, Guid invoiceId, CancellationToken ct = default);
    /// <summary>Called when an invoice is issued: charges it when the tenant has autopay on. Null = nothing to do.</summary>
    Task<InvoicePaymentResult?> AutopayAsync(Guid invoiceId, CancellationToken ct = default);

    /// <summary>A Stripe webhook: verified against Stripe:WebhookSecret, then applied. False = bad signature.</summary>
    Task<bool> HandleWebhookAsync(string json, string? signature, CancellationToken ct = default);
}

public sealed record TenantPaymentMethod(bool Configured, string? Type, string? Label, bool Autopay);

/// <summary>State: paid | processing | failed.</summary>
public sealed record InvoicePaymentResult(string State, string? Message);
