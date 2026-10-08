using System.Text.Json;
using ContactConnection.Api.Endpoints;
using ContactConnection.Api.Telephony;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Health;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace ContactConnection.Api.Health;

// Platform health, API side (S184). The Worker runs the checks; the API reports its own heartbeat and what only it knows,
// watches the Worker, relays changes to the Portal's Health page live, and serves the page.

public interface IPlatformHubClient
{
    Task ReceiveHealthChanged();
}

/// <summary>The Platform Portal's live connection (Portal sign-ins only).</summary>
[Authorize(Policy = "PlatformAdmin")]
public sealed class PlatformHub : Hub<IPlatformHubClient>
{
    public const string Group = "platform";
    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, Group);
        await base.OnConnectedAsync();
    }
}

/// <summary>
/// Every 20 s: the API's heartbeat + facts (FreeSWITCH event connection, agents' softphone connections) for the Worker.
/// Every minute: if the Worker has stopped reporting, the API records it (and so alerts) itself. Relays health changes
/// to the Portal.
/// </summary>
public sealed class HealthReporterService(IConnectionMultiplexer redis, AgentHealthStore agents, IServiceScopeFactory scopes,
    IHubContext<PlatformHub, IPlatformHubClient> hub, ILogger<HealthReporterService> logger) : BackgroundService
{
    private static readonly DateTimeOffset StartedAt = DateTimeOffset.UtcNow;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var channel = RedisChannel.Literal(HealthRecorder.ChangedChannel);
        await redis.GetSubscriber().SubscribeAsync(channel, (_, _) => _ = hub.Clients.Group(PlatformHub.Group).ReceiveHealthChanged());

        var tick = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var (onCall, poor) = agents.Summary();
                var facts = new ApiFacts(DateTimeOffset.UtcNow, EslBackgroundService.ConnectedSince, onCall, poor, StartedAt);
                await redis.GetDatabase().StringSetAsync(HealthKeys.ApiFacts, JsonSerializer.Serialize(facts), TimeSpan.FromHours(1));
                if (++tick % 3 == 0) await WatchWorkerAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Health reporter round failed."); }
            try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); } catch (OperationCanceledException) { break; }
        }
        await redis.GetSubscriber().UnsubscribeAsync(channel);
    }

    private async Task WatchWorkerAsync(CancellationToken ct)
    {
        var raw = await redis.GetDatabase().StringGetAsync(HealthKeys.WorkerHeartbeat);
        DateTimeOffset? last = !raw.IsNullOrEmpty && DateTimeOffset.TryParse(raw.ToString(), out var at) ? at : null;
        var age = last is { } l ? (DateTimeOffset.UtcNow - l).TotalSeconds : double.MaxValue;
        var def = HealthCatalog.Find("worker")!;
        if (age < (def.Warn ?? 150)) return;   // the Worker records itself while it's running
        using var scope = scopes.CreateScope();
        await scope.ServiceProvider.GetRequiredService<HealthRecorder>().RecordAsync(
        [
            last is null
                ? new HealthResult("worker", null, "The Worker isn't reporting — background jobs and health checks have stopped.", HealthStatus.Critical)
                : new HealthResult("worker", Math.Round(age), $"Last report {last:HH:mm} UTC — background jobs and health checks have stopped."),
        ], ct);
    }
}

public static class PlatformHealthEndpoints
{
    public static IEndpointRouteBuilder MapPlatformHealthEndpoints(this IEndpointRouteBuilder app)
    {
        // For an outside uptime monitor: just up / down, nothing else.
        app.MapGet("/api/v1/health", Ping).AllowAnonymous();

        var g = app.MapGroup("/api/v1/portal/health").RequireAuthorization("PlatformAdmin");
        g.MapGet("", Overview);
        g.MapPost("acknowledge", Acknowledge);
        g.MapPut("levels", SetLevels).RequireAuthorization("PlatformOwner");
        g.MapPost("mute", Mute).RequireAuthorization("PlatformOwner");
        g.MapPut("recipients", SetRecipients).RequireAuthorization("PlatformOwner");
        g.MapDelete("people/{oid}", RemovePerson).RequireAuthorization("PlatformOwner");
        g.MapPost("test-alert", TestAlert).RequireAuthorization("PlatformOwner");
        return app;
    }

