using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Kpis;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// KPI widget data + tenant-defined KPIs (S181, docs/dispositions-kpi-plan.md). Production calls only; interactions counted
/// by their own campaign; disposition KPIs follow the current catalog mapping. Pushed to dashboards (agent-sessions /
/// call-state / agent-state events), never polled.
/// </summary>
public static class KpiEndpoints
{
    public static IEndpointRouteBuilder MapKpiEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/dashboard-widgets/kpi", Get).RequireAuthorization("ReportsView");

        var g = app.MapGroup("/api/v1/custom-kpis");
        g.MapGet("", List).RequireAuthorization();
        g.MapGet("variables", Variables).RequireAuthorization();
        g.MapPost("validate", ValidateFormula).RequireAuthorization();
        g.MapPost("", Create).RequireAuthorization("TenantAdmin");
        g.MapPut("{id:guid}", Update).RequireAuthorization("TenantAdmin");
        g.MapDelete("{id:guid}", Delete).RequireAuthorization("TenantAdmin");
        return app;
    }

    private static async Task<IResult> Get(
        Guid? campaignId, Guid? clientId, string? groupBy, string? groupBy2, string? timeWindowMode, int? timeWindowValue,
        Guid? groupId, string? dnis, IAgentGroupRepository agentGroups,
        KpiService kpis, TenantContext tc, CancellationToken ct)
    {
        if (tc.Current is not { } tenant) return Results.Unauthorized();
        var (since, until) = Window(tenant.Timezone, timeWindowMode, timeWindowValue, DateTimeOffset.UtcNow);
        var result = await kpis.ComputeAsync(new KpiQuery(since, until, clientId, campaignId,
            KpiDimension.IsValid(groupBy) ? groupBy! : "none", KpiDimension.IsValid(groupBy2) ? groupBy2 : null, tenant.Timezone,
            GroupId: groupId, DnisKeys: WidgetFilters.Dnis(dnis)), ct);
        return Results.Ok(result);
    }

    /// <summary>The KPI window, ending now (or at the end of the previous day for "yesterday"). Calendar windows are in the
    /// tenant's time zone; this week starts Monday.</summary>
    internal static (DateTimeOffset Since, DateTimeOffset Until) Window(string tenantTimezone, string? mode, int? value, DateTimeOffset nowUtc)
    {
        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(tenantTimezone); }
        catch (Exception) { tz = TimeZoneInfo.Utc; }
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, tz).DateTime);
        DateTimeOffset Midnight(DateOnly d) => new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(d.ToDateTime(TimeOnly.MinValue), tz), TimeSpan.Zero);

        return mode switch
        {
            "hours" => (nowUtc.AddHours(-(value is > 0 ? value.Value : 1)), nowUtc),
            "minutes" => (nowUtc.AddMinutes(-(value is > 0 ? value.Value : 30)), nowUtc),
            "yesterday" => (Midnight(today.AddDays(-1)), Midnight(today)),
            "week" => (Midnight(today.AddDays(-(((int)today.DayOfWeek + 6) % 7))), nowUtc),
            "month" => (Midnight(new DateOnly(today.Year, today.Month, 1)), nowUtc),
            _ => (Midnight(today), nowUtc),
        };
    }

    // ── Custom KPIs ────────────────────────────────────────────────────────────

    private static async Task<IResult> List(ScopedTenantDbContextFactory dbf, TenantContext tc, CancellationToken ct)
    {
        if (!tc.HasTenant) return Results.Unauthorized();
        await using var db = dbf.Create();
        return Results.Ok(await db.CustomKpis.AsNoTracking().OrderBy(k => k.DisplayOrder).ThenBy(k => k.Name).ToListAsync(ct));
    }

    private static async Task<IResult> Create(CustomKpiRequest req, ScopedTenantDbContextFactory dbf, TenantContext tc, CancellationToken ct)
    {
        if (tc.Current is not { } tenant) return Results.Unauthorized();
        await using var db = dbf.Create();
        if (await ValidateAsync(db, req, ct) is { } error) return error;
        try
        {
            var k = CustomKpi.Create(tenant.Id, req.Name, req.Description, req.NumeratorCategoryIds ?? [], req.DenominatorCategoryIds ?? [], req.DisplayOrder ?? 0,
                req.Kind ?? "ratio", req.Formula, req.Format ?? "percent");
            db.CustomKpis.Add(k);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/custom-kpis/{k.Id}", k);
        }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    }

    private static async Task<IResult> Update(Guid id, CustomKpiRequest req, ScopedTenantDbContextFactory dbf, TenantContext tc, CancellationToken ct)
    {
        if (!tc.HasTenant) return Results.Unauthorized();
        await using var db = dbf.Create();
        var k = await db.CustomKpis.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (k is null) return Results.NotFound();
        if (await ValidateAsync(db, req, ct) is { } error) return error;
        try
        {
            k.Update(req.Name, req.Description, req.NumeratorCategoryIds ?? [], req.DenominatorCategoryIds ?? [], req.DisplayOrder ?? k.DisplayOrder,
                req.Kind ?? k.Kind, req.Formula ?? k.Formula, req.Format ?? k.Format);
            if (req.IsActive is { } active) k.SetActive(active);
            await db.SaveChangesAsync(ct);
            return Results.Ok(k);
        }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    }

    private static async Task<IResult> Delete(Guid id, ScopedTenantDbContextFactory dbf, TenantContext tc, CancellationToken ct)
    {
        if (!tc.HasTenant) return Results.Unauthorized();
        await using var db = dbf.Create();
        var k = await db.CustomKpis.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (k is null) return Results.NotFound();
        db.CustomKpis.Remove(k);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> Variables(KpiService kpis, TenantContext tc, CancellationToken ct) =>
        !tc.HasTenant ? Results.Unauthorized() : Results.Ok(await kpis.VariablesAsync(ct));

    /// <summary>The designer's live check: null error = usable.</summary>
    private static async Task<IResult> ValidateFormula(FormulaRequest req, KpiService kpis, TenantContext tc, CancellationToken ct)
    {
        if (!tc.HasTenant) return Results.Unauthorized();
        var known = (await kpis.VariablesAsync(ct)).Select(v => v.Name);
        return Results.Ok(new { error = KpiFormula.Validate(req.Formula ?? "", known) });
    }

    private static async Task<IResult?> ValidateAsync(TenantDbContext db, CustomKpiRequest req, CancellationToken ct)
    {
        if (req.Kind == "formula")
        {
            if (!KpiFormat.IsValid(req.Format ?? KpiFormat.Percent)) return Results.BadRequest(new { error = $"Unknown format '{req.Format}'." });
            var names = await KpiService.VariableNamesAsync(db, ct);
            var variableNames = KpiFormula.Fixed.Select(v => v.Name).Concat(names.Categories.Values).Concat(names.Dispositions.Values);
            return KpiFormula.Validate(req.Formula ?? "", variableNames) is { } formulaError ? Results.BadRequest(new { error = formulaError }) : null;
        }
        var ids = (req.NumeratorCategoryIds ?? []).Concat(req.DenominatorCategoryIds ?? []).Distinct().ToList();
        var known = await db.DispositionCategories.Where(c => ids.Contains(c.Id)).CountAsync(ct);
        return known == ids.Count ? null : Results.BadRequest(new { error = "One of the chosen categories doesn't exist." });
    }

    public sealed record CustomKpiRequest(string Name, string? Description, List<Guid>? NumeratorCategoryIds,
        List<Guid>? DenominatorCategoryIds, int? DisplayOrder, bool? IsActive,
        string? Kind = null, string? Formula = null, string? Format = null);
    public sealed record FormulaRequest(string? Formula);
}
