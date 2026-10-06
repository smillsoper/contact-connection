using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// The disposition catalog (S181, docs/dispositions-kpi-plan.md): reporting categories, dispositions scoped to the tenant /
/// a client / a campaign, and the Unmapped list of recorded texts that match nothing. Every catalog change re-links past
/// interactions, so KPIs always follow the current mapping. Reads are open to any signed-in user (the Flow Designer and
/// KPI widget need them); changes need tenant admin.
/// </summary>
public static class DispositionsEndpoints
{
    public static IEndpointRouteBuilder MapDispositionsEndpoints(this IEndpointRouteBuilder app)
    {
        var cat = app.MapGroup("/api/v1/disposition-categories");
        cat.MapGet("", Categories).RequireAuthorization();
        cat.MapPost("", CreateCategory).RequireAuthorization("TenantAdmin");
        cat.MapPut("{id:guid}", UpdateCategory).RequireAuthorization("TenantAdmin");
        cat.MapPost("{id:guid}/active", SetCategoryActive).RequireAuthorization("TenantAdmin");

        var d = app.MapGroup("/api/v1/dispositions");
        d.MapGet("", List).RequireAuthorization();
        d.MapGet("for-campaign/{campaignId:guid}", ForCampaign).RequireAuthorization();
        d.MapGet("unmapped", Unmapped).RequireAuthorization("TenantAdmin");
        d.MapPost("unmapped/resolve", ResolveUnmapped).RequireAuthorization("TenantAdmin");
        d.MapPost("", Create).RequireAuthorization("TenantAdmin");
        d.MapPut("{id:guid}", Update).RequireAuthorization("TenantAdmin");
        d.MapPost("{id:guid}/active", SetActive).RequireAuthorization("TenantAdmin");
        d.MapDelete("{id:guid}", Delete).RequireAuthorization("TenantAdmin");
        return app;
    }

    // ── Categories ─────────────────────────────────────────────────────────────

    private static async Task<IResult> Categories(IDispositionService svc, TenantContext tc, CancellationToken ct) =>
        !tc.HasTenant ? Results.Unauthorized() : Results.Ok((await svc.CategoriesAsync(ct)).Select(CategoryResponse));

    private static async Task<IResult> CreateCategory(CategoryRequest req, IDispositionService svc, ScopedTenantDbContextFactory dbf,
        TenantContext tc, CancellationToken ct)
    {
        if (tc.Current is not { } tenant) return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest(new { error = "Name the category." });
        var existing = await svc.CategoriesAsync(ct);
        if (existing.Any(c => Disposition.Normalize(c.Name) == Disposition.Normalize(req.Name)))
            return Results.Conflict(new { error = $"There's already a category named '{req.Name.Trim()}'." });
        await using var db = dbf.Create();
        var c = DispositionCategory.Create(tenant.Id, req.Name, req.Description, req.SalesOpportunity, req.ExcludedFromKpis,
            req.DisplayOrder ?? (existing.Count == 0 ? 100 : existing.Max(x => x.DisplayOrder) + 10));
        try { c.SetRecordingRule(Blank(req.RecordingAction), req.RecordingRetentionDays); }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        db.DispositionCategories.Add(c);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/disposition-categories/{c.Id}", CategoryResponse(c));
    }

    private static async Task<IResult> UpdateCategory(Guid id, CategoryRequest req, ScopedTenantDbContextFactory dbf, TenantContext tc, CancellationToken ct)
    {
        if (!tc.HasTenant) return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest(new { error = "Name the category." });
        await using var db = dbf.Create();
        var c = await db.DispositionCategories.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return Results.NotFound();
        if (await db.DispositionCategories.AnyAsync(x => x.Id != id && x.Name.ToLower() == req.Name.Trim().ToLower(), ct))
            return Results.Conflict(new { error = $"There's already a category named '{req.Name.Trim()}'." });
        c.Update(req.Name, req.Description, req.SalesOpportunity, req.ExcludedFromKpis, req.DisplayOrder ?? c.DisplayOrder);
        try { c.SetRecordingRule(Blank(req.RecordingAction), req.RecordingRetentionDays); }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        await db.SaveChangesAsync(ct);
        return Results.Ok(CategoryResponse(c));
    }

    private static async Task<IResult> SetCategoryActive(Guid id, ActiveRequest req, ScopedTenantDbContextFactory dbf, TenantContext tc, CancellationToken ct)
    {
        if (!tc.HasTenant) return Results.Unauthorized();
        await using var db = dbf.Create();
        var c = await db.DispositionCategories.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return Results.NotFound();
        if (!req.Active && await db.Dispositions.AnyAsync(x => x.CategoryId == id && x.IsActive, ct))
            return Results.Conflict(new { error = "Move its dispositions to another category first." });
        try { c.SetActive(req.Active); }
        catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
        await db.SaveChangesAsync(ct);
        return Results.Ok(CategoryResponse(c));
    }

