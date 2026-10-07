using System.Globalization;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// Caller history for the agent on a call (S181): the caller's earlier calls, found by the number they're calling from
/// (ANI) — matched against past calls' caller number and the phone numbers the customer gave — so "what did I order last
/// time?" is answered without leaving the script. Production calls only, the same client only (a caller's history with
/// another of the tenant's clients isn't this agent's business). Detail reuses the records widget's read-only view.
/// </summary>
public static class CallerHistoryEndpoints
{
    public const int MaxCalls = 50;

    public static IEndpointRouteBuilder MapCallerHistoryEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/call-records/{id:guid}/caller-history").RequireAuthorization();
        g.MapGet("", List);
        g.MapGet("{pastId:guid}", Detail);
        return app;
    }

    internal static string? PhoneKey(string? phone) => Domain.ValueObjects.PhoneKey.Of(phone);

    private static async Task<(CallRecord Current, string Key, IQueryable<CallRecord> Past)?> QueryAsync(TenantDbContext db, Guid id, CancellationToken ct)
    {
        var current = await db.CallRecords.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (current is null || PhoneKey(current.CallerId) is not { } key) return null;
        var like = $"%{key}";
        var past = db.CallRecords.AsNoTracking().Where(r => r.Id != id && r.RunMode == CallRunMode.Production
            && (EF.Functions.Like(r.CallerId ?? "", like) || EF.Functions.Like(r.Phone ?? "", like) || EF.Functions.Like(r.BillingPhone ?? "", like)));
        if (current.ClientId != Guid.Empty)
        {
            var clientId = current.ClientId;
            past = past.Where(r => r.ClientId == clientId);
        }
        return (current, key, past);
    }

    private static async Task<IResult> List(Guid id, ScopedTenantDbContextFactory dbf, TenantContext tc, CancellationToken ct)
    {
        if (tc.Current is not { } tenant) return Results.Unauthorized();
        await using var db = dbf.Create();
        if (await QueryAsync(db, id, ct) is not { } q) return Results.Ok(new { number = (string?)null, total = 0, items = Array.Empty<object>() });

        var total = await q.Past.CountAsync(ct);
        var calls = await q.Past.Include(r => r.Interactions).OrderByDescending(r => r.CreatedAt).Take(MaxCalls).ToListAsync(ct);
        var campaignIds = calls.SelectMany(c => c.Interactions.Select(i => i.CampaignId).OfType<Guid>().Append(c.CampaignId)).Distinct().ToList();
        var campaigns = await db.Campaigns.AsNoTracking().Where(c => campaignIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var agentIds = calls.SelectMany(c => c.Interactions.Select(i => i.AgentId)).OfType<Guid>().Distinct().ToList();
        var agents = await db.Agents.AsNoTracking().Where(a => agentIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.FirstName + " " + a.LastName, ct);

        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(tenant.Timezone); } catch (Exception) { zone = TimeZoneInfo.Utc; }
        return Results.Ok(new
        {
            number = q.Key,
            total,
            items = calls.Select(c =>
            {
                var ix = c.Interactions.OrderBy(i => i.StartedAt).ToList();
                var ordered = ix.Where(i => i.OrderSubmittedAt != null).ToList();
                return new
                {
                    c.Id,
                    startedAt = TimeZoneInfo.ConvertTime(c.CallStartAt ?? c.CreatedAt, zone).ToString("yyyy-MM-dd h:mm tt", CultureInfo.InvariantCulture),
                    campaign = string.Join(" + ", ix.Select(i => i.CampaignId).OfType<Guid>().Append(c.CampaignId).Distinct()
                        .Select(cid => campaigns.GetValueOrDefault(cid)).Where(n => n is not null)),
                    agent = string.Join(" + ", ix.Select(i => i.AgentId).OfType<Guid>().Distinct().Select(a => agents.GetValueOrDefault(a)).Where(n => n is not null)),
                    disposition = c.CompoundDisposition,
                    orderNumbers = string.Join(", ", ordered.Select(i => i.OrderNumber).Where(n => !string.IsNullOrEmpty(n))),
                    orderTotal = ordered.Count == 0 ? (decimal?)null : ordered.Sum(i => i.TotalAmount ?? i.Cart?.CartTotal ?? 0m),
                    items = string.Join(", ", ordered.SelectMany(i => i.Cart?.Items ?? []).Select(it => it.Quantity > 1 ? $"{it.Quantity} × {it.Description}" : it.Description)),
                    customerName = string.Join(" ", new[] { c.FirstName, c.LastName }.Where(p => !string.IsNullOrWhiteSpace(p))),
                    durationSeconds = c.HandleTimeSeconds,
                    matchedOn = PhoneKey(c.CallerId) == q.Key ? "caller number" : "phone given on the call",
                };
            }),
        });
    }

    /// <summary>One past call's full read-only detail — only if it really is in this caller's history.</summary>
    private static async Task<IResult> Detail(Guid id, Guid pastId, ScopedTenantDbContextFactory dbf, TenantContext tc,
        [AsParameters] CallDetailView.Deps deps, HttpContext http, CancellationToken ct)
    {
        if (tc.Current is not { } tenant) return Results.Unauthorized();
        await using var db = dbf.Create();
        if (await QueryAsync(db, id, ct) is not { } q || !await q.Past.AnyAsync(r => r.Id == pastId, ct)) return Results.NotFound();
        var built = await CallDetailView.BuildAsync(pastId, deps, tenant.Timezone, ct);
        if (built is not { } b) return Results.NotFound();
        // Recordings of past calls: only roles allowed to play recordings, not every agent.
        return Results.Ok(new { detail = b.Detail, canPlayRecording = b.RecordingStatus == "available" && CallRecordingsEndpoints.CanPlay(http) });
    }
}
