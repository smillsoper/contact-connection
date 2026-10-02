using System.Text.Json;
using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Media;

/// <summary>
/// "Replay media attribution" (S171): re-resolves past calls' media attribution exactly as a live call
/// would have been attributed — the assignments in effect on the call's date (tenant-local), and the
/// caller's location (the zip the call was placed by, else an address zip on the call, else the caller's
/// area code). For assignments that arrived late, a station corrected after the fact, or a Local default
/// changed retroactively. Preview is read-only; apply writes in chunks and records the previous
/// attribution on each changed call's history. Idempotent: a re-run only rewrites what still differs.
/// </summary>
public static class MediaReplayer
{
    private const int ChunkSize = 250;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public record Scope(Guid? PhoneNumberId, DateTimeOffset From, DateTimeOffset To);

    public record Change(string From, string To, int Calls);

    public record Preview(int Calls, int ChangedCalls, List<Change> Changes);

    /// <summary>Assignments, agency names and numbers for the scope, loaded once per run.</summary>
    private sealed class Book
    {
        public required Dictionary<string, (Guid Id, string? ClientNumber)> NumbersByDid { get; init; }
        public required ILookup<Guid, MediaAssignment> AssignmentsByNumber { get; init; }
        public required Dictionary<Guid, string> AgencyNames { get; init; }
        public Dictionary<string, CallerLocation?> Locations { get; } = [];
    }

    private static async Task<Book> LoadAsync(TenantDbContext db, Scope s, CancellationToken ct)
    {
        var numbers = await db.PhoneNumbers.AsNoTracking()
            .Where(p => s.PhoneNumberId == null || p.Id == s.PhoneNumberId)
            .Select(p => new { p.Id, p.Number, p.ClientNumber }).ToListAsync(ct);
        var ids = numbers.Select(n => n.Id).ToList();
        return new Book
        {
            NumbersByDid = numbers.GroupBy(n => n.Number).ToDictionary(g => g.Key, g => (g.First().Id, g.First().ClientNumber)),
            AssignmentsByNumber = (await db.MediaAssignments.AsNoTracking().Where(a => ids.Contains(a.PhoneNumberId)).ToListAsync(ct))
                .ToLookup(a => a.PhoneNumberId),
            AgencyNames = await db.MediaAgencies.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Name, ct),
        };
    }

    private static IQueryable<CallRecord> Calls(TenantDbContext db, Book book, Scope s)
    {
        var dids = book.NumbersByDid.Keys.ToList();
        return db.CallRecords
            .Where(r => r.CreatedAt >= s.From && r.CreatedAt < s.To && r.Dnis != null && dids.Contains(r.Dnis))
            .OrderBy(r => r.CreatedAt).ThenBy(r => r.Id);
    }

    /// <summary>What the call would be attributed to now — the live resolution rules, replayed.</summary>
    private static async Task<MediaAttribution?> ResolveAsync(
        ContactConnectionDbContext platformDb, Book book, CallRecord r, TimeZoneInfo tz, CancellationToken ct)
    {
        if (r.Dnis is null || !book.NumbersByDid.TryGetValue(r.Dnis, out var number)) return r.MediaAttribution;

        var current = r.MediaAttribution;
        var zip = current?.LocationSource == CallerLocation.FromZip ? current.LocationKey
                : r.Addresses?.Billing?.Zip ?? r.Addresses?.Shipping?.Zip;
        var key = $"{CallerLocator.Zip5(zip)}|{CallerLocator.AreaCode(r.CallerId)}";
        if (!book.Locations.TryGetValue(key, out var caller))
            book.Locations[key] = caller = await CallerLocator.LocateAsync(platformDb, zip, r.CallerId, ct);

        var callDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(r.CreatedAt, tz).DateTime);
        var chosen = MediaAssignmentRules.Resolve(book.AssignmentsByNumber[number.Id], callDate, caller?.Point);
        return chosen is null ? null
            : MediaAttribution.From(chosen, book.AgencyNames.GetValueOrDefault(chosen.MediaAgencyId, ""), number.ClientNumber ?? r.Dnis, caller);
    }

    private static bool Same(MediaAttribution? a, MediaAttribution? b) =>
        JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(b, Json);

    private static string Label(MediaAttribution? m) => m is null ? "(no attribution)" : $"{m.Agency} · {m.Station}";

    public static async Task<Preview> PreviewAsync(
        TenantDbContext db, ContactConnectionDbContext platformDb, Scope scope, TimeZoneInfo tz, CancellationToken ct)
    {
        var book = await LoadAsync(db, scope, ct);
        var changes = new Dictionary<(string, string), int>();
        int calls = 0, changed = 0;
        for (var skip = 0; ; skip += ChunkSize)
        {
            var chunk = await Calls(db, book, scope).AsNoTracking().Skip(skip).Take(ChunkSize).ToListAsync(ct);
            if (chunk.Count == 0) break;
            foreach (var r in chunk)
            {
                calls++;
                var replayed = await ResolveAsync(platformDb, book, r, tz, ct);
                if (Same(r.MediaAttribution, replayed)) continue;
                changed++;
                var k = (Label(r.MediaAttribution), Label(replayed));
                changes[k] = changes.GetValueOrDefault(k) + 1;
            }
        }
        return new Preview(calls, changed,
            changes.Select(c => new Change(c.Key.Item1, c.Key.Item2, c.Value)).OrderByDescending(c => c.Calls).Take(50).ToList());
    }

    public static async Task RunAsync(
        TenantDbContext db, ContactConnectionDbContext platformDb, MediaReplayBatch batch, TimeZoneInfo tz, CancellationToken ct)
    {
        var scope = new Scope(batch.PhoneNumberId, batch.From, batch.To);
        var book = await LoadAsync(db, scope, ct);
        batch.Start(await Calls(db, book, scope).CountAsync(ct));
        await db.SaveChangesAsync(ct);

        for (var skip = 0; ; skip += ChunkSize)
        {
            var chunk = await Calls(db, book, scope).Skip(skip).Take(ChunkSize).ToListAsync(ct);
            if (chunk.Count == 0) break;
            var changed = 0;
            foreach (var r in chunk)
            {
                var replayed = await ResolveAsync(platformDb, book, r, tz, ct);
                if (Same(r.MediaAttribution, replayed)) continue;
                var before = r.MediaAttribution;
                r.SetMediaAttribution(replayed);
                db.CallRecordAuditEntries.Add(CallRecordAuditEntry.Create(
                    r.Id, CallAuditAction.MediaReattributed,
                    $"Media attribution replayed: {Label(before)} → {Label(replayed)} ({batch.Reason})",
                    JsonSerializer.Serialize(new { batchId = batch.Id, batch.Reason, before, after = replayed }, Json),
                    batch.RequestedById, batch.RequestedBy ?? "Media replay"));
                changed++;
            }
            batch.Progress(chunk.Count, changed);
            await db.SaveChangesAsync(ct);
            foreach (var e in db.ChangeTracker.Entries().Where(e => e.Entity is not MediaReplayBatch).ToList())
                e.State = EntityState.Detached;
        }
        batch.Complete();
        await db.SaveChangesAsync(ct);
    }
}
