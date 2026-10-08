using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Media;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Worker;

/// <summary>
/// Keeps <c>public.broadcast_stations</c> current from the FCC's daily LMS dump (Media Agency Phase B,
/// S171) — the searchable station list behind media assignments and nearest-station attribution for
/// Local buys. Checks hourly; imports when the table is empty or the last import is older than
/// <c>Fcc:Import:MaxAgeHours</c> (default 24). Platform-wide data, so one import serves every tenant.
/// </summary>
public sealed class BroadcastStationImportService(
    IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<BroadcastStationImportService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (bool.TryParse(config["Fcc:Import:Enabled"], out var enabled) && !enabled)
        {
            logger.LogInformation("BroadcastStationImportService disabled (Fcc:Import:Enabled=false).");
            return;
        }
        var maxAge = TimeSpan.FromHours(int.TryParse(config["Fcc:Import:MaxAgeHours"], out var h) ? h : 24);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ContactConnectionDbContext>();
                var last = await db.BroadcastStations.MaxAsync(s => (DateTimeOffset?)s.ImportedAt, stoppingToken);
                if (last is null || DateTimeOffset.UtcNow - last > maxAge)
                    await scope.ServiceProvider.GetRequiredService<FccStationImporter>().ImportAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                // Stale stations are harmless for a day; retry on the next pass.
                logger.LogWarning(ex, "FCC station import failed — will retry next hour");
            }
            JobHeartbeats.Report("FCC station import", TimeSpan.FromHours(1));
            try { await Task.Delay(TimeSpan.FromHours(1), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
