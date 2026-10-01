using System.Globalization;
using System.IO.Compression;
using System.Text;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace ContactConnection.Infrastructure.Media;

/// <summary>
/// Rebuilds <c>public.broadcast_stations</c> from the FCC's public LMS database (S171, Media Agency
/// Phase B). The FCC publishes a dated daily dump of pipe-delimited tables at
/// https://enterpriseefiling.fcc.gov/dataentry/public/tv/lmsDatabase.html — three are enough:
/// <list type="bullet">
/// <item><c>facility</c> — call sign, service code, community served, active flag;</item>
/// <item><c>application_facility</c> — which applications belong to which facility;</item>
/// <item><c>app_location</c> — each application's transmitter coordinates (DMS, NAD83).</item>
/// </list>
/// A station's coordinates come from its most recent application that has a location (covers ~99% of
/// full-power TV and ~93% of radio; checked S171). Only active, licensed US stations are kept. The
/// table is replaced in one transaction (bulk COPY), so readers never see a partial list.
/// </summary>
public class FccStationImporter(IHttpClientFactory httpClientFactory, ContactConnectionDbContext db, ILogger<FccStationImporter> logger)
{
    private const string BaseUrl = "https://enterpriseefiling.fcc.gov/dataentry/api/download/dbfile";

    private static readonly HashSet<string> UsStates =
    [
        "AL","AK","AZ","AR","CA","CO","CT","DE","DC","FL","GA","HI","ID","IL","IN","IA","KS","KY","LA","ME","MD",
        "MA","MI","MN","MS","MO","MT","NE","NV","NH","NJ","NM","NY","NC","ND","OH","OK","OR","PA","RI","SC","SD",
        "TN","TX","UT","VT","VA","WA","WV","WI","WY","PR","VI","GU","AS","MP",
    ];

    /// <summary>Stations an ad can air on: full-power, Class A and low-power digital TV; AM, FM and
    /// low-power FM. Left out: translators, boosters, auxiliaries, the LMS's non-broadcast record types,
    /// and analog TV records (TV/LPA/LPT — full-power analog ended 2009, low-power analog 2021).</summary>
    public static readonly HashSet<string> BroadcastServices = ["DTV", "DCA", "DTS", "LPD", "FM", "AM", "FL"];
    public static readonly string[] TvServices = ["DTV", "DCA", "DTS", "LPD"];
    public static readonly string[] RadioServices = ["FM", "AM", "FL"];

    public record ImportResult(int Stations, string DumpDate);

    public async Task<ImportResult> ImportAsync(CancellationToken ct = default)
    {
        var work = Path.Combine(Path.GetTempPath(), "cc-fcc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var dumpDate = await DownloadAsync(work, ct);
            var stations = Build(work);
            await ReplaceAsync(stations, ct);
            logger.LogInformation("FCC station import: {Count} stations from the {Date} LMS dump", stations.Count, dumpDate);
            return new ImportResult(stations.Count, dumpDate);
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch { /* temp cleanup is best-effort */ }
        }
    }

    // ── Download ──────────────────────────────────────────────────────────────

