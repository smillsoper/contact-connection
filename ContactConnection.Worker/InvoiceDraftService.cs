using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Billing;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Worker;

/// <summary>
/// Monthly invoice drafts (S179, Sprint 1 item 2). Once it's the 1st or later in a tenant's time zone, builds a DRAFT
/// invoice for the previous month from the usage meter — never issues it; a platform admin reviews and issues it in the
/// Portal. Only tenants with billing set up (a saved rate or monthly minimum) and that existed in that month. Idempotent:
/// <see cref="IInvoiceService.CreateMonthlyDraftAsync"/> returns the existing live invoice, so the hourly pass is safe.
/// </summary>
public sealed class InvoiceDraftService(IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<InvoiceDraftService> logger)
    : BackgroundService
{
    private readonly bool _enabled = !bool.TryParse(config["Billing:Drafts:Enabled"], out var en) || en;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(
        int.TryParse(config["Billing:Drafts:PollMinutes"], out var m) && m > 0 ? m : 60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled) { logger.LogInformation("InvoiceDraftService disabled (Billing:Drafts:Enabled=false)."); return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunCycleAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Invoice draft pass failed."); }
            try { await Task.Delay(_interval, stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var platformDb = scope.ServiceProvider.GetRequiredService<ContactConnectionDbContext>();
        var invoices = scope.ServiceProvider.GetRequiredService<IInvoiceService>();

        var tenants = await platformDb.Tenants.AsNoTracking()
            .Where(t => t.IsActive && (t.BillingRatePerMinute != null || t.BillingMonthlyMinimum != null))
            .ToListAsync(ct);
        foreach (var tenant in tenants)
        {
            var local = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, UsageMeter.ZoneFor(tenant));
            var lastMonth = new DateTime(local.Year, local.Month, 1).AddMonths(-1);
            var createdLocal = TimeZoneInfo.ConvertTime(tenant.CreatedAt, UsageMeter.ZoneFor(tenant));
            if (new DateTime(createdLocal.Year, createdLocal.Month, 1) > lastMonth) continue;   // didn't exist yet

            try
            {
                var draft = await invoices.CreateMonthlyDraftAsync(tenant.Id, lastMonth.Year, lastMonth.Month, "monthly draft", ct);
                if (draft.IsDraft && draft.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(-5))
                    logger.LogInformation("Invoice draft for {Tenant} {Month:yyyy-MM}: ${Total:0.00}", tenant.Subdomain, lastMonth, draft.Total);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Invoice draft for {Tenant} {Month:yyyy-MM} failed", tenant.Subdomain, lastMonth);
            }
        }
    }
}
