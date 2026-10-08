using System.Net;
using System.Text;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ContactConnection.Infrastructure.Health;

/// <summary>Redis keys the API and Worker use to watch each other (S184).</summary>
public static class HealthKeys
{
    /// <summary>The Worker's last health round (ISO time).</summary>
    public const string WorkerHeartbeat = "health:worker";
    /// <summary>The API's heartbeat + facts only it knows (<see cref="ApiFacts"/> JSON), every 20 s.</summary>
    public const string ApiFacts = "health:api";
}

/// <summary>What the API reports every 20 s: its FreeSWITCH event connection and agents' softphone connections.</summary>
/// <param name="StartedAt">When the API process started — FreeSWITCH isn't judged in its first minute.</param>
public sealed record ApiFacts(DateTimeOffset At, DateTimeOffset? EslConnectedSince, int AgentsOnCall, int AgentsPoor, DateTimeOffset? StartedAt = null);

/// <summary>One check's measurement. <paramref name="Status"/> overrides the threshold evaluation (up / down checks).</summary>
public sealed record HealthResult(string Key, double? Value, string? Detail, string? Status = null);

/// <summary>
/// Records platform health results (S184): updates each check's state, keeps a minute-by-minute history, opens and closes
/// incidents, and emails Owners + extra recipients when a check goes to warning / critical (repeating an unacknowledged
/// critical every 30 minutes) and when it recovers. Used by the Worker's health service and the API's Worker watchdog.
/// </summary>
public sealed class HealthRecorder(ContactConnectionDbContext db, IEmailService email, IConnectionMultiplexer redis,
    IConfiguration config, ILogger<HealthRecorder> logger)
{
    /// <summary>Redis channel the API relays to the Portal's Health page.</summary>
    public const string ChangedChannel = "platform-health:changed";

    public async Task RecordAsync(IReadOnlyList<HealthResult> results, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var keys = results.Select(r => r.Key).ToList();
        var checks = await db.HealthChecks.Where(c => keys.Contains(c.Key)).ToDictionaryAsync(c => c.Key, ct);
        var open = await db.HealthIncidents.Where(i => keys.Contains(i.Key) && i.ResolvedAt == null).ToDictionaryAsync(i => i.Key, ct);
        var changed = false;

        foreach (var r in results)
        {
            var def = HealthCatalog.Find(r.Key);
            if (!checks.TryGetValue(r.Key, out var check))
            {
                check = PlatformHealthCheck.New(r.Key, now);
                db.HealthChecks.Add(check);
                checks[r.Key] = check;
            }
            var status = r.Status
                ?? (r.Value is { } v && def is not null && (def.Warn is not null || def.Crit is not null)
                    ? def.Evaluate(v, check.WarnOverride ?? def.Warn, check.CritOverride ?? def.Crit)
                    : r.Value is null ? HealthStatus.Unknown : HealthStatus.Ok);

            if (check.Record(status, r.Value, r.Detail, now) is not null) changed = true;
            db.HealthSamples.Add(HealthSample.Of(r.Key, now, status, r.Value));

            var problem = status is HealthStatus.Warning or HealthStatus.Critical;
            if (problem && !check.IsMuted(now))
            {
                if (open.TryGetValue(r.Key, out var incident)) incident.Worsen(status, r.Detail);
                else { incident = HealthIncident.Open(r.Key, status, r.Detail, now); db.HealthIncidents.Add(incident); open[r.Key] = incident; }
            }
            else if (status == HealthStatus.Ok && open.TryGetValue(r.Key, out var resolved)) resolved.Resolve(now);
        }
        await db.SaveChangesAsync(ct);

        // Alerts: one email for everything due this round. Marked only once sent, so a mail failure retries next minute.
        var due = checks.Values.Select(c => (Check: c, Kind: c.AlertDue(now))).Where(x => x.Kind is not null).ToList();
        if (due.Count > 0 && await SendAsync(due!, now, ct))
        {
            foreach (var (c, _) in due) c.MarkAlerted(now);
            await db.SaveChangesAsync(ct);
        }

        // Every round (values move even when statuses don't) — the Health page re-reads on it.
        try { await redis.GetSubscriber().PublishAsync(RedisChannel.Literal(ChangedChannel), changed ? "changed" : "measured"); }
        catch (Exception ex) { logger.LogDebug(ex, "Health: could not publish the change notice"); }
    }

    /// <summary>Owners who have signed in to the Portal, plus the extra addresses.</summary>
    public async Task<List<string>> RecipientsAsync(CancellationToken ct)
    {
        var owners = await db.PlatformUsers.AsNoTracking().Where(u => u.Role == PlatformRole.Owner).Select(u => u.Email).ToListAsync(ct);
        var extra = (await db.HealthSettings.AsNoTracking().FirstOrDefaultAsync(ct))?.ExtraRecipients ?? [];
        return owners.Concat(extra).Select(e => e.ToLowerInvariant()).Distinct().ToList();
    }

    private async Task<bool> SendAsync(List<(PlatformHealthCheck Check, string Kind)> due, DateTimeOffset now, CancellationToken ct)
    {
        var to = await RecipientsAsync(ct);
        if (to.Count == 0) { logger.LogWarning("Health: {Count} alert(s) due but nobody to send them to", due.Count); return true; }

        var worst = due.Where(d => d.Kind == "problem").Select(d => d.Check.Status).OrderByDescending(HealthStatus.Rank).FirstOrDefault();
        var subject = worst switch
        {
            HealthStatus.Critical => $"CRITICAL — {Names(due, "problem", HealthStatus.Critical)}",
            HealthStatus.Warning => $"Warning — {Names(due, "problem", HealthStatus.Warning)}",
            _ => $"Recovered — {Names(due, "recovered", null)}",
        };
        var portal = (config["App:PortalUrl"] ?? "https://admin.contactconnection.io").TrimEnd('/') + "/portal/health";

        var rows = new StringBuilder();
        foreach (var (c, kind) in due.OrderByDescending(d => HealthStatus.Rank(d.Check.Status)))
        {
            var def = HealthCatalog.Find(c.Key);
            var (label, color) = kind == "recovered" ? ("Recovered", "#059669")
                : c.Status == HealthStatus.Critical ? ("Critical", "#dc2626") : ("Warning", "#d97706");
            var value = c.Value is { } v ? $"{Math.Round(v, 1)} {def?.Unit}".Trim() : "";
            rows.Append($"<tr><td style=\"padding:6px 10px;color:{color};font-weight:600\">{label}</td>")
                .Append($"<td style=\"padding:6px 10px\">{WebUtility.HtmlEncode(def?.Name ?? c.Key)}</td>")
                .Append($"<td style=\"padding:6px 10px;color:#374151\">{WebUtility.HtmlEncode(value)}</td>")
                .Append($"<td style=\"padding:6px 10px;color:#6b7280\">{WebUtility.HtmlEncode(c.Detail ?? "")}</td></tr>");
        }
        var html = $"""
            <div style="font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#111827">
              <p>ContactConnection platform health, {now:yyyy-MM-dd HH:mm} UTC:</p>
              <table style="border-collapse:collapse;border:1px solid #e5e7eb">{rows}</table>
              <p><a href="{portal}">Open the Health page</a> to acknowledge or mute. An unacknowledged critical repeats every 30 minutes.</p>
            </div>
            """;
        try
        {
            await email.SendAsync(new EmailMessage { To = to, Subject = $"[ContactConnection] {subject}", HtmlBody = html, FromName = "ContactConnection Health" }, ct);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Health: alert email failed — will retry");
            return false;
        }
    }

    private static string Names(List<(PlatformHealthCheck Check, string Kind)> due, string kind, string? status)
    {
        var names = due.Where(d => d.Kind == kind && (status is null || d.Check.Status == status))
            .Select(d => HealthCatalog.Find(d.Check.Key)?.Name ?? d.Check.Key).ToList();
        return names.Count <= 2 ? string.Join(", ", names) : $"{names[0]}, {names[1]} and {names.Count - 2} more";
    }
}
