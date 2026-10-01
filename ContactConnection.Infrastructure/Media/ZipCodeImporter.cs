using System.Globalization;
using System.IO.Compression;
using System.Text;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace ContactConnection.Infrastructure.Media;

/// <summary>
/// Loads the zip-codes.com ZIP Code Database (Standard edition, CSV) into <c>public.zip_codes</c> and
/// derives <c>public.area_codes</c> from it (S171, Media Agency Phase B). Built against the published
/// sample, which keeps the full file's columns, quoting and encoding. Accepts the .csv itself or the
/// download .zip (uses its .csv file). Only each ZIP's primary record (PrimaryRecord = "P") is kept —
/// the others are city alias rows with the same location. Both tables are replaced in one transaction.
/// </summary>
public class ZipCodeImporter(ContactConnectionDbContext db)
{
    public record ImportResult(int Zips, int AreaCodes);

    public async Task<ImportResult> ImportAsync(Stream upload, string fileName, CancellationToken ct = default)
    {
        List<ZipCodeLocation> zips;
        if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            // ZipArchive needs a seekable stream; uploads aren't.
            using var buffer = new MemoryStream();
            await upload.CopyToAsync(buffer, ct);
            buffer.Position = 0;
            using var archive = new ZipArchive(buffer, ZipArchiveMode.Read);
            var entry = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException("The .zip has no .csv file in it.");
            using var csv = entry.Open();
            zips = Parse(csv);
        }
        else zips = Parse(upload);

        if (zips.Count == 0) throw new InvalidDataException("No ZIP Code rows found — is this the zip-codes.com Standard CSV?");
        var areaCodes = AreaCodeCenters(zips);
        await ReplaceAsync(zips, areaCodes, ct);
        return new ImportResult(zips.Count, areaCodes.Count);
    }

    internal static List<ZipCodeLocation> Parse(Stream csv)
    {
        using var reader = new StreamReader(csv, Encoding.Latin1);
        var header = CsvFields(reader.ReadLine() ?? "");
        var ix = header.Select((name, i) => (name, i)).ToDictionary(x => x.name, x => x.i, StringComparer.OrdinalIgnoreCase);
        foreach (var required in new[] { "ZipCode", "Latitude", "Longitude", "AreaCode", "PrimaryRecord" })
            if (!ix.ContainsKey(required)) throw new InvalidDataException($"Missing column '{required}' — is this the zip-codes.com Standard CSV?");

        string Get(List<string> row, string column) =>
            ix.TryGetValue(column, out var i) && i < row.Count ? row[i].Trim() : "";

        var now = DateTimeOffset.UtcNow;
        var byZip = new Dictionary<string, ZipCodeLocation>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var row = CsvFields(line);
            if (Get(row, "PrimaryRecord") != "P") continue;
            var zip = Get(row, "ZipCode");
            if (zip.Length != 5) continue;
            if (!double.TryParse(Get(row, "Latitude"), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)) continue;
            if (!double.TryParse(Get(row, "Longitude"), NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)) continue;
            if (lat == 0 && lon == 0) continue;
            byZip[zip] = new ZipCodeLocation
            {
                Zip = zip,
                City = First(Get(row, "CityMixedCase"), Get(row, "City")),
                State = First(Get(row, "State")),
                County = First(Get(row, "CountyMixedCase"), Get(row, "County")),
                Latitude = lat,
                Longitude = lon,
                AreaCodes = Get(row, "AreaCode").Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(a => a.Length == 3 && a.All(char.IsDigit)).Distinct().ToArray(),
                ImportedAt = now,
            };
        }
        return [.. byZip.Values];
    }

    /// <summary>Each area code's center: the mean location of the ZIPs it serves.</summary>
    internal static List<AreaCodeLocation> AreaCodeCenters(IEnumerable<ZipCodeLocation> zips) =>
        zips.SelectMany(z => z.AreaCodes.Select(a => (AreaCode: a, z.Latitude, z.Longitude, z.ImportedAt)))
            .GroupBy(x => x.AreaCode)
            .Select(g => new AreaCodeLocation
            {
                AreaCode = g.Key,
                Latitude = Math.Round(g.Average(x => x.Latitude), 6),
                Longitude = Math.Round(g.Average(x => x.Longitude), 6),
                ZipCount = g.Count(),
                ImportedAt = g.First().ImportedAt,
            })
            .ToList();

    /// <summary>Splits one CSV line: comma-separated, fields optionally double-quoted, "" = a literal quote.</summary>
    internal static List<string> CsvFields(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else sb.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        fields.Add(sb.ToString());
        return fields;
    }

    private static string? First(params string[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private async Task ReplaceAsync(List<ZipCodeLocation> zips, List<AreaCodeLocation> areaCodes, CancellationToken ct)
    {
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using (var delete = new NpgsqlCommand("DELETE FROM public.zip_codes; DELETE FROM public.area_codes;", conn, tx))
            await delete.ExecuteNonQueryAsync(ct);

        await using (var import = await conn.BeginBinaryImportAsync(
            "COPY public.zip_codes (zip, city, state, county, latitude, longitude, area_codes, imported_at) FROM STDIN (FORMAT BINARY)", ct))
        {
            foreach (var z in zips)
            {
                await import.StartRowAsync(ct);
                await import.WriteAsync(z.Zip, NpgsqlDbType.Varchar, ct);
                await WriteNullable(import, z.City, ct);
                await WriteNullable(import, z.State, ct);
                await WriteNullable(import, z.County, ct);
                await import.WriteAsync(z.Latitude, NpgsqlDbType.Double, ct);
                await import.WriteAsync(z.Longitude, NpgsqlDbType.Double, ct);
                await import.WriteAsync(z.AreaCodes, NpgsqlDbType.Array | NpgsqlDbType.Text, ct);
                await import.WriteAsync(z.ImportedAt, NpgsqlDbType.TimestampTz, ct);
            }
            await import.CompleteAsync(ct);
        }

        await using (var import = await conn.BeginBinaryImportAsync(
            "COPY public.area_codes (area_code, latitude, longitude, zip_count, imported_at) FROM STDIN (FORMAT BINARY)", ct))
        {
            foreach (var a in areaCodes)
            {
                await import.StartRowAsync(ct);
                await import.WriteAsync(a.AreaCode, NpgsqlDbType.Varchar, ct);
                await import.WriteAsync(a.Latitude, NpgsqlDbType.Double, ct);
                await import.WriteAsync(a.Longitude, NpgsqlDbType.Double, ct);
                await import.WriteAsync(a.ZipCount, NpgsqlDbType.Integer, ct);
                await import.WriteAsync(a.ImportedAt, NpgsqlDbType.TimestampTz, ct);
            }
            await import.CompleteAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    private static Task WriteNullable(NpgsqlBinaryImporter import, string? value, CancellationToken ct) =>
        value is null ? import.WriteNullAsync(ct) : import.WriteAsync(value, NpgsqlDbType.Varchar, ct);
}
