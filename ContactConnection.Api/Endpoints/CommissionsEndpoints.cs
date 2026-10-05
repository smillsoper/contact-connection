using System.Globalization;
using System.Text;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects;
using ContactConnection.Infrastructure.Commerce;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Media;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// Commissions (S171): rules per campaign / client, the tenant's pay period, the per-agent report and
/// payroll CSV, an agent's own earnings, and a call's entries with reverse / restore.
/// Rules + pay period: tenant admins. Reports: reports.view (supervisors). Mine: any signed-in agent.
/// Call panel: calls.view; reverse / restore: calls.manage.
/// </summary>
public static class CommissionsEndpoints
{
    public static IEndpointRouteBuilder MapCommissionsEndpoints(this IEndpointRouteBuilder app)
    {
        var rules = app.MapGroup("/api/v1/commission-rules").RequireAuthorization("TenantAdmin");
        rules.MapGet("", ListRules);
        rules.MapPost("", CreateRule);
        rules.MapPut("{id:guid}", UpdateRule);
        rules.MapDelete("{id:guid}", DeleteRule);

        // Recalculate past calls under the rules in effect when each started (preview, then a Worker batch).
        var recalc = app.MapGroup("/api/v1/commissions/recalc").RequireAuthorization("TenantAdmin");
        recalc.MapPost("preview", PreviewRecalc);
        recalc.MapPost("", StartRecalc);
        recalc.MapGet("", ListRecalcs);

        app.MapGet("/api/v1/commission-settings", GetSettings).RequireAuthorization();
        app.MapPut("/api/v1/commission-settings", UpdateSettings).RequireAuthorization("TenantAdmin");

        var reports = app.MapGroup("/api/v1/commissions").RequireAuthorization();
        reports.MapGet("report", Report);
        reports.MapGet("entries", Entries);
        reports.MapGet("export.csv", ExportCsv);
        reports.MapGet("mine", Mine);

        app.MapGet("/api/v1/call-review/calls/{id:guid}/commissions", CallCommissions).RequireAuthorization();
        app.MapPost("/api/v1/call-review/calls/{id:guid}/commissions/reverse", ReverseCall).RequireAuthorization();
        app.MapPost("/api/v1/call-review/calls/{id:guid}/commissions/restore", RestoreCall).RequireAuthorization();
        return app;
    }

    // ── Rules ─────────────────────────────────────────────────────────────────

    private static async Task<IResult> ListRules(Guid? clientId, Guid? campaignId, TenantContext tenant, ScopedTenantDbContextFactory dbFactory, CancellationToken ct)
    {
        if (!tenant.HasTenant) return Results.Unauthorized();
        await using var db = dbFactory.Create();
        var q = db.CommissionRules.AsNoTracking();
        if (campaignId is not null) q = q.Where(r => r.CampaignId == campaignId);
        else if (clientId is not null) q = q.Where(r => r.ClientId == clientId);
        var list = await q.OrderBy(r => r.Kind).ThenBy(r => r.TierLabel == null).ThenBy(r => r.EffectiveFrom).ThenBy(r => r.Name).ToListAsync(ct);
        var tz = Zone(tenant);
        return Results.Ok(list.Select(r => RuleJson(r, tz)));
    }

    private static async Task<IResult> CreateRule(RuleRequest req, TenantContext tenant, ScopedTenantDbContextFactory dbFactory, CancellationToken ct)
    {
        if (!tenant.HasTenant) return Results.Unauthorized();
        await using var db = dbFactory.Create();
        try
        {
            var tz = Zone(tenant);
            var rule = CommissionRule.Create(tenant.Current!.Id, req.CampaignId is null ? req.ClientId : null, req.CampaignId);
            rule.Set(req.Name ?? "", req.Kind ?? "", req.Amount, req.ProductId, await ProductLabelAsync(db, req.ProductId, ct),
                req.FieldName, req.FieldValue, req.TierLabel, req.IsActive ?? true, Local(req.EffectiveFrom, tz), Local(req.EffectiveUntil, tz));
            db.CommissionRules.Add(rule);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/commission-rules/{rule.Id}", RuleJson(rule, tz));
        }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    }