    // ── Dispositions ───────────────────────────────────────────────────────────

    private static async Task<IResult> List(ScopedTenantDbContextFactory dbf, TenantContext tc, CancellationToken ct)
    {
        if (!tc.HasTenant) return Results.Unauthorized();
        await using var db = dbf.Create();
        var list = await db.Dispositions.AsNoTracking().OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name).ToListAsync(ct);
        var counts = await db.CallInteractions.AsNoTracking().Where(i => i.DispositionId != null)
            .GroupBy(i => i.DispositionId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key!.Value, x => x.Count, ct);
        return Results.Ok(list.Select(x => Response(x, counts.GetValueOrDefault(x.Id))));
    }

    private static async Task<IResult> ForCampaign(Guid campaignId, IDispositionService svc, TenantContext tc, CancellationToken ct) =>
        !tc.HasTenant ? Results.Unauthorized() : Results.Ok((await svc.ForCampaignAsync(campaignId, ct)).Select(x => Response(x, 0)));

    private static async Task<IResult> Create(DispositionRequest req, IDispositionService svc, ScopedTenantDbContextFactory dbf,
        TenantContext tc, CancellationToken ct)
    {
        if (tc.Current is not { } tenant) return Results.Unauthorized();
        await using var db = dbf.Create();
        if (await ValidateAsync(db, req, null, ct) is { } error) return error;
        var scope = await ScopeAsync(db, req.ClientId, req.CampaignId, ct);
        if (scope.Error is { } scopeError) return Results.BadRequest(new { error = scopeError });
        var d = Disposition.Create(tenant.Id, req.Name, req.Code, req.CategoryId, scope.ClientId, req.CampaignId, req.Aliases, req.DisplayOrder ?? 0);
        try { d.SetRecordingRule(Blank(req.RecordingAction), req.RecordingRetentionDays); }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        db.Dispositions.Add(d);
        await db.SaveChangesAsync(ct);
        var relinked = await svc.RelinkAllAsync(ct);
        return Results.Created($"/api/v1/dispositions/{d.Id}", new { disposition = Response(d, 0), relinked });
    }

    private static async Task<IResult> Update(Guid id, DispositionRequest req, IDispositionService svc, ScopedTenantDbContextFactory dbf,
        TenantContext tc, CancellationToken ct)
    {
        if (!tc.HasTenant) return Results.Unauthorized();
        await using var db = dbf.Create();
        var d = await db.Dispositions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return Results.NotFound();
        if (await ValidateAsync(db, req with { ClientId = d.ClientId, CampaignId = d.CampaignId }, id, ct) is { } error) return error;
        d.Update(req.Name, req.Code, req.CategoryId, req.Aliases ?? [], req.DisplayOrder ?? d.DisplayOrder);
        try { d.SetRecordingRule(Blank(req.RecordingAction), req.RecordingRetentionDays); }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        await db.SaveChangesAsync(ct);
        var relinked = await svc.RelinkAllAsync(ct);
        return Results.Ok(new { disposition = Response(d, 0), relinked });
    }

    /// <summary>Retire / restore. A retired disposition can't be picked any more, but past calls stay linked to it.</summary>
    private static async Task<IResult> SetActive(Guid id, ActiveRequest req, IDispositionService svc, ScopedTenantDbContextFactory dbf,
        TenantContext tc, CancellationToken ct)
    {
        if (!tc.HasTenant) return Results.Unauthorized();
        await using var db = dbf.Create();
        var d = await db.Dispositions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return Results.NotFound();
        d.SetActive(req.Active);
        await db.SaveChangesAsync(ct);
        var relinked = await svc.RelinkAllAsync(ct);
        return Results.Ok(new { disposition = Response(d, 0), relinked });
    }

    /// <summary>Only a disposition no call ever recorded (a typo) can be deleted — anything used is retired instead.</summary>
    private static async Task<IResult> Delete(Guid id, ScopedTenantDbContextFactory dbf, TenantContext tc, CancellationToken ct)
    {
        if (!tc.HasTenant) return Results.Unauthorized();
        await using var db = dbf.Create();
        var d = await db.Dispositions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return Results.NotFound();
        if (await db.CallInteractions.AnyAsync(i => i.DispositionId == id, ct))
            return Results.Conflict(new { error = "Calls have recorded this disposition — retire it instead." });
        db.Dispositions.Remove(d);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    // ── Unmapped ───────────────────────────────────────────────────────────────

    private static async Task<IResult> Unmapped(IDispositionService svc, ScopedTenantDbContextFactory dbf, TenantContext tc, CancellationToken ct)
    {
        if (!tc.HasTenant) return Results.Unauthorized();
        await using var db = dbf.Create();
        var names = await db.Campaigns.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        return Results.Ok((await svc.UnmappedAsync(ct)).Select(u => new
        {
            u.Text, u.Count, u.ProductionCount, u.LastSeen,
            Campaigns = u.CampaignIds.Select(c => new { id = c, name = names.GetValueOrDefault(c, "") }),
        }));
    }

    /// <summary>An unmapped text becomes an alias of an existing disposition, or a new disposition — then every interaction
    /// that recorded it is linked.</summary>
    private static async Task<IResult> ResolveUnmapped(ResolveRequest req, IDispositionService svc, ScopedTenantDbContextFactory dbf,
        TenantContext tc, CancellationToken ct)
    {
        if (tc.Current is not { } tenant) return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(req.Text)) return Results.BadRequest(new { error = "Which text?" });
        await using var db = dbf.Create();
        if (req.DispositionId is { } existingId)
        {
            var d = await db.Dispositions.FirstOrDefaultAsync(x => x.Id == existingId, ct);
            if (d is null) return Results.NotFound();
            d.AddAlias(req.Text);
        }
        else if (req.Create is { } create)
        {
            var newReq = create with { Name = string.IsNullOrWhiteSpace(create.Name) ? req.Text.Trim() : create.Name, Aliases = create.Aliases ?? [] };
            if (await ValidateAsync(db, newReq, null, ct) is { } error) return error;
            var scope = await ScopeAsync(db, newReq.ClientId, newReq.CampaignId, ct);
            if (scope.Error is { } scopeError) return Results.BadRequest(new { error = scopeError });
            var aliases = Disposition.Normalize(newReq.Name) == Disposition.Normalize(req.Text) ? newReq.Aliases : [.. newReq.Aliases!, req.Text];
            var created = Disposition.Create(tenant.Id, newReq.Name, newReq.Code, newReq.CategoryId, scope.ClientId, newReq.CampaignId, aliases);
            try { created.SetRecordingRule(Blank(newReq.RecordingAction), newReq.RecordingRetentionDays); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            db.Dispositions.Add(created);
        }
        else return Results.BadRequest(new { error = "Choose an existing disposition or create one." });
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { relinked = await svc.RelinkAllAsync(ct) });
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static async Task<IResult?> ValidateAsync(TenantDbContext db, DispositionRequest req, Guid? id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest(new { error = "Name the disposition." });
        if (!await db.DispositionCategories.AnyAsync(c => c.Id == req.CategoryId && c.IsActive, ct))
            return Results.BadRequest(new { error = "Choose a reporting category." });
        // The same name (or alias) can't mean two things in the same scope.
        var sameScope = await db.Dispositions.AsNoTracking()
            .Where(x => x.Id != id && x.ClientId == req.ClientId && x.CampaignId == req.CampaignId).ToListAsync(ct);
        foreach (var text in new[] { req.Name }.Concat(req.Aliases ?? []))
            if (sameScope.FirstOrDefault(x => x.Matches(text)) is { } clash)
                return Results.Conflict(new { error = $"'{text.Trim()}' already means '{clash.Name}' at this scope." });
        return null;
    }

    /// <summary>A campaign-scoped disposition also carries its campaign's client (so client-level matching is exact).</summary>
    private static async Task<(Guid? ClientId, string? Error)> ScopeAsync(TenantDbContext db, Guid? clientId, Guid? campaignId, CancellationToken ct)
    {
        if (campaignId is { } c)
        {
            var client = await db.Campaigns.AsNoTracking().Where(x => x.Id == c).Select(x => (Guid?)x.ClientId).FirstOrDefaultAsync(ct);
            return client is null ? (null, "That campaign doesn't exist.") : (client, null);
        }
        if (clientId is { } cl && !await db.Clients.AnyAsync(x => x.Id == cl, ct)) return (null, "That client doesn't exist.");
        return (clientId, null);
    }

    private static object CategoryResponse(DispositionCategory c) => new
    {
        c.Id, c.Key, c.Name, c.Description, c.SalesOpportunity, c.ExcludedFromKpis, c.DisplayOrder, c.IsActive, c.IsSystem,
        c.RecordingAction, c.RecordingRetentionDays,
    };

    private static object Response(Disposition d, int interactions) => new
    {
        d.Id, d.Name, d.Code, d.CategoryId, d.ClientId, d.CampaignId, d.Aliases, d.DisplayOrder, d.IsActive, Interactions = interactions,
        d.RecordingAction, d.RecordingRetentionDays,
    };

    /// <param name="RecordingAction">keep / conversation / discard; blank = inherit (S181, retain-by-disposition campaigns).</param>
    public sealed record CategoryRequest(string Name, string? Description, bool SalesOpportunity, bool ExcludedFromKpis, int? DisplayOrder,
        string? RecordingAction = null, int? RecordingRetentionDays = null);
    public sealed record ActiveRequest(bool Active);
    public sealed record DispositionRequest(string Name, string? Code, Guid CategoryId, Guid? ClientId, Guid? CampaignId,
        List<string>? Aliases, int? DisplayOrder, string? RecordingAction = null, int? RecordingRetentionDays = null);

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    public sealed record ResolveRequest(string Text, Guid? DispositionId, DispositionRequest? Create);
}
