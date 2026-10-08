using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Worker;

/// <summary>
/// Team chat uploads no message uses (S183): pasted / attached and never sent, removed in an edit, or left behind by a deleted
/// message. After <see cref="ChatFile.OrphanGrace"/> (time to finish writing a message) the blob and its row are removed.
/// Hourly, per tenant, in batches.
/// </summary>
public sealed class ChatFileCleanupService(IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<ChatFileCleanupService> logger)
    : BackgroundService
{
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(int.TryParse(config["Chat:FileCleanup:PollMinutes"], out var m) && m > 0 ? m : 60);
    private const int BatchSize = 200;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ChatFileCleanupService started — every {Minutes}m, grace {Hours}h.", _interval.TotalMinutes, ChatFile.OrphanGrace.TotalHours);
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunCycleAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Chat file cleanup cycle failed."); }
            try { await Task.Delay(_interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        List<Tenant> tenants;
        using (var scope = scopeFactory.CreateScope())
            tenants = await scope.ServiceProvider.GetRequiredService<ContactConnectionDbContext>().Tenants.Where(t => t.IsActive).ToListAsync(ct);

        foreach (var tenant in tenants)
        {
            if (ct.IsCancellationRequested) return;
            try { await CleanTenantAsync(tenant, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Chat file cleanup failed for tenant {Subdomain}.", tenant.Subdomain);
            }
        }
    }

    private async Task CleanTenantAsync(Tenant tenant, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Current = tenant;
        var blobs = scope.ServiceProvider.GetRequiredService<IBlobStorage>();
        await using var db = scope.ServiceProvider.GetRequiredService<ScopedTenantDbContextFactory>().Create();

        // Raised-hand screen pictures (S183) are kept a week.
        var snapCutoff = DateTimeOffset.UtcNow - HelpRequest.SnapshotKeep;
        var oldSnaps = await db.HelpRequests.Where(h => h.SnapshotKey != null && h.CreatedAt < snapCutoff).Take(BatchSize).ToListAsync(ct);
        foreach (var h in oldSnaps)
        {
            try { await blobs.DeleteAsync(h.SnapshotKey!, ct); }
            catch (Exception ex) { logger.LogWarning(ex, "Couldn't delete help snapshot {Key}.", h.SnapshotKey); }
            h.SetSnapshot(null);
        }
        if (oldSnaps.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Chat cleanup: removed {Count} raised-hand screen picture(s) for {Subdomain}.", oldSnaps.Count, tenant.Subdomain);
        }

        var cutoff = DateTimeOffset.UtcNow - ChatFile.OrphanGrace;
        var orphans = await db.ChatFiles
            .Where(f => f.CreatedAt < cutoff
                && !db.ChatMessages.Any(m => m.DeletedAt == null
                    && (m.AttachmentIds.Contains(f.Id) || m.Body.Contains("data-chat-file=\"" + f.Id.ToString() + "\""))))
            .OrderBy(f => f.CreatedAt).Take(BatchSize).ToListAsync(ct);
        if (orphans.Count == 0) return;

        foreach (var f in orphans)
        {
            try { await blobs.DeleteAsync(f.StorageKey, ct); }
            catch (Exception ex) { logger.LogWarning(ex, "Couldn't delete chat file blob {Key} — removing the row anyway.", f.StorageKey); }
        }
        db.ChatFiles.RemoveRange(orphans);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Chat file cleanup: removed {Count} unused upload(s) for {Subdomain}.", orphans.Count, tenant.Subdomain);
    }
}
