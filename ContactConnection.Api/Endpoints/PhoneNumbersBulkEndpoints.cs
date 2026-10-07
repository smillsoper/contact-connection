using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Telephony;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// Bulk telephony management (S182): every number in the account in one list, paste-to-add, bulk move / Reserve / activate /
/// deactivate / label, and assign-from-Reserve oldest first. Each number moves through <see cref="NumberOwnership"/> so the
/// platform routing table stays true and another account's live number can never be taken.
/// </summary>
public static class PhoneNumbersBulkEndpoints
{
    public static IEndpointRouteBuilder MapPhoneNumbersBulkEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/phone-numbers").RequireAuthorization("TenantAdmin");
        group.MapGet("all",                  GetAll);
        group.MapPost("bulk-add",            BulkAdd);
        group.MapPost("bulk",                BulkAction);
        group.MapPost("assign-from-reserve", AssignFromReserve);
        return app;
    }

    /// <summary>active · inactive (held on its campaign) · reserve · released (no longer the account's; kept for history).</summary>
    internal static string Status(PhoneNumber pn) =>
        pn.IsReleased ? "released" : pn.InReserve ? "reserve" : pn.IsActive ? "active" : "inactive";

    // ── GET /api/v1/phone-numbers/all ───────────────────────────────────────

    private static async Task<IResult> GetAll(ScopedTenantDbContextFactory dbf, TenantContext ctx, CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();
        await using var db = dbf.Create();
        var campaigns = await db.Campaigns.AsNoTracking()
            .Select(c => new { c.Id, c.Name, c.ClientId, c.Status }).ToDictionaryAsync(c => c.Id, ct);
        var clients = await db.Clients.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var providers = await db.NumberProviders.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var numbers = await db.PhoneNumbers.AsNoTracking().ToListAsync(ct);

        // Reserve is shown in the order it is handed out: longest-waiting first.
        var reserveRank = numbers.Where(n => n.InReserve && n.IsActive).OrderBy(n => n.ReservedAt).ThenBy(n => n.Number)
            .Select((n, i) => (n.Id, Rank: i + 1)).ToDictionary(x => x.Id, x => x.Rank);

        return Results.Ok(new
        {
            campaigns = campaigns.Values.OrderBy(c => c.Name)
                .Select(c => new { c.Id, c.Name, c.ClientId, clientName = clients.GetValueOrDefault(c.ClientId), c.Status }),
            numbers = numbers.OrderBy(n => n.Number).Select(n =>
            {
                var campaign = n.CampaignId is { } cid ? campaigns.GetValueOrDefault(cid) : null;
                return new
                {
                    n.Id, n.Number, n.Label, n.IsActive, n.CampaignId,
                    campaignName = campaign?.Name,
                    clientId = campaign?.ClientId,
                    clientName = campaign is null ? null : clients.GetValueOrDefault(campaign.ClientId),
                    status = Status(n),
                    n.ReservedAt,
                    reserveRank = reserveRank.TryGetValue(n.Id, out var r) ? r : (int?)null,
                    n.ProviderId,
                    providerName = n.ProviderId is { } pid ? providers.GetValueOrDefault(pid) : null,
                    n.Role, n.ClientNumber, n.FlowId, n.TelephonyFlowId, n.UpdatedAt,
                };
            }),
        });
    }

    // ── POST /api/v1/phone-numbers/bulk-add ─────────────────────────────────

    private static async Task<IResult> BulkAdd(
        BulkAddNumbersRequest req, ScopedTenantDbContextFactory dbf, NumberOwnership ownership,
        TenantContext ctx, CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();
        var tenantId = ctx.Current!.Id;
        await using var db = dbf.Create();

        if (req.CampaignId is { } cid && !await db.Campaigns.AnyAsync(c => c.Id == cid, ct))
            return Results.NotFound(new { error = "Campaign not found." });
        if (req.ProviderId is { } pid && !await db.NumberProviders.AnyAsync(p => p.Id == pid, ct))
            return Results.BadRequest(new { error = "Unknown number provider." });

        var existing = await db.PhoneNumbers.ToListAsync(ct);
        var results = new List<BulkNumberResult>();
        var seen = new HashSet<string>();
        var changed = new List<PhoneNumber>();

        foreach (var raw in SplitNumbers(req.Numbers))
        {
            var number = PhoneNumber.Normalize(raw);
            var digits = number.Count(char.IsDigit);
            if (digits is < 10 or > 15) { results.Add(new(raw, number, "invalid", "Not a phone number.")); continue; }
            if (!seen.Add(number))      { results.Add(new(raw, number, "duplicate", "Listed twice.")); continue; }

            var forms = PhoneNumber.Forms(number);
            var mine = existing.FirstOrDefault(p => forms.Contains(p.Number));
            if (mine is not null && !mine.IsReleased)
            {
                results.Add(new(raw, number, "duplicate", $"Already in this account ({Status(mine)}) — move it instead."));
                continue;
            }
            if (await ownership.ConflictAsync(tenantId, number, releasedHere: false, ct) is { } inUse)
            {
                results.Add(new(raw, number, "conflict", inUse));
                continue;
            }

            if (mine is not null)
            {
                // Released earlier by this account — take it back (its history row is reused, never duplicated).
                if (req.CampaignId is { } c) mine.Reassign(c);
                mine.Activate();
                if (req.Label is not null) mine.UpdateLabel(req.Label);
                changed.Add(mine);
                results.Add(new(raw, mine.Number, "reacquired", null));
                continue;
            }

            var pn = PhoneNumber.Create(tenantId, req.CampaignId, number, req.Label);
            if (req.ProviderId is not null || req.Role is not null)
            {
                try { pn.SetProvider(req.ProviderId, req.Role ?? PhoneNumberRole.Hosted, null); }
                catch (ArgumentException ex) { results.Add(new(raw, number, "invalid", ex.Message)); continue; }
            }
            db.PhoneNumbers.Add(pn);
            changed.Add(pn);
            results.Add(new(raw, number, "added", null));
        }

        await db.SaveChangesAsync(ct);
        foreach (var pn in changed) await ownership.ApplyAsync(pn, ct);
        await ownership.SaveAsync(ct);
        return Results.Ok(new { results, added = results.Count(r => r.Outcome is "added" or "reacquired") });
    }

    internal static IEnumerable<string> SplitNumbers(IEnumerable<string> lines) =>
        lines.SelectMany(l => l.Split(['\n', '\r', ',', ';', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
             .Where(s => s.Length > 0);

    // ── POST /api/v1/phone-numbers/bulk ─────────────────────────────────────

    private static async Task<IResult> BulkAction(
        BulkNumberActionRequest req, ScopedTenantDbContextFactory dbf, NumberOwnership ownership,
        TenantContext ctx, CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();
        if (req.Ids.Count == 0) return Results.BadRequest(new { error = "Choose at least one number." });
        await using var db = dbf.Create();

        if (req.Action == "move")
        {
            if (req.CampaignId is not { } target) return Results.BadRequest(new { error = "Choose the campaign to move them to." });
            if (!await db.Campaigns.AnyAsync(c => c.Id == target, ct)) return Results.NotFound(new { error = "Campaign not found." });
        }
        else if (req.Action is not ("reserve" or "activate" or "deactivate" or "label" or "delete"))
            return Results.BadRequest(new { error = $"Unknown action '{req.Action}'." });

        var numbers = await db.PhoneNumbers.Where(p => req.Ids.Contains(p.Id)).ToListAsync(ct);
        var results = new List<BulkNumberResult>();
        var changed = new List<PhoneNumber>();
        var deleted = new List<PhoneNumber>();
        foreach (var pn in numbers.OrderBy(n => n.Number))
        {
            if (req.Action == "delete")
            {
                // Only a number that never took a call (a typo, a number never pointed at us) is deleted outright;
                // anything with history is released instead so its audit trail stays.
                var forms = PhoneNumber.Forms(pn.Number);
                if (await db.CallRecords.AnyAsync(r => r.Dnis != null && forms.Contains(r.Dnis), ct)
                    || await db.MediaAssignments.AnyAsync(a => a.PhoneNumberId == pn.Id, ct))
                {
                    results.Add(new(pn.Number, pn.Number, "kept", "Has call or media history — release it instead (Move to Reserve, then Deactivate)."));
                    continue;
                }
                db.PhoneNumbers.Remove(pn);
                deleted.Add(pn);
                results.Add(new(pn.Number, pn.Number, "done", "deleted"));
                continue;
            }
            // Bringing a released number back (move / activate) needs it to be free again.
            if (pn.IsReleased && req.Action is "move" or "activate"
                && await ownership.ConflictAsync(pn.TenantId, pn.Number, releasedHere: false, ct) is { } inUse)
            {
                results.Add(new(pn.Number, pn.Number, "conflict", inUse));
                continue;
            }
            switch (req.Action)
            {
                case "move":
                    if (pn.IsReleased) pn.Activate();
                    pn.Reassign(req.CampaignId!.Value); break;
                case "reserve":    pn.MoveToReserve(); break;
                case "activate":   pn.Activate(); break;
                case "deactivate": pn.Deactivate(); break;
                case "label":      pn.UpdateLabel(string.IsNullOrWhiteSpace(req.Label) ? null : req.Label); break;
            }
            changed.Add(pn);
            results.Add(new(pn.Number, pn.Number, "done", Status(pn)));
        }
        foreach (var missing in req.Ids.Except(numbers.Select(n => n.Id)))
            results.Add(new(missing.ToString(), null, "invalid", "Not found."));

        await db.SaveChangesAsync(ct);
        foreach (var pn in changed) await ownership.ApplyAsync(pn, ct);
        foreach (var pn in deleted) await ownership.ForgetAsync(pn, ct);
        await ownership.SaveAsync(ct);
        return Results.Ok(new { results, done = changed.Count + deleted.Count });
    }

    // ── POST /api/v1/phone-numbers/assign-from-reserve ──────────────────────

    private static async Task<IResult> AssignFromReserve(
        AssignFromReserveRequest req, ScopedTenantDbContextFactory dbf, NumberOwnership ownership,
        TenantContext ctx, CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();
        if (req.Count < 1) return Results.BadRequest(new { error = "How many numbers?" });
        await using var db = dbf.Create();
        if (!await db.Campaigns.AnyAsync(c => c.Id == req.CampaignId, ct)) return Results.NotFound(new { error = "Campaign not found." });

        // Longest-waiting first, so drag calls from a recently retired campaign die down before a number is reused.
        var picked = await db.PhoneNumbers
            .Where(p => p.CampaignId == null && p.IsActive)
            .OrderBy(p => p.ReservedAt).ThenBy(p => p.Number)
            .Take(req.Count)
            .ToListAsync(ct);
        foreach (var pn in picked)
        {
            pn.Reassign(req.CampaignId);
            if (req.Label is not null) pn.UpdateLabel(req.Label);
        }
        await db.SaveChangesAsync(ct);
        foreach (var pn in picked) await ownership.ApplyAsync(pn, ct);
        await ownership.SaveAsync(ct);

        return Results.Ok(new
        {
            assigned = picked.Select(p => p.Number),
            shortBy = Math.Max(0, req.Count - picked.Count),
        });
    }
}

public record BulkAddNumbersRequest(List<string> Numbers, Guid? CampaignId = null, string? Label = null,
    Guid? ProviderId = null, string? Role = null);
public record BulkNumberActionRequest(List<Guid> Ids, string Action, Guid? CampaignId = null, string? Label = null);
public record AssignFromReserveRequest(Guid CampaignId, int Count, string? Label = null);
public record BulkNumberResult(string Input, string? Number, string Outcome, string? Detail);
