using System.Globalization;
using System.Text.Json;
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
        app.MapGet("/api/v1/phone-numbers/{id:guid}/media-assignments/changes", ListChanges).RequireAuthorization("TenantAdmin");

        // Replay attribution onto past calls (S171) — preview, then a Worker batch.
        var replay = app.MapGroup("/api/v1/media-replay").RequireAuthorization("TenantAdmin");
        replay.MapGet("numbers", ReplayNumbers);
        replay.MapPost("preview", PreviewReplay);
        replay.MapPost("", StartReplay);
        replay.MapGet("", ListReplays);

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
        var names = await db.MediaAgencies.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var beforeOthers = existing.ToDictionary(x => x.Id, x => Snapshot(x, names));
        try
        {
            var a = MediaAssignment.Create(tenant.Current!.Id, id, req.MarketType ?? "", agency.Id, req.Station ?? "",
                req.StartDate.Value, CreatedBy(http));
            if (await ApplyStationAsync(a, req, platformDb, ct) is { } stationError) return Results.BadRequest(new { error = stationError });
            a.SetDetails(req.MediaType, req.AdType);
            a.SetFieldValues(req.FieldValues ?? []);
            if (a.MarketType == MediaMarketType.National)
            {
                MediaAssignmentRules.FitNational(a, existing);
                MediaAssignmentRules.SetNationalEnd(a, req.EndDate, existing);
            }
            else
            {
                a.SetEndDate(req.EndDate);
                // The first Local one on a number is its default until another is chosen.
                var anyDefault = existing.Any(x => x.MarketType == MediaMarketType.Local && x.IsDefaultLocal);
                if (req.IsDefaultLocal == true || !anyDefault) MediaAssignmentRules.MakeDefaultLocal(a, existing);
            }
            db.MediaAssignments.Add(a);
            Log(db, a, MediaAssignmentChangeAction.Created, $"Added {Describe(a, agency.Name)}", null, Snapshot(a, names), CreatedBy(http));
            LogSideEffects(db, existing, beforeOthers, names, CreatedBy(http));
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/media-assignments/{a.Id}", AssignmentResponse(a, agency.Name, MediaAttributionResolver.TodayIn(tenant.Current!.Timezone)));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    // Everything can be edited, including market, agency and start date (corrections); a different buy from a
    // date is still best added as a new assignment so the history shows the hand-over.
    private static async Task<IResult> UpdateAssignment(
        Guid id, AssignmentRequest req, HttpContext http, TenantContext tenant, ScopedTenantDbContextFactory dbFactory,
        ContactConnectionDbContext platformDb, CancellationToken ct)
    {
        if (!tenant.HasTenant) return Results.Unauthorized();
        await using var db = dbFactory.Create();
        var a = await db.MediaAssignments.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return Results.NotFound();
        // Market, agency and start date can be corrected too (a number moved agencies, a start date entered wrong).
        var agencyId = req.MediaAgencyId != Guid.Empty ? req.MediaAgencyId : a.MediaAgencyId;
        var agency = await db.MediaAgencies.AsNoTracking().FirstOrDefaultAsync(x => x.Id == agencyId, ct);
        if (agency is null) return Results.BadRequest(new { error = "Choose a media agency." });
        if (agencyId != a.MediaAgencyId && !agency.IsActive) return Results.BadRequest(new { error = $"{agency.Name} is inactive." });
        if (MissingRequired(agency, req.FieldValues) is { } missing) return Results.BadRequest(new { error = missing });
        var names = await db.MediaAgencies.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var before = Snapshot(a, names);
        var others = await db.MediaAssignments.Where(x => x.PhoneNumberId == a.PhoneNumberId && x.Id != a.Id).ToListAsync(ct);
        var beforeOthers = others.ToDictionary(x => x.Id, x => Snapshot(x, names));
        try
        {
            MediaAssignmentRules.Reassign(a, req.MarketType ?? a.MarketType, agencyId, req.StartDate ?? a.StartDate, others);
            if (req.Station is not null && await ApplyStationAsync(a, req, platformDb, ct) is { } stationError)
                return Results.BadRequest(new { error = stationError });
            a.SetDetails(req.MediaType, req.AdType);
            if (req.FieldValues is not null) a.SetFieldValues(req.FieldValues);
            if (a.MarketType == MediaMarketType.Local)
            {
                a.SetEndDate(req.EndDate);
                if (req.IsDefaultLocal == true)
                    MediaAssignmentRules.MakeDefaultLocal(a, others);
            }
            else MediaAssignmentRules.SetNationalEnd(a, req.EndDate, others);
            var after = Snapshot(a, names);
            if (after != before)
                Log(db, a, MediaAssignmentChangeAction.Edited, $"Edited {Describe(a, agency?.Name ?? "")}: {Diff(before, after)}", before, after, CreatedBy(http));
            LogSideEffects(db, others, beforeOthers, names, CreatedBy(http));
            await db.SaveChangesAsync(ct);
            return Results.Ok(AssignmentResponse(a, agency?.Name ?? "", MediaAttributionResolver.TodayIn(tenant.Current!.Timezone)));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    // For mistakes. A deleted National's predecessor gets its original end back, so the history stays
    // seamless. Calls already attributed keep their copy — nothing references the assignment.
    private static async Task<IResult> DeleteAssignment(Guid id, HttpContext http, TenantContext tenant, ScopedTenantDbContextFactory dbFactory, CancellationToken ct)
    {
        if (!tenant.HasTenant) return Results.Unauthorized();
        await using var db = dbFactory.Create();
        var a = await db.MediaAssignments.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return Results.NotFound();
        var names = await db.MediaAgencies.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        Log(db, a, MediaAssignmentChangeAction.Deleted, $"Deleted {Describe(a, names.GetValueOrDefault(a.MediaAgencyId, ""))}",
            Snapshot(a, names), null, CreatedBy(http));
        if (a.MarketType == MediaMarketType.National)
        {
            var previous = await db.MediaAssignments.FirstOrDefaultAsync(x =>
                x.PhoneNumberId == a.PhoneNumberId && x.MarketType == MediaMarketType.National && x.Id != a.Id
                && x.EndDate == a.StartDate.AddDays(-1), ct);
            if (previous is not null)
            {
                var before = Snapshot(previous, names);
                previous.SetEndDate(a.EndDate);
                Log(db, previous, MediaAssignmentChangeAction.Ended,
                    $"{Describe(previous, names.GetValueOrDefault(previous.MediaAgencyId, ""))} gets its end date back ({previous.EndDate?.ToString("yyyy-MM-dd") ?? "open-ended"}) after the next one was deleted",
                    before, Snapshot(previous, names), CreatedBy(http));
            }
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

    // ── Change log ─────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions SnapshotJson = new(JsonSerializerDefaults.Web);

    private static string Snapshot(MediaAssignment a, Dictionary<Guid, string> agencyNames) => JsonSerializer.Serialize(new
    {
        a.MarketType, agency = agencyNames.GetValueOrDefault(a.MediaAgencyId, ""), a.Station, a.StationFacilityId,
        a.MediaType, a.AdType, a.StartDate, a.EndDate, a.IsDefaultLocal, fields = a.FieldValues.OrderBy(kv => kv.Key).ToDictionary(),
    }, SnapshotJson);

    private static string Describe(MediaAssignment a, string agencyName) =>
        $"{(a.MarketType == MediaMarketType.National ? "National" : "Local")} {agencyName} · {a.Station} (from {a.StartDate:yyyy-MM-dd})";

    /// <summary>"station: CNN → FOX, end date: (none) → 2026-10-31".</summary>
    private static string Diff(string before, string after)
    {
        using var b = JsonDocument.Parse(before);
        using var a = JsonDocument.Parse(after);
        var parts = new List<string>();
        foreach (var p in a.RootElement.EnumerateObject())
        {
            var old = b.RootElement.TryGetProperty(p.Name, out var o) ? o.GetRawText() : "null";
            if (old == p.Value.GetRawText()) continue;
            static string Show(string raw) => raw is "null" or "\"\"" ? "(none)" : raw.Trim('"');
            parts.Add($"{p.Name}: {Show(old)} → {Show(p.Value.GetRawText())}");
        }
        return parts.Count == 0 ? "no visible change" : string.Join(", ", parts);
    }

    private static void Log(TenantDbContext db, MediaAssignment a, string action, string summary, string? before, string? after, string? by) =>
        db.MediaAssignmentChanges.Add(MediaAssignmentChange.Create(a.TenantId, a.PhoneNumberId, a.Id, action, summary, before, after, by));

    /// <summary>Logs the other assignments an add/edit changed as a side effect (a National ended by a newer
    /// one, the default Local moved).</summary>
    private static void LogSideEffects(
        TenantDbContext db, List<MediaAssignment> others, Dictionary<Guid, string> before, Dictionary<Guid, string> names, string? by)
    {
        foreach (var o in others)
        {
            var after = Snapshot(o, names);
            if (!before.TryGetValue(o.Id, out var was) || was == after) continue;
            var action = o.EndDate is not null && Diff(was, after).StartsWith("endDate") ? MediaAssignmentChangeAction.Ended : MediaAssignmentChangeAction.DefaultChanged;
            Log(db, o, action, $"{Describe(o, names.GetValueOrDefault(o.MediaAgencyId, ""))}: {Diff(was, after)}", was, after, by);
        }
    }

    private static async Task<IResult> ListChanges(Guid id, TenantContext tenant, ScopedTenantDbContextFactory dbFactory, CancellationToken ct)
    {
        if (!tenant.HasTenant) return Results.Unauthorized();
        await using var db = dbFactory.Create();
        var changes = await db.MediaAssignmentChanges.AsNoTracking().Where(c => c.PhoneNumberId == id)
            .OrderByDescending(c => c.ChangedAt).Take(200)
            .Select(c => new { c.Id, c.AssignmentId, c.Action, c.Summary, c.ChangedBy, c.ChangedAt }).ToListAsync(ct);
        return Results.Ok(changes);
    }

    // ── Replay attribution onto past calls ────────────────────────────────────

    /// <summary>Numbers with media assignments — what a replay can target.</summary>
    private static async Task<IResult> ReplayNumbers(TenantContext tenant, ScopedTenantDbContextFactory dbFactory, CancellationToken ct)
    {
        if (!tenant.HasTenant) return Results.Unauthorized();
        await using var db = dbFactory.Create();
        var ids = await db.MediaAssignments.AsNoTracking().Select(a => a.PhoneNumberId).Distinct().ToListAsync(ct);
        var numbers = await db.PhoneNumbers.AsNoTracking().Where(p => ids.Contains(p.Id))
            .OrderBy(p => p.Number).Select(p => new { p.Id, p.Number, p.ClientNumber, p.Label }).ToListAsync(ct);
        return Results.Ok(numbers);
    }

    private static async Task<IResult> PreviewReplay(
        ReplayRequest req, TenantContext tenant, ScopedTenantDbContextFactory dbFactory, ContactConnectionDbContext platformDb, CancellationToken ct)
    {
        if (!tenant.HasTenant) return Results.Unauthorized();
        var tz = Zone(tenant);
        if (ToScope(req, tz) is not { } scope) return Results.BadRequest(new { error = "Choose a start and an end, the end after the start." });
        await using var db = dbFactory.Create();
        var p = await MediaReplayer.PreviewAsync(db, platformDb, scope, tz, ct);
        return Results.Ok(p);
    }

    private static async Task<IResult> StartReplay(
        ReplayRequest req, HttpContext http, TenantContext tenant, ScopedTenantDbContextFactory dbFactory, CancellationToken ct)
    {
        if (!tenant.HasTenant) return Results.Unauthorized();
        if (ToScope(req, Zone(tenant)) is not { } scope) return Results.BadRequest(new { error = "Choose a start and an end, the end after the start." });
        if (!Guid.TryParse(http.User.FindFirst("sub")?.Value, out var userId)) return Results.Unauthorized();
        await using var db = dbFactory.Create();
        if (await db.MediaReplayBatches.AnyAsync(b => b.Status == CommissionRecalcStatus.Pending || b.Status == CommissionRecalcStatus.Running, ct))
            return Results.Conflict(new { error = "A replay is already running — wait for it to finish." });
        try
        {
            var batch = MediaReplayBatch.Create(tenant.Current!.Id, scope.PhoneNumberId, scope.From, scope.To, req.Reason ?? "", userId, CreatedBy(http));
            db.MediaReplayBatches.Add(batch);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { batch.Id });
        }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    }

    private static async Task<IResult> ListReplays(TenantContext tenant, ScopedTenantDbContextFactory dbFactory, CancellationToken ct)
    {
        if (!tenant.HasTenant) return Results.Unauthorized();
        await using var db = dbFactory.Create();
        var batches = await db.MediaReplayBatches.AsNoTracking().OrderByDescending(b => b.CreatedAt).Take(20).ToListAsync(ct);
        var numberIds = batches.Where(b => b.PhoneNumberId is not null).Select(b => b.PhoneNumberId!.Value).ToList();
        var numbers = await db.PhoneNumbers.AsNoTracking().Where(p => numberIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Number, ct);
        var tz = Zone(tenant);
        return Results.Ok(batches.Select(b => new
        {
            b.Id, b.Status, b.Reason, b.RequestedBy, b.TotalCalls, b.ProcessedCalls, b.ChangedCalls, b.Error, b.CreatedAt, b.CompletedAt,
            from = LocalText(b.From, tz), to = LocalText(b.To, tz),
            scope = b.PhoneNumberId is { } n ? numbers.GetValueOrDefault(n, "(number)") : "All numbers",
        }));
    }

    private static MediaReplayer.Scope? ToScope(ReplayRequest req, TimeZoneInfo tz) =>
        Local(req.From, tz) is { } from && Local(req.To, tz) is { } to && to > from ? new(req.PhoneNumberId, from, to) : null;

    private static DateTimeOffset? Local(string? text, TimeZoneInfo tz)
    {
        if (string.IsNullOrWhiteSpace(text) || !DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)) return null;
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, tz.GetUtcOffset(local)).ToUniversalTime();
    }

    private static string LocalText(DateTimeOffset at, TimeZoneInfo tz) =>
        TimeZoneInfo.ConvertTime(at, tz).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);

    private static TimeZoneInfo Zone(TenantContext tenant)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(tenant.Current?.Timezone ?? "UTC"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
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

/// <summary>From / To are tenant-local "yyyy-MM-ddTHH:mm"; calls that STARTED in [From, To). No PhoneNumberId = all numbers.</summary>
public record ReplayRequest(Guid? PhoneNumberId, string? From, string? To, string? Reason = null);

public record AgencyRequest(string? Name, List<MediaAgencyField>? Fields, bool? IsActive);

public record AssignmentRequest(
    string? MarketType, Guid MediaAgencyId, string? Station, int? StationFacilityId, string? MediaType, string? AdType,
    DateOnly? StartDate, DateOnly? EndDate, bool? IsDefaultLocal, Dictionary<string, string>? FieldValues);