    private static async Task<IResult> Ping(ContactConnectionDbContext db, IConnectionMultiplexer redis, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            await db.Database.ExecuteSqlRawAsync("SELECT 1", timeout.Token);
            await redis.GetDatabase().PingAsync();
            return Results.Ok(new { status = "ok" });
        }
        catch { return Results.Json(new { status = "unhealthy" }, statusCode: StatusCodes.Status503ServiceUnavailable); }
    }

    private static string Who(HttpContext http) =>
        $"{http.User.FindFirst("given_name")?.Value} {http.User.FindFirst("family_name")?.Value}".Trim() is { Length: > 0 } n ? n : http.User.FindFirst("email")?.Value ?? "Portal user";

    private static async Task<IResult> Overview(HttpContext http, ContactConnectionDbContext db, HealthRecorder recorder, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var checks = await db.HealthChecks.AsNoTracking().ToListAsync(ct);
        var since = now.AddHours(-24);
        var samples = await db.HealthSamples.AsNoTracking().Where(s => s.At >= since).Select(s => new { s.Key, s.At, s.Status }).ToListAsync(ct);
        // History strip: the worst status in each 15-minute slot of the last 24 hours (96 slots, oldest first).
        var slots = samples.GroupBy(s => s.Key).ToDictionary(g => g.Key, g =>
        {
            var arr = new string[96];
            foreach (var s in g)
            {
                var i = Math.Clamp((int)((s.At - since).TotalMinutes / 15), 0, 95);
                if (arr[i] is null || HealthStatus.Rank(s.Status) > HealthStatus.Rank(arr[i])) arr[i] = s.Status;
            }
            return arr;
        });
        var incidents = await db.HealthIncidents.AsNoTracking().OrderByDescending(i => i.StartedAt).Take(50).ToListAsync(ct);
        var owner = await PortalTenantsEndpoints.IsOwnerAsync(http);

        // Every known check, measured or not yet, in catalog order (jobs after).
        var order = HealthCatalog.Checks.Select(c => c.Key).ToList();
        var rows = checks.Select(c => c.Key).Union(order)
            .Select(k => (Key: k, Def: HealthCatalog.Find(k), State: checks.FirstOrDefault(c => c.Key == k)))
            .Where(x => x.Def is not null)
            .OrderBy(x => order.IndexOf(x.Key) is var i && i < 0 ? 999 : i).ThenBy(x => x.Key)
            .Select(x => new
            {
                x.Key, area = x.Def!.Area, name = x.Def.Name, unit = x.Def.Unit, description = x.Def.Description,
                status = x.State?.Status ?? HealthStatus.Unknown, value = x.State?.Value, detail = x.State?.Detail ?? "Not measured yet",
                checkedAt = x.State?.CheckedAt, statusSince = x.State?.StatusSince,
                warn = x.State?.WarnOverride ?? x.Def.Warn, crit = x.State?.CritOverride ?? x.Def.Crit,
                defaultWarn = x.Def.Warn, defaultCrit = x.Def.Crit, higherIsWorse = x.Def.HigherIsWorse,
                adjustable = x.Def.Warn is not null || x.Def.Crit is not null,
                mutedUntil = x.State is { } s && s.IsMuted(now) ? s.MutedUntil : null,
                acknowledgedAt = x.State?.AcknowledgedAt, acknowledgedBy = x.State?.AcknowledgedBy,
                history = slots.GetValueOrDefault(x.Key),
            }).ToList();

        object? alerting = null;
        if (owner)
        {
            var people = await db.PlatformUsers.AsNoTracking().OrderBy(u => u.Name).ToListAsync(ct);
            var extra = (await db.HealthSettings.AsNoTracking().FirstOrDefaultAsync(ct))?.ExtraRecipients ?? [];
            alerting = new
            {
                owners = people.Where(p => p.Role == PlatformRole.Owner).Select(p => new { p.EntraOid, p.Name, p.Email, p.LastSignInAt }),
                extra,
            };
        }
        return Results.Ok(new
        {
            checkedAt = checks.Count == 0 ? (DateTimeOffset?)null : checks.Max(c => c.CheckedAt),
            checks = rows,
            incidents = incidents.Select(i => new
            {
                i.Id, i.Key, name = HealthCatalog.Find(i.Key)?.Name ?? i.Key, i.Severity, i.Detail, i.StartedAt, i.ResolvedAt,
            }),
            alerting,
        });
    }

    public record KeyRequest(string? Key);

    private static async Task<IResult> Acknowledge(KeyRequest req, HttpContext http, ContactConnectionDbContext db, CancellationToken ct)
    {
        var c = await db.HealthChecks.FirstOrDefaultAsync(x => x.Key == req.Key, ct);
        if (c is null) return Results.NotFound();
        c.Acknowledge(Who(http), DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    public record LevelsRequest(string? Key, double? Warn, double? Crit, bool Reset);

    private static async Task<IResult> SetLevels(LevelsRequest req, ContactConnectionDbContext db, CancellationToken ct)
    {
        var def = req.Key is null ? null : HealthCatalog.Find(req.Key);
        if (def is null || (def.Warn is null && def.Crit is null)) return Results.BadRequest(new { error = "This check has no adjustable levels." });
        if (!req.Reset)
        {
            if (req.Warn is null || req.Crit is null) return Results.BadRequest(new { error = "Give both a warning and a critical level." });
            if (def.HigherIsWorse ? req.Crit < req.Warn : req.Crit > req.Warn)
                return Results.BadRequest(new { error = def.HigherIsWorse ? "Critical must be at or above warning." : "Critical must be at or below warning." });
        }
        var c = await db.HealthChecks.FirstOrDefaultAsync(x => x.Key == req.Key, ct);
        if (c is null) { c = PlatformHealthCheck.New(req.Key!, DateTimeOffset.UtcNow); db.HealthChecks.Add(c); }
        if (req.Reset) c.SetLevels(null, null); else c.SetLevels(req.Warn, req.Crit);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    /// <param name="Minutes">How long to mute (0 = unmute).</param>
    public record MuteRequest(string? Key, int Minutes);

    private static async Task<IResult> Mute(MuteRequest req, ContactConnectionDbContext db, CancellationToken ct)
    {
        if (req.Minutes is < 0 or > 7 * 24 * 60) return Results.BadRequest(new { error = "Mute for up to 7 days." });
        var c = await db.HealthChecks.FirstOrDefaultAsync(x => x.Key == req.Key, ct);
        if (c is null) return Results.NotFound();
        c.Mute(req.Minutes == 0 ? null : DateTimeOffset.UtcNow.AddMinutes(req.Minutes));
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    public record RecipientsRequest(List<string>? Extra);

    private static async Task<IResult> SetRecipients(RecipientsRequest req, ContactConnectionDbContext db, CancellationToken ct)
    {
        var s = await db.HealthSettings.FirstOrDefaultAsync(ct);
        if (s is null) { s = HealthSettings.Default(); db.HealthSettings.Add(s); }
        try { s.SetRecipients(req.Extra ?? []); }
        catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    /// <summary>Stops alerts to someone who no longer works here (they're re-added if they sign in to the Portal again).</summary>
    private static async Task<IResult> RemovePerson(string oid, ContactConnectionDbContext db, CancellationToken ct)
    {
        var removed = await db.PlatformUsers.Where(u => u.EntraOid == oid).ExecuteDeleteAsync(ct);
        return removed == 0 ? Results.NotFound() : Results.NoContent();
    }

    private static async Task<IResult> TestAlert(HealthRecorder recorder, ContactConnection.Application.Interfaces.Services.IEmailService email, CancellationToken ct)
    {
        var to = await recorder.RecipientsAsync(ct);
        if (to.Count == 0) return Results.BadRequest(new { error = "Nobody to send to yet." });
        await email.SendAsync(new ContactConnection.Application.Interfaces.Services.EmailMessage
        {
            To = to, FromName = "ContactConnection Health", Subject = "[ContactConnection] Test health alert",
            HtmlBody = "<p style=\"font-family:Segoe UI,Arial,sans-serif\">This is a test from the Platform Portal's Health page — health alerts will reach this address.</p>",
        }, ct);
        return Results.Ok(new { sentTo = to });
    }
}
