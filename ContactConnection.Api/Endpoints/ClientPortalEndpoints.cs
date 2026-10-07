using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Kpis;
using ContactConnection.Infrastructure.Reports;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// The client portal (S181, docs/client-dashboards-plan.md §B). A client user sees only the client dashboards assigned to
/// them, and widget data is computed here from the SAVED widget config inside the dashboard's locked scope — the client
/// user never sends a filter, so they can't widen what they see.
/// </summary>
public static class ClientPortalEndpoints
{
    public static IEndpointRouteBuilder MapClientPortalEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/client-portal").RequireAuthorization("ClientUser");
        g.MapGet("me", Me);
        g.MapPut("me/preferences", SetPreferences);
        g.MapPost("me/mfa/setup", MfaSetup);
        g.MapPost("me/mfa/enable", MfaEnable);
        g.MapPost("me/mfa/disable", MfaDisable);
        g.MapGet("dashboards/{id:guid}", Dashboard);
        g.MapGet("dashboards/{id:guid}/widgets/{widgetId}/data", WidgetData);
        g.MapGet("dashboards/{id:guid}/widgets/{widgetId}/records/{callId:guid}", RecordDetail);
        g.MapGet("dashboards/{id:guid}/widgets/{widgetId}/records/{callId:guid}/recording", Recording);
        return app;
    }

    /// <summary>The signed-in client user with their dashboards — only client dashboards still marked as such.</summary>
    private static async Task<(ClientUser User, List<Dashboard> Dashboards)?> LoadAsync(TenantDbContext db, ClaimsPrincipal principal, CancellationToken ct)
    {
        if (!ClientPortalAuthEndpoints.TryUserId(principal, out var id)) return null;
        var user = await db.ClientUsers.Include(u => u.Dashboards).FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null || !user.CanSignIn) return null;
        var ids = user.Dashboards.Select(d => d.DashboardId).ToList();
        var dashboards = await db.Dashboards.AsNoTracking()
            .Where(d => ids.Contains(d.Id) && d.IsClientDashboard && d.ScopeClientId != null)
            .OrderBy(d => d.Name).ToListAsync(ct);
        return (user, dashboards);
    }

    private static async Task<IResult> Me(ClaimsPrincipal principal, ScopedTenantDbContextFactory dbf, TenantContext tc, CancellationToken ct)
    {
        if (tc.Current is not { } tenant) return Results.Unauthorized();
        await using var db = dbf.Create();
        if (await LoadAsync(db, principal, ct) is not { } loaded) return Results.Unauthorized();
        var (user, dashboards) = loaded;
        return Results.Ok(new
        {
            profile = ClientPortalAuthEndpoints.Profile(user, tenant),
            dashboards = dashboards.Select(d => new { id = d.Id, name = d.Name }),
        });
    }

    private static async Task<IResult> SetPreferences(ClientPreferencesRequest req, ClaimsPrincipal principal, ScopedTenantDbContextFactory dbf,
        TenantContext tc, CancellationToken ct)
    {
        if (tc.Current is not { } tenant) return Results.Unauthorized();
        if (req.TimeZone is { Length: > 0 } tz && !IsZone(tz)) return Results.BadRequest(new { error = "Unknown time zone." });
        await using var db = dbf.Create();
        if (await LoadAsync(db, principal, ct) is not { } loaded) return Results.Unauthorized();
        var (user, _) = loaded;
        user.SetPreferences(req.TimeZone, req.DefaultDashboardId);
        await db.SaveChangesAsync(ct);
        return Results.Ok(ClientPortalAuthEndpoints.Profile(user, tenant));
    }

    // ── Self-service two-step sign-in (S181) — always offered to client users; can't be turned off when the tenant requires it. ──

    private static async Task<IResult> MfaSetup(ClaimsPrincipal principal, ScopedTenantDbContextFactory dbf, TenantContext tc,
        Application.Interfaces.Services.IMfaService mfa, IConfiguration config, CancellationToken ct)
    {
        if (tc.Current is not { } tenant) return Results.Unauthorized();
        await using var db = dbf.Create();
        if (await LoadAsync(db, principal, ct) is not { } loaded) return Results.Unauthorized();
        var (user, _) = loaded;
        if (user.MfaEnabled) return Results.Conflict(new { error = "Two-step sign-in is already on." });
        var secret = mfa.GenerateSecret();
        user.StoreMfaSecret(secret);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new MfaSetupResponse(secret, mfa.GetOtpAuthUri(secret, user.Email, config["App:IssuerName"] ?? "ContactConnection")));
    }

    private static async Task<IResult> MfaEnable(MfaCodeRequest req, ClaimsPrincipal principal, ScopedTenantDbContextFactory dbf, TenantContext tc,
        Application.Interfaces.Services.IMfaService mfa, HttpContext http, CancellationToken ct)
    {
        if (tc.Current is not { } tenant) return Results.Unauthorized();
        await using var db = dbf.Create();
        if (await LoadAsync(db, principal, ct) is not { } loaded) return Results.Unauthorized();
        var (user, _) = loaded;
        if (user.MfaEnabled) return Results.Ok(ClientPortalAuthEndpoints.Profile(user, tenant));
        if (user.MfaSecret is null) return Results.BadRequest(new { error = "Start the setup again." });
        if (!mfa.Verify(user.MfaSecret, req.Code)) return Results.UnprocessableEntity(new { error = "Invalid code." });
        user.EnableMfa();
        db.ClientUserAudit.Add(ClientUserAuditEntry.Create(user.Id, ClientUserAuditAction.MfaEnabled, null, ClientPortalAuthEndpoints.Ip(http)));
        await db.SaveChangesAsync(ct);
        return Results.Ok(ClientPortalAuthEndpoints.Profile(user, tenant));
    }

    private static async Task<IResult> MfaDisable(MfaCodeRequest req, ClaimsPrincipal principal, ScopedTenantDbContextFactory dbf, TenantContext tc,
        Application.Interfaces.Services.IMfaService mfa, HttpContext http, CancellationToken ct)
    {
        if (tc.Current is not { } tenant) return Results.Unauthorized();
        if (tenant.Settings.MfaRequirement == "on") return Results.BadRequest(new { error = "Two-step sign-in is required by your account contact." });
        await using var db = dbf.Create();
        if (await LoadAsync(db, principal, ct) is not { } loaded) return Results.Unauthorized();
        var (user, _) = loaded;
        if (!user.MfaEnabled || user.MfaSecret is null) return Results.Ok(ClientPortalAuthEndpoints.Profile(user, tenant));
        if (!mfa.Verify(user.MfaSecret, req.Code)) return Results.UnprocessableEntity(new { error = "Invalid code." });
        user.ResetMfa();
        db.ClientUserAudit.Add(ClientUserAuditEntry.Create(user.Id, ClientUserAuditAction.MfaDisabled, null, ClientPortalAuthEndpoints.Ip(http)));
        await db.SaveChangesAsync(ct);
        return Results.Ok(ClientPortalAuthEndpoints.Profile(user, tenant));
    }

    private static async Task<IResult> Dashboard(Guid id, ClaimsPrincipal principal, ScopedTenantDbContextFactory dbf, TenantContext tc,
        HttpContext http, CancellationToken ct)
    {
        if (tc.Current is null) return Results.Unauthorized();
        await using var db = dbf.Create();
        if (await LoadAsync(db, principal, ct) is not { } loaded) return Results.Unauthorized();
        var (user, dashboards) = loaded;
        var dashboard = dashboards.FirstOrDefault(d => d.Id == id);
        if (dashboard is null) return Results.NotFound();

        db.ClientUserAudit.Add(ClientUserAuditEntry.Create(user.Id, ClientUserAuditAction.DashboardViewed, dashboard.Name, ClientPortalAuthEndpoints.Ip(http)));
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { id = dashboard.Id, name = dashboard.Name, layout = ClientLayout(dashboard.Layout).ToJsonString() });
    }

    /// <summary>The layout as a client user may see it: allowed widget types only, internal filters stripped.</summary>
    internal static JsonArray ClientLayout(string layout)
    {
        var result = new JsonArray();
        foreach (var w in ParseLayout(layout))
        {
            if (w["widgetType"]?.GetValue<string>() is not { } type || !Domain.Entities.Dashboard.ClientWidgetTypes.Contains(type)) continue;
            var copy = w.DeepClone().AsObject();
            if (copy["config"] is JsonObject config)
                foreach (var key in new[] { "clientId", "campaignId", "groupId", "loggedInOnly" }) config.Remove(key);
            result.Add(copy);
        }
        return result;
    }

    private static IEnumerable<JsonObject> ParseLayout(string layout)
    {
        try { return (JsonNode.Parse(layout) as JsonArray)?.OfType<JsonObject>().ToList() ?? []; }
        catch (JsonException) { return []; }
    }

    /// <summary>A client-dashboard widget the signed-in client user may see, with its saved config resolved inside the
    /// dashboard's locked scope. Null when the user, dashboard or widget isn't theirs (or isn't a client widget).</summary>
    private sealed record ClientWidget(ClientUser User, Dashboard Dashboard, string Type, JsonObject Config, IReadOnlyList<Guid> Campaigns,
        string Zone, string? Mode, int? Value)
    {
        public string? Str(string key) => Config[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
        public List<string> Strings(string key) => Config[key] is JsonArray a ? a.OfType<JsonValue>().Select(v => v.TryGetValue<string>(out var s) ? s : null).OfType<string>().ToList() : [];
        public bool Flag(string key, bool fallback) => Config[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : fallback;
    }

    private static async Task<ClientWidget?> ResolveWidgetAsync(TenantDbContext db, ClaimsPrincipal principal, Tenant tenant, Guid id,
        string widgetId, CancellationToken ct)
    {
        if (await LoadAsync(db, principal, ct) is not { } loaded) return null;
        var (user, dashboards) = loaded;
        var dashboard = dashboards.FirstOrDefault(d => d.Id == id);
        var widget = dashboard is null ? null : ParseLayout(dashboard.Layout).FirstOrDefault(w => w["id"]?.GetValue<string>() == widgetId);
        var type = widget?["widgetType"]?.GetValue<string>();
        if (dashboard is null || widget is null || type is null || !Domain.Entities.Dashboard.ClientWidgetTypes.Contains(type)) return null;

        var config = widget["config"] as JsonObject ?? new JsonObject();
        string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
        Guid? widgetCampaign = Guid.TryParse(Str(config["campaignId"]), out var wc) ? wc : null;
        var clientCampaigns = await db.Campaigns.AsNoTracking().Where(c => c.ClientId == dashboard.ScopeClientId).Select(c => c.Id).ToListAsync(ct);
        var campaigns = dashboard.EffectiveCampaigns(clientCampaigns, widgetCampaign);

        var zone = user.TimeZone is { } z && IsZone(z) ? z : tenant.Timezone;
        var window = config["timeWindow"] as JsonObject;
        int? value = window?["value"] is JsonValue wv && wv.TryGetValue<int>(out var n) ? n : null;
        return new ClientWidget(user, dashboard, type, config, campaigns, zone, Str(window?["mode"]), value);
    }

    private static async Task<IResult> WidgetData(Guid id, string widgetId, ClaimsPrincipal principal, ScopedTenantDbContextFactory dbf,
        TenantContext tc, KpiService kpis, ICallStateHistoryRepository callStates, CallRecordsReport records, HttpRequest request,
        IAgentGroupRepository agentGroups, CancellationToken ct)
    {
        if (tc.Current is not { } tenant) return Results.Unauthorized();
        await using var db = dbf.Create();
        if (await ResolveWidgetAsync(db, principal, tenant, id, widgetId, ct) is not { } w) return Results.NotFound();
        var (dashboard, type, config, campaigns, zone, mode, value) = (w.Dashboard, w.Type, w.Config, w.Campaigns, w.Zone, w.Mode, w.Value);
        string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
        // The widget's saved agent-group and DNIS filters (S181) — narrowing inside the scope.
        Guid? groupId = Guid.TryParse(Str(config["groupId"]), out var gid) ? gid : null;
        var dnisKeys = Domain.ValueObjects.PhoneKey.Set(w.Strings("dnis"));

        switch (type)
        {
            case "records":
            {
                // The client user may page, search, sort and filter — all narrowing, never outside the scope or the columns.
                var (since, until) = KpiEndpoints.Window(zone, mode, value, DateTimeOffset.UtcNow);
                var p = RecordsWidgetEndpoints.Paging(request, w.Config["pageSize"] is JsonValue ps && ps.TryGetValue<int>(out var size) ? size : 25);
                var columns = w.Strings("columns");
                var filters = p.Filters.Where(f => columns.Contains(f.Key)).ToDictionary();
                return Results.Ok(await records.QueryAsync(new RecordsQuery(since, until, dashboard.ScopeClientId, null, campaigns.ToHashSet(),
                    columns, p.Search, filters, p.Sort is { } so && columns.Contains(so) ? so : null, p.Desc, p.Page, p.PageSize, zone,
                    groupId, dnisKeys), ct));
            }
            case "kpi":
            {
                var (since, until) = KpiEndpoints.Window(zone, mode, value, DateTimeOffset.UtcNow);
                var groupBy = Str(config["groupBy"]);
                var groupBy2 = Str(config["groupBy2"]);
                return Results.Ok(await kpis.ComputeAsync(new KpiQuery(since, until, dashboard.ScopeClientId, null,
                    KpiDimension.IsValid(groupBy) ? groupBy! : "none", KpiDimension.IsValid(groupBy2) ? groupBy2 : null, zone,
                    campaigns.ToHashSet(), groupId, dnisKeys), ct));
            }
            case "service_level_threshold":
            {
                if (campaigns.Count == 0) return Results.Ok(new { met = 0, missed = 0, percent_in_sl = (double?)null });
                var (since, _) = KpiEndpoints.Window(zone, mode is "hours" or "minutes" ? mode : "today", value, DateTimeOffset.UtcNow);
                var stats = await callStates.GetServiceLevelStatsAsync(tenant.SchemaName, campaigns.ToList(), since, ct, groupId, dnisKeys);
                var total = stats.Met + stats.Missed;
                return Results.Ok(new { met = stats.Met, missed = stats.Missed, percent_in_sl = total > 0 ? Math.Round(stats.Met * 100.0 / total, 1) : (double?)null });
            }
            case "call_state_by_campaign":
            {
                if (campaigns.Count == 0) return Results.Ok(Array.Empty<object>());
                var names = await db.Campaigns.AsNoTracking().Where(c => campaigns.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
                var counts = (await callStates.GetActiveStateCountsAsync(tenant.SchemaName, campaigns.ToList(), ct, dnisKeys)).ToLookup(r => r.CampaignId);
                return Results.Ok(names.OrderBy(c => c.Value).Select(c =>
                {
                    int Count(params string[] states) => counts[c.Key].Where(r => states.Contains(r.State)).Sum(r => r.Count);
                    return new
                    {
                        campaign_id = c.Key, campaign_name = c.Value,
                        pre_queue = Count(CallHistoryState.PreQueue), in_queue = Count(CallHistoryState.InQueue),
                        with_agent = Count(CallHistoryState.Routing, CallHistoryState.Active), post_agent = Count(CallHistoryState.PostAgent),
                    };
                }).Where(r => r.pre_queue + r.in_queue + r.with_agent + r.post_agent > 0).ToList());
            }
            default:
                return Results.NotFound();
        }
    }

    /// <summary>One call's detail — only the widget's detail columns (or its columns), only inside the dashboard's scope.</summary>
    private static async Task<IResult> RecordDetail(Guid id, string widgetId, Guid callId, ClaimsPrincipal principal, ScopedTenantDbContextFactory dbf,
        TenantContext tc, CallRecordsReport records, [AsParameters] CallDetailView.Deps deps, CancellationToken ct)
    {
        if (tc.Current is not { } tenant) return Results.Unauthorized();
        await using var db = dbf.Create();
        if (await ResolveWidgetAsync(db, principal, tenant, id, widgetId, ct) is not { Type: "records" } w) return Results.NotFound();
        if (!await records.InScopeAsync(callId, w.Dashboard.ScopeClientId, w.Campaigns.ToHashSet(), ct)) return Results.NotFound();
        var built = await CallDetailView.BuildAsync(callId, deps, w.Zone, ct);
        if (built is not { } b) return Results.NotFound();
        return Results.Ok(new { detail = b.Detail, canPlayRecording = b.RecordingStatus == "available" && w.User.CanPlayRecordings && w.Flag("allowRecordings", true) });
    }

    /// <summary>Recording playback (S181): only for client users the tenant allowed, on a records widget that allows it, for a
    /// call inside the dashboard's scope. Every play is audited.</summary>
    private static async Task<IResult> Recording(Guid id, string widgetId, Guid callId, ClaimsPrincipal principal, ScopedTenantDbContextFactory dbf,
        TenantContext tc, CallRecordsReport records, ICallRecordRepository callRecords, IRecordingMergeJobRepository mergeJobs,
        Application.Interfaces.Services.IBlobStorage blobs, HttpContext http, CancellationToken ct)
    {
        if (tc.Current is not { } tenant) return Results.Unauthorized();
        await using var db = dbf.Create();
        if (await ResolveWidgetAsync(db, principal, tenant, id, widgetId, ct) is not { Type: "records" } w) return Results.NotFound();
        if (!w.User.CanPlayRecordings || !w.Flag("allowRecordings", true)) return Results.Forbid();
        if (!await records.InScopeAsync(callId, w.Dashboard.ScopeClientId, w.Campaigns.ToHashSet(), ct)) return Results.NotFound();

        // Audit once per play, not per range request the player makes while seeking.
        var range = http.Request.Headers.Range.ToString();
        if (string.IsNullOrEmpty(range) || range.StartsWith("bytes=0-"))
        {
            db.ClientUserAudit.Add(ClientUserAuditEntry.Create(w.User.Id, ClientUserAuditAction.RecordingPlayed, $"call {callId}",
                ClientPortalAuthEndpoints.Ip(http)));
            await db.SaveChangesAsync(ct);
        }
        return await CallRecordingsEndpoints.StreamAsync(callId, callRecords, mergeJobs, blobs, ct);
    }

    private static bool IsZone(string id)
    {
        try { TimeZoneInfo.FindSystemTimeZoneById(id); return true; }
        catch (Exception) { return false; }
    }
}

public record ClientPreferencesRequest(string? TimeZone, Guid? DefaultDashboardId);
