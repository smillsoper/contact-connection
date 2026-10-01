using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Media;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// Media Agency Phase A (S171): the tenant's media agencies (each with its own per-number field names)
/// and each phone number's media assignments — its attribution history. The assignment in effect when a
/// call arrives is copied onto the call record (EslBackgroundService → MediaAttributionResolver).
/// </summary>
public static class MediaEndpoints
{
    public static IEndpointRouteBuilder MapMediaEndpoints(this IEndpointRouteBuilder app)
    {
        var agencies = app.MapGroup("/api/v1/media-agencies").RequireAuthorization("TenantAdmin");
        agencies.MapGet("", ListAgencies);
        agencies.MapPost("", CreateAgency);
        agencies.MapPut("{id:guid}", UpdateAgency);

        app.MapGet("/api/v1/phone-numbers/{id:guid}/media-assignments", ListAssignments).RequireAuthorization("TenantAdmin");
        app.MapPost("/api/v1/phone-numbers/{id:guid}/media-assignments", AddAssignment).RequireAuthorization("TenantAdmin");
        app.MapPut("/api/v1/media-assignments/{id:guid}", UpdateAssignment).RequireAuthorization("TenantAdmin");
        app.MapDelete("/api/v1/media-assignments/{id:guid}", DeleteAssignment).RequireAuthorization("TenantAdmin");

        app.MapGet("/api/v1/broadcast-stations", SearchStations).RequireAuthorization("TenantAdmin");
        return app;
    }

    // ── Agencies ───────────────────────────────────────────────────────────────

    private static async Task<IResult> ListAgencies(TenantContext tenant, ScopedTenantDbContextFactory dbFactory, CancellationToken ct)
    {
        if (!tenant.HasTenant) return Results.Unauthorized();
        await using var db = dbFactory.Create();
        var list = await db.MediaAgencies.AsNoTracking().OrderBy(a => a.Name).ToListAsync(ct);
        return Results.Ok(list.Select(AgencyResponse));
    }

    private static async Task<IResult> CreateAgency(AgencyRequest req, TenantContext tenant, ScopedTenantDbContextFactory dbFactory, CancellationToken ct)
    {
        if (!tenant.HasTenant) return Results.Unauthorized();
        await using var db = dbFactory.Create();
        if (await db.MediaAgencies.AnyAsync(a => a.Name.ToLower() == (req.Name ?? "").Trim().ToLower(), ct))
            return Results.Conflict(new { error = $"An agency named '{req.Name?.Trim()}' already exists." });
        try
        {
            var agency = MediaAgency.Create(tenant.Current!.Id, req.Name ?? "", req.Fields ?? []);
            db.MediaAgencies.Add(agency);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/media-agencies/{agency.Id}", AgencyResponse(agency));
        }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    }

    private static async Task<IResult> UpdateAgency(Guid id, AgencyRequest req, TenantContext tenant, ScopedTenantDbContextFactory dbFactory, CancellationToken ct)
    {
        if (!tenant.HasTenant) return Results.Unauthorized();
        await using var db = dbFactory.Create();
        var agency = await db.MediaAgencies.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (agency is null) return Results.NotFound();
        try { agency.Update(req.Name ?? "", req.Fields ?? [], req.IsActive ?? agency.IsActive); }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        await db.SaveChangesAsync(ct);
        return Results.Ok(AgencyResponse(agency));
    }

    private static object AgencyResponse(MediaAgency a) => new { a.Id, a.Name, a.IsActive, a.Fields };

    // ── Assignments ────────────────────────────────────────────────────────────

