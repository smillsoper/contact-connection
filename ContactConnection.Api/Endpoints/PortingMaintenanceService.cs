using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// Porting housekeeping (S184), hourly: 30 days after a port closes, its account PIN and the signer's bill copy are
/// deleted (the signed LOA and signature record are kept). Runs in the API because the bill copies live in its file store.
/// </summary>
public sealed class PortingMaintenanceService(IServiceScopeFactory scopes, ILogger<PortingMaintenanceService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); } catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await PurgeAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Porting maintenance failed."); }
            try { await Task.Delay(TimeSpan.FromHours(1), stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }

    private async Task PurgeAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var master = scope.ServiceProvider.GetRequiredService<ContactConnectionDbContext>();
        var blobs = scope.ServiceProvider.GetRequiredService<IBlobStorage>();
        var now = DateTimeOffset.UtcNow;
        var cutoff = now - PortOrder.SensitiveRetention;
        var due = await master.PortOrders.Where(o => o.CompletedAt != null && o.CompletedAt < cutoff && o.SensitivePurgedAt == null).ToListAsync(ct);
        foreach (var o in due)
        {
            if (o.PurgeSensitive(now) is { } bill)
            {
                try { await blobs.DeleteAsync(bill, ct); }
                catch (Exception ex) { logger.LogWarning(ex, "Porting: couldn't delete the bill copy for {Reference}", o.Reference); }
            }
        }
        if (due.Count > 0)
        {
            await master.SaveChangesAsync(ct);
            logger.LogInformation("Porting: deleted the PIN and bill copy of {Count} closed port(s)", due.Count);
        }
    }
}