    private static async Task<IResult> UpdateRule(Guid id, RuleRequest req, TenantContext tenant, ScopedTenantDbContextFactory dbFactory, CancellationToken ct)
    {
        if (!tenant.HasTenant) return Results.Unauthorized();
        await using var db = dbFactory.Create();
        var rule = await db.CommissionRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rule is null) return Results.NotFound();
        try
        {
            var tz = Zone(tenant);
            rule.Set(req.Name ?? "", req.Kind ?? "", req.Amount, req.ProductId, await ProductLabelAsync(db, req.ProductId, ct),
                req.FieldName, req.FieldValue, req.TierLabel, req.IsActive ?? rule.IsActive, Local(req.EffectiveFrom, tz), Local(req.EffectiveUntil, tz));
            await db.SaveChangesAsync(ct);
            return Results.Ok(RuleJson(rule, tz));
        }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    }

    // Past entries keep the rule's name and numbers, so a rule can simply be deleted.
    private static async Task<IResult> DeleteRule(Guid id, TenantContext tenant, ScopedTenantDbContextFactory dbFactory, CancellationToken ct)
    {
        if (!tenant.HasTenant) return Results.Unauthorized();
        await using var db = dbFactory.Create();
        var rule = await db.CommissionRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rule is null) return Results.NotFound();
        db.CommissionRules.Remove(rule);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    /// <summary>A rule with its effective window as tenant-local "yyyy-MM-ddTHH:mm" (what the form edits).</summary>
    private static object RuleJson(CommissionRule r, TimeZoneInfo tz) => new
    {
        r.Id, r.ClientId, r.CampaignId, r.Name, r.Kind, r.Amount, r.ProductId, r.ProductLabel, r.FieldName, r.FieldValue,
        r.TierLabel, r.IsActive, effectiveFrom = LocalText(r.EffectiveFrom, tz), effectiveUntil = LocalText(r.EffectiveUntil, tz),
    };

    // ── Recalculate past calls ────────────────────────────────────────────────

    private static async Task<IResult> PreviewRecalc(RecalcRequest req, TenantContext tenant, ScopedTenantDbContextFactory dbFactory, CancellationToken ct)
    {
        if (!tenant.HasTenant) return Results.Unauthorized();
        if (ToScope(req, Zone(tenant)) is not { } scope) return Results.BadRequest(new { error = "Choose a start and an end, the end after the start." });
        await using var db = dbFactory.Create();
        var p = await CommissionRecalculator.PreviewAsync(db, scope, ct);
        var names = await AgentNamesAsync(db, p.Agents.Select(a => a.AgentId), ct);
        return Results.Ok(new
        {
            p.Calls, p.ChangedCalls, p.Current, p.Recalculated, difference = p.Recalculated - p.Current,
            agents = p.Agents.Select(a => new
            {
                a.AgentId, agentName = names.GetValueOrDefault(a.AgentId, "(unknown agent)"),
                a.Current, a.Recalculated, difference = a.Recalculated - a.Current, a.ChangedCalls,
            }).OrderBy(a => a.agentName),
        });
    }

    private static async Task<IResult> StartRecalc(
        RecalcRequest req, HttpContext http, TenantContext tenant, ScopedTenantDbContextFactory dbFactory, CancellationToken ct)
    {
        if (!tenant.HasTenant) return Results.Unauthorized();
        if (ToScope(req, Zone(tenant)) is not { } scope) return Results.BadRequest(new { error = "Choose a start and an end, the end after the start." });
        await using var db = dbFactory.Create();
        if (await db.CommissionRecalcBatches.AnyAsync(b => b.Status == CommissionRecalcStatus.Pending || b.Status == CommissionRecalcStatus.Running, ct))
            return Results.Conflict(new { error = "A recalculation is already running — wait for it to finish." });
        try
        {
            var by = $"{http.User.FindFirst("given_name")?.Value} {http.User.FindFirst("family_name")?.Value}".Trim();
            var batch = CommissionRecalcBatch.Create(tenant.Current!.Id, scope.ClientId, scope.CampaignId, scope.AgentId,
                scope.From, scope.To, req.PostTo ?? CommissionPostTo.Current, req.Reason ?? "", by.Length == 0 ? null : by);
            db.CommissionRecalcBatches.Add(batch);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { batch.Id });
        }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    }

    private static async Task<IResult> ListRecalcs(TenantContext tenant, ScopedTenantDbContextFactory dbFactory, CancellationToken ct)
    {
        if (!tenant.HasTenant) return Results.Unauthorized();
        await using var db = dbFactory.Create();
        var batches = await db.CommissionRecalcBatches.AsNoTracking().OrderByDescending(b => b.CreatedAt).Take(20).ToListAsync(ct);
        var clients = await db.Clients.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var campaigns = await db.Campaigns.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var agents = await AgentNamesAsync(db, batches.Where(b => b.AgentId is not null).Select(b => b.AgentId!.Value), ct);
        var tz = Zone(tenant);
        return Results.Ok(batches.Select(b => new
        {
            b.Id, b.Status, b.PostTo, b.Reason, b.RequestedBy, b.TotalCalls, b.ProcessedCalls, b.ChangedCalls, b.Difference, b.Error,
            b.CreatedAt, b.CompletedAt,
            from = LocalText(b.From, tz), to = LocalText(b.To, tz),
            scope = string.Join(" · ", new[]
            {
                b.CampaignId is { } cp ? campaigns.GetValueOrDefault(cp, "(campaign)") : b.ClientId is { } cl ? clients.GetValueOrDefault(cl, "(client)") + " — all campaigns" : "All clients",
                b.AgentId is { } a ? agents.GetValueOrDefault(a, "(agent)") : null,
            }.Where(x => x is not null)),
        }));
    }

    private static CommissionRecalculator.Scope? ToScope(RecalcRequest req, TimeZoneInfo tz) =>
        Local(req.From, tz) is { } from && Local(req.To, tz) is { } to && to > from
            ? new(req.CampaignId is null ? req.ClientId : null, req.CampaignId, req.AgentId, from, to)
            : null;

    /// <summary>Tenant-local "yyyy-MM-ddTHH:mm" (or a date) → UTC instant.</summary>
    private static DateTimeOffset? Local(string? text, TimeZoneInfo tz)
    {
        if (string.IsNullOrWhiteSpace(text) || !DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)) return null;
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, tz.GetUtcOffset(local)).ToUniversalTime();
    }

    private static string? LocalText(DateTimeOffset? at, TimeZoneInfo tz) =>
        at is { } v ? TimeZoneInfo.ConvertTime(v, tz).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture) : null;

    private static async Task<string?> ProductLabelAsync(TenantDbContext db, Guid? productId, CancellationToken ct) =>
        productId is null ? null : await db.Products.AsNoTracking().Where(p => p.Id == productId)
            .Select(p => p.Sku + " — " + p.Description).FirstOrDefaultAsync(ct);

    // ── Pay period ────────────────────────────────────────────────────────────

    private static IResult GetSettings(TenantContext tenant)
    {
        if (!tenant.HasTenant) return Results.Unauthorized();
        var s = tenant.Current!.Settings;
        var current = CurrentPeriod(tenant);
        return Results.Ok(new
        {
            frequency = PayPeriods.IsValid(s.PayPeriodFrequency) ? s.PayPeriodFrequency : PayPeriods.Biweekly,
            start = PayPeriods.AnchorOf(s.PayPeriodStart).ToString("yyyy-MM-dd"),
            current = PeriodJson(current),
            timezone = tenant.Current.Timezone,
        });
    }

    private static async Task<IResult> UpdateSettings(SettingsRequest req, TenantContext tenant, ContactConnectionDbContext platformDb, CancellationToken ct)
    {
        if (!tenant.HasTenant) return Results.Unauthorized();
        if (!PayPeriods.IsValid(req.Frequency)) return Results.BadRequest(new { error = "Choose weekly, biweekly, semimonthly or monthly." });
        if (!DateOnly.TryParse(req.Start, CultureInfo.InvariantCulture, out var start)) return Results.BadRequest(new { error = "Enter the first day of a pay period." });

        var row = await platformDb.Tenants.FirstOrDefaultAsync(t => t.Id == tenant.Current!.Id, ct);
        if (row is null) return Results.NotFound();
        var settings = row.Settings.Clone();
        settings.PayPeriodFrequency = req.Frequency!;
        settings.PayPeriodStart = start.ToString("yyyy-MM-dd");
        row.UpdateSettings(settings);
        await platformDb.SaveChangesAsync(ct);
        tenant.Current = row;
        return GetSettings(tenant);
    }

    // ── Reports ───────────────────────────────────────────────────────────────

    /// <summary>Per-agent totals for a pay period (period = current | previous, or start=yyyy-MM-dd for the
    /// period containing that date). Optional agentId narrows it to one agent.</summary>
    private static async Task<IResult> Report(
        string? period, string? start, Guid? agentId, HttpContext http, TenantContext tenant, ScopedTenantDbContextFactory dbFactory, CancellationToken ct)
    {
        if (!tenant.HasTenant) return Results.Unauthorized();
        if (!Has(http, Permission.ReportsView)) return Results.Forbid();
        var p = ResolvePeriod(tenant, period, start);
        var (from, to) = Bounds(tenant, p);

        await using var db = dbFactory.Create();
        var q = db.CommissionEntries.AsNoTracking().Where(e => e.OccurredAt >= from && e.OccurredAt < to);
        if (agentId is not null) q = q.Where(e => e.AgentId == agentId);
        var rows = await q.GroupBy(e => e.AgentId).Select(g => new
        {
            AgentId = g.Key,
            Earned = g.Where(e => e.EntryType == CommissionEntryType.Earned).Sum(e => e.Amount),
            Reversed = g.Where(e => e.EntryType == CommissionEntryType.Reversal).Sum(e => e.Amount),
            Total = g.Sum(e => e.Amount),
            Calls = g.Select(e => e.CallRecordId).Distinct().Count(),
        }).ToListAsync(ct);
        var names = await AgentNamesAsync(db, rows.Select(r => r.AgentId), ct);

        return Results.Ok(new
        {
            period = PeriodJson(p),
            agents = rows.Select(r => new { r.AgentId, agentName = names.GetValueOrDefault(r.AgentId, "(unknown agent)"), r.Earned, r.Reversed, r.Total, r.Calls })
                         .OrderBy(r => r.agentName),
            total = rows.Sum(r => r.Total),
        });
    }

    /// <summary>The entries behind a report row: one agent (or all) for a pay period.</summary>
    private static async Task<IResult> Entries(
        string? period, string? start, Guid? agentId, HttpContext http, TenantContext tenant, ScopedTenantDbContextFactory dbFactory, CancellationToken ct)
    {
        if (!tenant.HasTenant) return Results.Unauthorized();
        if (!Has(http, Permission.ReportsView)) return Results.Forbid();
        await using var db = dbFactory.Create();
        var p = ResolvePeriod(tenant, period, start);
        return Results.Ok(new { period = PeriodJson(p), entries = await EntryRowsAsync(db, tenant, p, agentId, ct) });
    }

    /// <summary>Payroll CSV: one row per entry for the period, plus the agent's period total.</summary>
    private static async Task<IResult> ExportCsv(
        string? period, string? start, Guid? agentId, HttpContext http, TenantContext tenant, ScopedTenantDbContextFactory dbFactory, CancellationToken ct)
    {
        if (!tenant.HasTenant) return Results.Unauthorized();
        if (!Has(http, Permission.ReportsView)) return Results.Forbid();
        await using var db = dbFactory.Create();
        var p = ResolvePeriod(tenant, period, start);
        var rows = await EntryRowsAsync(db, tenant, p, agentId, ct);

        var csv = new StringBuilder();
        csv.AppendLine("Agent,Agent Total,Date,Client,Campaign,Order Number,Call ID,Rule,Type,Detail,Amount,Note");
        foreach (var agent in rows.GroupBy(r => r.AgentName).OrderBy(g => g.Key))
        {
            var total = agent.Sum(r => r.Amount);
            foreach (var r in agent)
                csv.AppendLine(string.Join(',', Csv(r.AgentName), Money(total), Csv(r.Date), Csv(r.Client), Csv(r.Campaign),
                    Csv(r.OrderNumber), r.CallRecordId, Csv(r.RuleName), r.EntryType, Csv(r.Description), Money(r.Amount), Csv(r.Note)));
        }
        var name = $"commissions-{p.Start:yyyy-MM-dd}-to-{p.End:yyyy-MM-dd}.csv";
        return Results.File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", name);
    }

    /// <summary>The signed-in agent's own earnings: the current or previous pay period, plus today.</summary>
    private static async Task<IResult> Mine(string? period, HttpContext http, TenantContext tenant, ScopedTenantDbContextFactory dbFactory, CancellationToken ct)
    {
        if (!tenant.HasTenant) return Results.Unauthorized();
        if (!Guid.TryParse(http.User.FindFirst("sub")?.Value, out var agentId)) return Results.Unauthorized();
        await using var db = dbFactory.Create();
        var p = ResolvePeriod(tenant, period, null);
        var today = MediaAttributionResolver.TodayIn(tenant.Current!.Timezone);
        var (dayFrom, dayTo) = Bounds(tenant, new PayPeriod(today, today));
        var todayTotal = await db.CommissionEntries.AsNoTracking()
            .Where(e => e.AgentId == agentId && e.OccurredAt >= dayFrom && e.OccurredAt < dayTo).SumAsync(e => e.Amount, ct);
        var entries = await EntryRowsAsync(db, tenant, p, agentId, ct);
        return Results.Ok(new { period = PeriodJson(p), today = todayTotal, total = entries.Sum(e => e.Amount), entries });
    }

    // ── A call's commissions ──────────────────────────────────────────────────

    private static async Task<IResult> CallCommissions(Guid id, HttpContext http, TenantContext tenant, ScopedTenantDbContextFactory dbFactory, CancellationToken ct)
    {
        if (!tenant.HasTenant) return Results.Unauthorized();
        if (!Has(http, Permission.CallsView)) return Results.Forbid();
        await using var db = dbFactory.Create();
        // Order submitted = the first time any of the call's interactions submitted an order (S178).
        var record = await db.CallRecords.AsNoTracking().Where(r => r.Id == id)
            .Select(r => new
            {
                OrderSubmittedAt = r.Interactions.Min(i => i.OrderSubmittedAt),
                r.CommissionsReversedAt, r.CommissionsReversedReason,
            }).FirstOrDefaultAsync(ct);
        if (record is null) return Results.NotFound();
        var entries = await db.CommissionEntries.AsNoTracking().Where(e => e.CallRecordId == id).OrderBy(e => e.OccurredAt).ToListAsync(ct);
        var names = await AgentNamesAsync(db, entries.Select(e => e.AgentId), ct);
        return Results.Ok(new
        {
            record.OrderSubmittedAt, record.CommissionsReversedAt, record.CommissionsReversedReason,
            total = entries.Sum(e => e.Amount),
            entries = entries.Select(e => new
            {
                e.Id, e.EntryType, e.RuleName, e.Description, e.Amount, e.IsReversed, e.Note, e.OccurredAt,
                agentName = names.GetValueOrDefault(e.AgentId, "(unknown agent)"),
            }),
        });
    }

    private static async Task<IResult> ReverseCall(Guid id, ReverseRequest req, HttpContext http, TenantContext tenant, ICommissionService commissions, CancellationToken ct)
    {
        if (!tenant.HasTenant) return Results.Unauthorized();
        if (!Has(http, Permission.CallsManage)) return Results.Forbid();
        await commissions.ReverseAsync(id, string.IsNullOrWhiteSpace(req.Reason) ? CommissionTrigger.OrderCancelled : req.Reason, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RestoreCall(Guid id, HttpContext http, TenantContext tenant, ICommissionService commissions, CancellationToken ct)
    {
        if (!tenant.HasTenant) return Results.Unauthorized();
        if (!Has(http, Permission.CallsManage)) return Results.Forbid();
        await commissions.RestoreAsync(id, ct);
        return Results.NoContent();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private record EntryRow(
        Guid Id, Guid AgentId, string AgentName, Guid CallRecordId, string? OrderNumber, string Client, string Campaign,
        string EntryType, string RuleName, string Description, decimal Amount, string? Note, DateTimeOffset OccurredAt, string Date);

    private static async Task<List<EntryRow>> EntryRowsAsync(
        TenantDbContext db, TenantContext tenant, PayPeriod p, Guid? agentId, CancellationToken ct)
    {
        var (from, to) = Bounds(tenant, p);
        var q = db.CommissionEntries.AsNoTracking().Where(e => e.OccurredAt >= from && e.OccurredAt < to);
        if (agentId is not null) q = q.Where(e => e.AgentId == agentId);
        var entries = await q.OrderBy(e => e.OccurredAt).ToListAsync(ct);

        var names = await AgentNamesAsync(db, entries.Select(e => e.AgentId), ct);
        var callIds = entries.Select(e => e.CallRecordId).Distinct().ToList();
        // The order number of the interaction an entry was earned on (S178): same agent + campaign, else the call's first.
        var ixOrders = await db.CallInteractions.AsNoTracking().Where(i => callIds.Contains(i.CallRecordId))
            .Select(i => new { i.CallRecordId, i.AgentId, i.CampaignId, i.OrderNumber, i.StartedAt }).ToListAsync(ct);
        string? OrderFor(CommissionEntry e) =>
            (ixOrders.FirstOrDefault(i => i.CallRecordId == e.CallRecordId && i.AgentId == e.AgentId && i.CampaignId == e.CampaignId)
             ?? ixOrders.Where(i => i.CallRecordId == e.CallRecordId).OrderBy(i => i.StartedAt).FirstOrDefault())?.OrderNumber;
        var clients = await db.Clients.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var campaigns = await db.Campaigns.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var tz = Zone(tenant);

        return entries.Select(e => new EntryRow(
            e.Id, e.AgentId, names.GetValueOrDefault(e.AgentId, "(unknown agent)"), e.CallRecordId, OrderFor(e),
            clients.GetValueOrDefault(e.ClientId, ""), campaigns.GetValueOrDefault(e.CampaignId, ""),
            e.EntryType, e.RuleName, e.Description, e.Amount, e.Note, e.OccurredAt,
            TimeZoneInfo.ConvertTime(e.OccurredAt, tz).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))).ToList();
    }

    private static async Task<Dictionary<Guid, string>> AgentNamesAsync(TenantDbContext db, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await db.Agents.AsNoTracking().Where(a => list.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => (a.FirstName + " " + a.LastName).Trim(), ct);
    }

    private static PayPeriod CurrentPeriod(TenantContext tenant)
    {
        var s = tenant.Current!.Settings;
        return PayPeriods.Containing(MediaAttributionResolver.TodayIn(tenant.Current.Timezone), s.PayPeriodFrequency, PayPeriods.AnchorOf(s.PayPeriodStart));
    }

    private static PayPeriod ResolvePeriod(TenantContext tenant, string? period, string? start)
    {
        var s = tenant.Current!.Settings;
        var anchor = PayPeriods.AnchorOf(s.PayPeriodStart);
        if (DateOnly.TryParse(start, CultureInfo.InvariantCulture, out var date))
            return PayPeriods.Containing(date, s.PayPeriodFrequency, anchor);
        var current = CurrentPeriod(tenant);
        return period == "previous" ? current.Previous(s.PayPeriodFrequency, anchor) : current;
    }

    /// <summary>The period's [from, to) instants: tenant-local midnight of Start to midnight after End,
    /// as UTC — Npgsql only accepts offset-0 values for timestamptz parameters.</summary>
    private static (DateTimeOffset From, DateTimeOffset To) Bounds(TenantContext tenant, PayPeriod p)
    {
        var tz = Zone(tenant);
        DateTimeOffset Midnight(DateOnly d)
        {
            var local = d.ToDateTime(TimeOnly.MinValue);
            return new DateTimeOffset(local, tz.GetUtcOffset(local)).ToUniversalTime();
        }
        return (Midnight(p.Start), Midnight(p.End.AddDays(1)));
    }

    private static TimeZoneInfo Zone(TenantContext tenant)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(tenant.Current?.Timezone ?? "UTC"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
    }

    private static object PeriodJson(PayPeriod p) => new { start = p.Start.ToString("yyyy-MM-dd"), end = p.End.ToString("yyyy-MM-dd"), label = p.Label };

    private static bool Has(HttpContext http, string permission) =>
        (http.User.FindFirst("permissions")?.Value ?? "").Split(',').Contains(permission);

    private static string Money(decimal d) => d.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Csv(string? s)
    {
        s ??= "";
        // Neutralize spreadsheet formulas, then quote.
        if (s.Length > 0 && "=+-@".Contains(s[0])) s = "'" + s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}

/// <summary>EffectiveFrom / EffectiveUntil are tenant-local "yyyy-MM-ddTHH:mm"; blank = open.</summary>
public record RuleRequest(
    Guid? ClientId, Guid? CampaignId, string? Name, string? Kind, decimal Amount, Guid? ProductId,
    string? FieldName, string? FieldValue, string? TierLabel, bool? IsActive,
    string? EffectiveFrom = null, string? EffectiveUntil = null);

/// <summary>From / To are tenant-local "yyyy-MM-ddTHH:mm"; calls that STARTED in [From, To).
/// PostTo = current | call_date.</summary>
public record RecalcRequest(
    Guid? ClientId, Guid? CampaignId, Guid? AgentId, string? From, string? To, string? PostTo = null, string? Reason = null);

public record SettingsRequest(string? Frequency, string? Start);

public record ReverseRequest(string? Reason);