    private static async Task<IResult> ListAssignments(Guid id, TenantContext tenant, ScopedTenantDbContextFactory dbFactory, CancellationToken ct)
    {
        if (!tenant.HasTenant) return Results.Unauthorized();
        await using var db = dbFactory.Create();
        if (!await db.PhoneNumbers.AnyAsync(p => p.Id == id, ct)) return Results.NotFound();

        var assignments = await db.MediaAssignments.AsNoTracking().Where(a => a.PhoneNumberId == id).ToListAsync(ct);
        var names = await db.MediaAgencies.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Name, ct);
        var today = MediaAttributionResolver.TodayIn(tenant.Current!.Timezone);
        var current = MediaAssignmentRules.Resolve(assignments, today);
        return Results.Ok(new
        {
            today,
            currentAssignmentId = current?.Id,
            assignments = assignments
                .OrderByDescending(a => a.StartDate).ThenBy(a => a.MarketType)
                .Select(a => AssignmentResponse(a, names.GetValueOrDefault(a.MediaAgencyId, "(unknown agency)"), today)),
        });
    }

    private static async Task<IResult> AddAssignment(
        Guid id, AssignmentRequest req, HttpContext http, TenantContext tenant, ScopedTenantDbContextFactory dbFactory,
        ContactConnectionDbContext platformDb, CancellationToken ct)
    {
        if (!tenant.HasTenant) return Results.Unauthorized();
        await using var db = dbFactory.Create();
        if (!await db.PhoneNumbers.AnyAsync(p => p.Id == id, ct)) return Results.NotFound();
        if (req.StartDate is null) return Results.BadRequest(new { error = "A start date is required." });

        var agency = await db.MediaAgencies.FirstOrDefaultAsync(a => a.Id == req.MediaAgencyId, ct);
        if (agency is null) return Results.BadRequest(new { error = "Choose a media agency." });
        if (!agency.IsActive) return Results.BadRequest(new { error = $"{agency.Name} is inactive." });
        if (MissingRequired(agency, req.FieldValues) is { } missing) return Results.BadRequest(new { error = missing });

        var existing = await db.MediaAssignments.Where(a => a.PhoneNumberId == id).ToListAsync(ct);
        try
        {
            var a = MediaAssignment.Create(tenant.Current!.Id, id, req.MarketType ?? "", agency.Id, req.Station ?? "",
                req.StartDate.Value, CreatedBy(http));
            if (await ApplyStationAsync(a, req, platformDb, ct) is { } stationError) return Results.BadRequest(new { error = stationError });
            a.SetDetails(req.MediaType, req.AdType);
            a.SetFieldValues(req.FieldValues ?? []);
            if (a.MarketType == MediaMarketType.National)
                MediaAssignmentRules.FitNational(a, existing);
            else
            {
                a.SetEndDate(req.EndDate);
                // The first Local one on a number is its default until another is chosen.
                var anyDefault = existing.Any(x => x.MarketType == MediaMarketType.Local && x.IsDefaultLocal);
                if (req.IsDefaultLocal == true || !anyDefault) MediaAssignmentRules.MakeDefaultLocal(a, existing);
            }
            db.MediaAssignments.Add(a);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/media-assignments/{a.Id}", AssignmentResponse(a, agency.Name, MediaAttributionResolver.TodayIn(tenant.Current!.Timezone)));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    // Start date, market type and agency are fixed once created — a different buy is a new assignment.
    private static async Task<IResult> UpdateAssignment(
        Guid id, AssignmentRequest req, TenantContext tenant, ScopedTenantDbContextFactory dbFactory,
        ContactConnectionDbContext platformDb, CancellationToken ct)
    {
        if (!tenant.HasTenant) return Results.Unauthorized();
        await using var db = dbFactory.Create();
        var a = await db.MediaAssignments.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return Results.NotFound();
        var agency = await db.MediaAgencies.AsNoTracking().FirstOrDefaultAsync(x => x.Id == a.MediaAgencyId, ct);
        if (agency is not null && MissingRequired(agency, req.FieldValues) is { } missing) return Results.BadRequest(new { error = missing });
        try
        {
            if (req.Station is not null && await ApplyStationAsync(a, req, platformDb, ct) is { } stationError)
                return Results.BadRequest(new { error = stationError });
            a.SetDetails(req.MediaType, req.AdType);
            if (req.FieldValues is not null) a.SetFieldValues(req.FieldValues);
            if (a.MarketType == MediaMarketType.Local)
            {
                a.SetEndDate(req.EndDate);
                if (req.IsDefaultLocal == true)
                    MediaAssignmentRules.MakeDefaultLocal(a, await db.MediaAssignments.Where(x => x.PhoneNumberId == a.PhoneNumberId).ToListAsync(ct));
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(AssignmentResponse(a, agency?.Name ?? "", MediaAttributionResolver.TodayIn(tenant.Current!.Timezone)));
        }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    }

    // For mistakes. A deleted National's predecessor gets its original end back, so the history stays
    // seamless. Calls already attributed keep their copy — nothing references the assignment.
    private static async Task<IResult> DeleteAssignment(Guid id, TenantContext tenant, ScopedTenantDbContextFactory dbFactory, CancellationToken ct)
    {
        if (!tenant.HasTenant) return Results.Unauthorized();
        await using var db = dbFactory.Create();
        var a = await db.MediaAssignments.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return Results.NotFound();
        if (a.MarketType == MediaMarketType.National)
        {
            var previous = await db.MediaAssignments.FirstOrDefaultAsync(x =>
                x.PhoneNumberId == a.PhoneNumberId && x.MarketType == MediaMarketType.National && x.Id != a.Id
                && x.EndDate == a.StartDate.AddDays(-1), ct);
            previous?.SetEndDate(a.EndDate);
        }
        db.MediaAssignments.Remove(a);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    /// <summary>Sets the station — linked to its FCC facility (with coordinates) when one was picked from
    /// the list, otherwise free text.</summary>
    private static async Task<string?> ApplyStationAsync(MediaAssignment a, AssignmentRequest req, ContactConnectionDbContext platformDb, CancellationToken ct)
    {
        if (req.StationFacilityId is not { } facilityId)
        {
            a.SetStation(req.Station ?? "");
            return null;
        }
        var station = await platformDb.BroadcastStations.AsNoTracking().FirstOrDefaultAsync(s => s.FacilityId == facilityId, ct);
        if (station is null) return $"FCC facility {facilityId} isn't in the station list.";
        a.SetStation(string.IsNullOrWhiteSpace(req.Station) ? station.CallSign : req.Station, station.FacilityId, station.Latitude, station.Longitude);
        return null;
    }

    // ── FCC stations (platform-wide, imported daily by the Worker) ─────────────

    /// <summary>Call-sign prefix matches first, then community city matches. <paramref name="q"/> may
    /// end with a state ("KABC CA", "Phoenix AZ"); <paramref name="service"/> = tv | radio ranks that kind first.</summary>
    private static async Task<IResult> SearchStations(string? q, string? service, ContactConnectionDbContext platformDb, CancellationToken ct)
    {
        var terms = (q ?? "").Trim().ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (terms.Count == 0) return Results.Ok(Array.Empty<object>());
        string? state = terms.Count > 1 && terms[^1].Length == 2 ? terms[^1] : null;
        if (state is not null) terms.RemoveAt(terms.Count - 1);
        var text = string.Join(' ', terms);

        var query = platformDb.BroadcastStations.AsNoTracking();
        if (state is not null) query = query.Where(s => s.CommunityState == state);
        // The assignment's media type only ranks stations of that kind first — never hides the rest, so a
        // media type set before the station can't make a valid station look missing (S171).
        var preferred = service?.ToLowerInvariant() switch
        {
            "tv" => FccStationImporter.TvServices,
            "radio" => FccStationImporter.RadioServices,
            _ => [],
        };
        var cityPattern = "%" + text.Replace("%", "").Replace("_", "") + "%";
        var matches = await query
            .Where(s => s.CallSign.StartsWith(text) || EF.Functions.ILike(s.CommunityCity!, cityPattern))
            .OrderBy(s => s.CallSign.StartsWith(text) ? 0 : 1)
            .ThenBy(s => preferred.Contains(s.ServiceCode) ? 0 : 1)
            .ThenBy(s => s.CallSign)
            .Take(25)
            .Select(s => new
            {
                s.FacilityId, s.CallSign, s.ServiceCode, s.CommunityCity, s.CommunityState,
                s.Latitude, s.Longitude, s.NetworkAffiliation,
            })
            .ToListAsync(ct);
        return Results.Ok(matches);
    }

    private static string? MissingRequired(MediaAgency agency, Dictionary<string, string>? values)
    {
        var missing = agency.Fields
            .Where(f => f.Required && string.IsNullOrWhiteSpace(values?.GetValueOrDefault(f.Name)))
            .Select(f => f.Name).ToList();
        return missing.Count == 0 ? null : $"{agency.Name} requires: {string.Join(", ", missing)}.";
    }

    private static string? CreatedBy(HttpContext http)
    {
        var name = $"{http.User.FindFirst("given_name")?.Value} {http.User.FindFirst("family_name")?.Value}".Trim();
        return name.Length == 0 ? null : name;
    }

    private static object AssignmentResponse(MediaAssignment a, string agencyName, DateOnly today) => new
    {
        a.Id, a.PhoneNumberId, a.MarketType, a.MediaAgencyId, agencyName, a.Station,
        a.StationFacilityId, a.StationLatitude, a.StationLongitude, a.MediaType, a.AdType,
        a.StartDate, a.EndDate, a.IsDefaultLocal, a.FieldValues, a.CreatedByName, a.CreatedAt,
        inEffect = a.InEffectOn(today),
    };
}

public record AgencyRequest(string? Name, List<MediaAgencyField>? Fields, bool? IsActive);

public record AssignmentRequest(
    string? MarketType, Guid MediaAgencyId, string? Station, int? StationFacilityId, string? MediaType, string? AdType,
    DateOnly? StartDate, DateOnly? EndDate, bool? IsDefaultLocal, Dictionary<string, string>? FieldValues);