    /// <summary>Today's dump folder (Eastern date), else yesterday's — the FCC posts it during the day.</summary>
    private async Task<string> DownloadAsync(string work, CancellationToken ct)
    {
        var http = httpClientFactory.CreateClient("Fcc");
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var today = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, eastern).Date;
        foreach (var day in new[] { today, today.AddDays(-1), today.AddDays(-2) })
        {
            var folder = day.ToString("MM-dd-yyyy", CultureInfo.InvariantCulture);
            var ok = true;
            foreach (var table in new[] { "facility", "application_facility", "app_location" })
            {
                using var response = await http.GetAsync($"{BaseUrl}/{folder}/{table}.zip", HttpCompletionOption.ResponseHeadersRead, ct);
                if (!response.IsSuccessStatusCode) { ok = false; break; }
                await using var file = File.Create(Path.Combine(work, table + ".zip"));
                await response.Content.CopyToAsync(file, ct);
            }
            if (ok) return folder;
        }
        throw new InvalidOperationException("No FCC LMS dump found for the last three days.");
    }

    // ── Parse + join ──────────────────────────────────────────────────────────

    internal static List<BroadcastStation> Build(string work)
    {
        // application id → coordinates
        var locations = new Dictionary<string, (double Lat, double Lon)>();
        foreach (var r in Rows(Path.Combine(work, "app_location.zip")))
        {
            if (r.Get("aloc_active_ind") != "Y") continue;
            if (Dms(r.Get("aloc_lat_deg"), r.Get("aloc_lat_mm"), r.Get("aloc_lat_ss"), r.Get("aloc_lat_dir"), "S") is not { } lat) continue;
            if (Dms(r.Get("aloc_long_deg"), r.Get("aloc_long_mm"), r.Get("aloc_long_ss"), r.Get("aloc_long_dir"), "W") is not { } lon) continue;
            locations.TryAdd(r.Get("aloc_aapp_application_id"), (lat, lon));
        }

        // facility id → its most recent application that has a location
        var best = new Dictionary<string, (string CreateTs, string AppId)>();
        foreach (var r in Rows(Path.Combine(work, "application_facility.zip")))
        {
            var app = r.Get("afac_application_id");
            if (!locations.ContainsKey(app)) continue;
            var facility = r.Get("afac_facility_id");
            var created = r.Get("create_ts");
            if (!best.TryGetValue(facility, out var current) || string.CompareOrdinal(created, current.CreateTs) > 0)
                best[facility] = (created, app);
        }

        var now = DateTimeOffset.UtcNow;
        var stations = new Dictionary<int, BroadcastStation>();
        foreach (var r in Rows(Path.Combine(work, "facility.zip")))
        {
            if (r.Get("active_ind") != "Y") continue;
            var callSign = r.Get("callsign").Trim();
            if (callSign.Length == 0 || callSign == "NEW") continue;
            if (!BroadcastServices.Contains(r.Get("service_code").Trim())) continue;
            var state = r.Get("community_served_state").Trim().ToUpperInvariant();
            if (!UsStates.Contains(state)) continue;
            if (!int.TryParse(r.Get("facility_id"), out var facilityId)) continue;
            if (!best.TryGetValue(r.Get("facility_id"), out var app)) continue;
            var (lat, lon) = locations[app.AppId];
            stations[facilityId] = new BroadcastStation
            {
                FacilityId = facilityId,
                CallSign = Clip(callSign, 20)!,
                ServiceCode = Clip(r.Get("service_code").Trim(), 10) ?? "",
                CommunityCity = Clip(Title(r.Get("community_served_city")), 100),
                CommunityState = state,
                Latitude = Math.Round(lat, 6),
                Longitude = Math.Round(lon, 6),
                NetworkAffiliation = Clip(r.Get("network_affiliation").Trim(), 100),
                ImportedAt = now,
            };
        }
        return [.. stations.Values];
    }

    internal static double? Dms(string deg, string min, string sec, string dir, string negativeDir)
    {
        if (!int.TryParse(deg, out var d)) return null;
        _ = int.TryParse(min, out var m);
        _ = double.TryParse(sec, NumberStyles.Float, CultureInfo.InvariantCulture, out var s);
        var value = d + m / 60.0 + s / 3600.0;
        return dir == negativeDir ? -value : value;
    }

    private static string Title(string s) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.Trim().ToLowerInvariant());

    private static string? Clip(string? s, int max) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Length <= max ? s : s[..max];

    /// <summary>Streams the rows of the single pipe-delimited .dat file inside an LMS table zip.
    /// The header line names the columns (with a trailing "^|" end-of-record marker).</summary>
    internal static IEnumerable<Row> Rows(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.Entries.First(e => e.Name.EndsWith(".dat", StringComparison.OrdinalIgnoreCase));
        using var reader = new StreamReader(entry.Open(), Encoding.Latin1);
        var header = (reader.ReadLine() ?? "").TrimEnd('^', '|').Split('|');
        var index = header.Select((name, i) => (name, i)).ToDictionary(x => x.name, x => x.i);
        string? line;
        while ((line = reader.ReadLine()) is not null)
            yield return new Row(line.Split('|'), index);
    }

    internal readonly record struct Row(string[] Values, Dictionary<string, int> Index)
    {
        public string Get(string column) =>
            Index.TryGetValue(column, out var i) && i < Values.Length ? Values[i] : "";
    }

    // ── Replace ───────────────────────────────────────────────────────────────

    private async Task ReplaceAsync(List<BroadcastStation> stations, CancellationToken ct)
    {
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await using (var delete = new NpgsqlCommand("DELETE FROM public.broadcast_stations", conn, tx))
            await delete.ExecuteNonQueryAsync(ct);

        await using (var import = await conn.BeginBinaryImportAsync(
            "COPY public.broadcast_stations (facility_id, call_sign, service_code, community_city, community_state, " +
            "latitude, longitude, network_affiliation, imported_at) FROM STDIN (FORMAT BINARY)", ct))
        {
            foreach (var s in stations)
            {
                await import.StartRowAsync(ct);
                await import.WriteAsync(s.FacilityId, NpgsqlDbType.Integer, ct);
                await import.WriteAsync(s.CallSign, NpgsqlDbType.Varchar, ct);
                await import.WriteAsync(s.ServiceCode, NpgsqlDbType.Varchar, ct);
                await WriteNullable(import, s.CommunityCity, ct);
                await WriteNullable(import, s.CommunityState, ct);
                await import.WriteAsync(s.Latitude, NpgsqlDbType.Double, ct);
                await import.WriteAsync(s.Longitude, NpgsqlDbType.Double, ct);
                await WriteNullable(import, s.NetworkAffiliation, ct);
                await import.WriteAsync(s.ImportedAt, NpgsqlDbType.TimestampTz, ct);
            }
            await import.CompleteAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    private static Task WriteNullable(NpgsqlBinaryImporter import, string? value, CancellationToken ct) =>
        value is null ? import.WriteNullAsync(ct) : import.WriteAsync(value, NpgsqlDbType.Varchar, ct);
}
