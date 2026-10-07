using System.Globalization;
using ContactConnection.Api.Hubs;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Telephony;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// Agent dedications (S183) — a supervisor dedicates an agent to a set of campaigns for a while, until a time, or on
/// weekly windows (see <see cref="AgentDedication"/>; routing applies them in EligibleAgentRanker) — and the agent
/// portal's personal queue: the callers waiting right now on campaigns the signed-in agent takes calls for.
/// </summary>
public static class AgentDedicationsEndpoints
{
    public static IEndpointRouteBuilder MapAgentDedicationsEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/agent-dedications").RequireAuthorization();
        g.MapGet("", List);
        g.MapPost("", Create);
        g.MapPost("{id:guid}/end", End);
        g.MapGet("mine", Mine);
        app.MapGet("/api/v1/agent/my-queue", MyQueue).RequireAuthorization();
        return app;
    }

    private static string[] Perms(HttpContext http) =>
        (http.User.FindFirst("permissions")?.Value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
    /// <summary>Dedicating changes who takes which calls — supervisors who can override, or people who manage agents.</summary>
    internal static bool CanManage(HttpContext http) =>
        Perms(http).Any(p => p is Permission.SupervisorOverride or Permission.AgentsManage);
    private static bool CanView(HttpContext http) =>
        CanManage(http) || Perms(http).Any(p => p is Permission.ReportsView or Permission.AgentsView or Permission.SupervisorMonitor);
    private static Guid? Me(HttpContext http) => Guid.TryParse(http.User.FindFirst("sub")?.Value, out var id) ? id : null;

    private static TimeZoneInfo Zone(string tz)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(tz); } catch (Exception) { return TimeZoneInfo.Utc; }
    }

    private static readonly string[] DayNames = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    /// <summary>"Mon–Fri", "Mon, Wed, Fri", "Every day".</summary>
    internal static string Days(int[] days)
    {
        var d = days.Distinct().Order().ToArray();
        if (d.Length == 7) return "Every day";
        // A consecutive run of three or more reads as a range.
        if (d.Length >= 3 && d[^1] - d[0] == d.Length - 1) return $"{DayNames[d[0]]}–{DayNames[d[^1]]}";
        return string.Join(", ", d.Select(x => DayNames[x]));
    }

    private static string Time(string hhmm) =>
        TimeOnly.Parse(hhmm, CultureInfo.InvariantCulture).ToString("h:mm tt", CultureInfo.InvariantCulture);

    private static string When(DateTimeOffset at, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(at, zone).ToString("MMM d, h:mm tt", CultureInfo.InvariantCulture);

    /// <summary>"until Oct 7, 3:15 PM" / "Mon–Fri 9:00 AM–1:00 PM · through Oct 31".</summary>
    internal static string Describe(AgentDedication d, TimeZoneInfo zone)
    {
        if (d.Mode != DedicationMode.Schedule) return d.EndsAt is { } e ? $"until {When(e, zone)}" : "";
        var windows = string.Join("; ", d.Windows.Select(w => $"{Days(w.Days)} {Time(w.Start)}–{Time(w.End)}"));
        return d.EndsAt is { } last
            ? $"{windows} · through {TimeZoneInfo.ConvertTime(last.AddMinutes(-1), zone).ToString("MMM d", CultureInfo.InvariantCulture)}"
            : windows;
    }

    private static async Task<Dictionary<Guid, string>> CampaignNamesAsync(TenantDbContext db, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await db.Campaigns.AsNoTracking().Where(c => list.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
    }

    private static object Dto(AgentDedication d, Dictionary<Guid, string> names, TimeZoneInfo zone, DateTimeOffset now) => new
    {
        d.Id, d.AgentId, d.Mode,
        campaigns = d.CampaignIds.Select(id => new { id, name = names.GetValueOrDefault(id, "(removed campaign)") }),
        d.StartsAt, d.EndsAt, windows = d.Windows, d.Note, d.CreatedByName, d.CreatedAt,
        activeNow = d.IsActiveAt(now), nextChangeAt = d.NextChangeAfter(now), summary = Describe(d, zone),
    };

    /// <summary>Current (not ended, not expired) dedications — for one agent, or everyone's.</summary>
    private static async Task<IResult> List(Guid? agentId, ScopedTenantDbContextFactory dbf, TenantContext tc, HttpContext http, CancellationToken ct)
    {
        if (tc.Current is not { } tenant) return Results.Unauthorized();
        if (!CanView(http)) return Results.Forbid();
        await using var db = dbf.Create();
        var now = DateTimeOffset.UtcNow;
        var rows = await db.AgentDedications.AsNoTracking()
            .Where(d => d.EndedAt == null && (d.EndsAt == null || d.EndsAt > now) && (agentId == null || d.AgentId == agentId))
            .OrderBy(d => d.CreatedAt).ToListAsync(ct);
        var names = await CampaignNamesAsync(db, rows.SelectMany(r => r.CampaignIds), ct);
        var zone = Zone(tenant.Timezone);
        return Results.Ok(rows.Select(r => Dto(r, names, zone, now)));
    }

    public record CreateDedicationRequest(Guid AgentId, List<Guid>? CampaignIds, string? Mode, int? Minutes, DateTimeOffset? EndsAt,
        List<DedicationWindow>? Windows, string? LastDay, string? Note);

    private static async Task<IResult> Create(CreateDedicationRequest req, ScopedTenantDbContextFactory dbf, TenantContext tc,
        IHubContext<FlowHub, IFlowHubClient> hub, HttpContext http, CancellationToken ct)
    {
        if (tc.Current is not { } tenant) return Results.Unauthorized();
        if (!CanManage(http) || Me(http) is not { } me) return Results.Forbid();
        await using var db = dbf.Create();
        var agent = await db.Agents.FirstOrDefaultAsync(a => a.Id == req.AgentId && a.IsActive, ct);
        if (agent is null) return Results.BadRequest(new { error = "That agent wasn't found." });
        var ids = (req.CampaignIds ?? []).Distinct().ToList();
        var known = await db.Campaigns.Where(c => ids.Contains(c.Id) && c.Status == CampaignStatus.Active).Select(c => c.Id).ToListAsync(ct);
        if (ids.Count == 0 || known.Count != ids.Count) return Results.BadRequest(new { error = "Choose one or more active campaigns." });

        var now = DateTimeOffset.UtcNow;
        var zone = Zone(tenant.Timezone);
        DateTimeOffset? endsAt = req.Mode switch
        {
            DedicationMode.Duration => req.Minutes is > 0 and <= 60 * 24 * 31 ? now.AddMinutes(req.Minutes.Value) : null,
            DedicationMode.Until => req.EndsAt,
            // The schedule runs through the whole last day, in the tenant's time zone.
            DedicationMode.Schedule when DateOnly.TryParse(req.LastDay, CultureInfo.InvariantCulture, out var day) =>
                LocalMidnight(day.AddDays(1), zone),
            _ => null,
        };
        var creator = await db.Agents.Where(a => a.Id == me).Select(a => (a.FirstName + " " + a.LastName).Trim()).FirstOrDefaultAsync(ct) ?? "";
        AgentDedication d;
        try
        {
            d = AgentDedication.Create(tenant.Id, agent.Id, ids, req.Mode ?? "", now, endsAt, req.Windows, tenant.Timezone, req.Note, me, creator);
        }
        catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
        db.AgentDedications.Add(d);
        await db.SaveChangesAsync(ct);
        await NotifyAsync(hub, tenant.Id, agent.Id);
        var names = await CampaignNamesAsync(db, d.CampaignIds, ct);
        return Results.Ok(Dto(d, names, zone, now));
    }

    private static DateTimeOffset LocalMidnight(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    private static async Task<IResult> End(Guid id, ScopedTenantDbContextFactory dbf, TenantContext tc,
        IHubContext<FlowHub, IFlowHubClient> hub, HttpContext http, CancellationToken ct)
    {
        if (tc.Current is not { } tenant) return Results.Unauthorized();
        if (!CanManage(http) || Me(http) is not { } me) return Results.Forbid();
        await using var db = dbf.Create();
        var d = await db.AgentDedications.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return Results.NotFound();
        d.End(me, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);
        await NotifyAsync(hub, tenant.Id, d.AgentId);
        return Results.NoContent();
    }

    /// <summary>The agent's portal (banner + personal queue) and supervisors' Agent List widgets refresh.</summary>
    private static async Task NotifyAsync(IHubContext<FlowHub, IFlowHubClient> hub, Guid tenantId, Guid agentId)
    {
        await hub.Clients.Group($"agent:{agentId}").ReceiveDedicationChanged();
        await hub.Clients.Group($"supervisor:{tenantId}").ReceiveAgentSessionsChanged(agentId.ToString());
    }

    /// <summary>The signed-in agent's own dedication right now (any active one's campaigns), and when that next changes.</summary>
    private static async Task<IResult> Mine(ScopedTenantDbContextFactory dbf, TenantContext tc, HttpContext http, CancellationToken ct)
    {
        if (tc.Current is not { } tenant) return Results.Unauthorized();
        if (Me(http) is not { } me) return Results.Unauthorized();
        await using var db = dbf.Create();
        var now = DateTimeOffset.UtcNow;
        var current = (await db.AgentDedications.AsNoTracking()
            .Where(d => d.AgentId == me && d.EndedAt == null && (d.EndsAt == null || d.EndsAt > now))
            .ToListAsync(ct));
        var active = current.Where(d => d.IsActiveAt(now)).ToList();
        var names = await CampaignNamesAsync(db, active.SelectMany(d => d.CampaignIds), ct);
        var zone = Zone(tenant.Timezone);
        return Results.Ok(new
        {
            active = active.Count > 0,
            campaigns = active.SelectMany(d => d.CampaignIds).Distinct().Select(id => names.GetValueOrDefault(id, "")).Where(n => n != "").Order(),
            summary = string.Join("; ", active.Select(d => Describe(d, zone))),
            nextChangeAt = current.Select(d => d.NextChangeAfter(now)).Where(t => t is not null).Min(),
        });
    }

    /// <summary>Callers waiting right now on campaigns the signed-in agent takes calls for (assignments, groups and
    /// dedications — the same routes the queue offers calls through), oldest first.</summary>
    private static async Task<IResult> MyQueue(ScopedTenantDbContextFactory dbf, TenantContext tc, ITelephonyCallSessionStore sessions,
        HttpContext http, CancellationToken ct)
    {
        if (tc.Current is not { } tenant) return Results.Unauthorized();
        if (Me(http) is not { } me) return Results.Unauthorized();
        var queued = (await sessions.GetAllAsync(ct))
            .Where(s => s.TenantId == tenant.Id && s.Vars.GetValueOrDefault("_queued") == "true")
            .OrderBy(s => s.Vars.GetValueOrDefault("_in_queue_at") ?? "")
            .ToList();
        if (queued.Count == 0) return Results.Ok(Array.Empty<object>());

        await using var db = dbf.Create();
        var mine = new Dictionary<(Guid, Guid?), bool>();
        var rows = new List<(TelephonyCallSession S, Guid CampaignId)>();
        foreach (var s in queued)
        {
            var key = (s.CampaignId, QueueOffer.RestrictGroupId(s.Vars));
            if (!mine.TryGetValue(key, out var handles))
                mine[key] = handles = await EligibleAgentRanker.ResolveRouteAsync(db, key.Item1, me, key.Item2, ct) is not null;
            if (handles) rows.Add((s, s.CampaignId));
        }
        var names = await CampaignNamesAsync(db, rows.Select(r => r.CampaignId), ct);
        return Results.Ok(rows.Select(r => new
        {
            callRecordId = r.S.CallRecordId,
            campaign = names.GetValueOrDefault(r.CampaignId, ""),
            queuedSince = r.S.Vars.GetValueOrDefault("_in_queue_at"),
            isCallback = r.S.Vars.GetValueOrDefault("_queue_callback") == "true",
        }));
    }
}
