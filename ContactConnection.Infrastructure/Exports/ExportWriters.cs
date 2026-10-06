using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using ContactConnection.Domain.ValueObjects.Exports;

namespace ContactConnection.Infrastructure.Exports;

/// <summary>Columns-mode output (S180): one header + one line per row, streamed.</summary>
public interface IExportRowWriter : IAsyncDisposable
{
    Task WriteHeaderAsync(IReadOnlyList<ExportColumn> columns);
    Task WriteRowAsync(IReadOnlyList<ExportColumn> columns, IReadOnlyList<string> values);
    Task CompleteAsync();
}

public static class ExportRowWriter
{
    public static readonly UTF8Encoding Utf8NoBom = new(false);

    public static IExportRowWriter For(ExportSpec spec, Stream output) => spec.Format switch
    {
        ExportFormat.Fixed => new FixedWidthWriter(output, spec),
        ExportFormat.Xlsx => new XlsxWriter(output),
        _ => new DelimitedWriter(output, spec),
    };

    public static string NewLine(ExportSpec spec) => spec.LineEnding == "lf" ? "\n" : "\r\n";
}

public sealed class DelimitedWriter(Stream output, ExportSpec spec) : IExportRowWriter
{
    private readonly StreamWriter _w = new(output, ExportRowWriter.Utf8NoBom, 64 * 1024, leaveOpen: true);
    private readonly string _delimiter = string.IsNullOrEmpty(spec.Delimiter) ? "," : spec.Delimiter;
    private readonly string _nl = ExportRowWriter.NewLine(spec);

    public Task WriteHeaderAsync(IReadOnlyList<ExportColumn> columns) =>
        Line(columns.Select(c => Field(c.Header, "auto")));

    public Task WriteRowAsync(IReadOnlyList<ExportColumn> columns, IReadOnlyList<string> values) =>
        Line(values.Select((v, i) => Field(v, columns[i].Quote)));

    private async Task Line(IEnumerable<string> fields)
    {
        await _w.WriteAsync(string.Join(_delimiter, fields));
        await _w.WriteAsync(_nl);
    }

    public string Field(string value, string quote) => quote switch
    {
        "always" => Quoted(value),
        "never" => value,
        _ => value.Contains(_delimiter) || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? Quoted(value) : value,
    };

    private static string Quoted(string v) => "\"" + v.Replace("\"", "\"\"") + "\"";

    public Task CompleteAsync() => _w.FlushAsync();
    public ValueTask DisposeAsync() => _w.DisposeAsync();
}

public sealed class FixedWidthWriter(Stream output, ExportSpec spec) : IExportRowWriter
{
    private readonly StreamWriter _w = new(output, ExportRowWriter.Utf8NoBom, 64 * 1024, leaveOpen: true);
    private readonly string _nl = ExportRowWriter.NewLine(spec);

    public Task WriteHeaderAsync(IReadOnlyList<ExportColumn> columns) =>
        Line(columns.Select(c => Cell(c, c.Header)));

    public Task WriteRowAsync(IReadOnlyList<ExportColumn> columns, IReadOnlyList<string> values) =>
        Line(values.Select((v, i) => Cell(columns[i], v)));

    public static string Cell(ExportColumn c, string value)
    {
        // Line breaks would break the record layout.
        value = value.Replace("\r", " ").Replace("\n", " ");
        var pad = string.IsNullOrEmpty(c.PadChar) ? ' ' : c.PadChar[0];
        return ExportTemplateEngine.Fit(value, c.Width ?? 0, pad, right: c.Align == "right");
    }

    private async Task Line(IEnumerable<string> cells)
    {
        await _w.WriteAsync(string.Concat(cells));
        await _w.WriteAsync(_nl);
    }

    public Task CompleteAsync() => _w.FlushAsync();
    public ValueTask DisposeAsync() => _w.DisposeAsync();
}

/// <summary>
/// A minimal single-sheet .xlsx (SpreadsheetML in a zip), streamed row by row — no library needed for a flat table.
/// Text cells are inline strings; a column typed <c>number</c> writes a numeric cell when the value parses.
/// </summary>
public sealed class XlsxWriter : IExportRowWriter
{
    private readonly ZipArchive _zip;
    private readonly StreamWriter _sheet;

    public XlsxWriter(Stream output)
    {
        _zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        Entry("[Content_Types].xml",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>""");
        Entry("_rels/.rels",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");
        Entry("xl/workbook.xml",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Export" sheetId="1" r:id="rId1"/></sheets></workbook>""");
        Entry("xl/_rels/workbook.xml.rels",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>""");
        _sheet = new StreamWriter(_zip.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Optimal).Open(), ExportRowWriter.Utf8NoBom);
        _sheet.Write("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>""");
    }

    private void Entry(string name, string xml)
    {
        using var w = new StreamWriter(_zip.CreateEntry(name, CompressionLevel.Optimal).Open(), ExportRowWriter.Utf8NoBom);
        w.Write(xml);
    }

    public Task WriteHeaderAsync(IReadOnlyList<ExportColumn> columns) =>
        Row(columns.Select(c => (c.Header, false)));

    public Task WriteRowAsync(IReadOnlyList<ExportColumn> columns, IReadOnlyList<string> values) =>
        Row(values.Select((v, i) => (v, columns[i].Type == "number")));

    private async Task Row(IEnumerable<(string Value, bool Number)> cells)
    {
        var sb = new StringBuilder("<row>");
        foreach (var (value, number) in cells)
        {
            if (number && decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
                sb.Append("<c><v>").Append(d.ToString(CultureInfo.InvariantCulture)).Append("</v></c>");
            else
                sb.Append("<c t=\"inlineStr\"><is><t xml:space=\"preserve\">").Append(Escape(value)).Append("</t></is></c>");
        }
        sb.Append("</row>");
        await _sheet.WriteAsync(sb.ToString());
    }

    /// <summary>XML-escaped, minus the control characters XML 1.0 can't carry.</summary>
    private static string Escape(string v) =>
        SecurityElement.Escape(new string(v.Where(ch => ch is '\t' or '\n' or '\r' || ch >= ' ').ToArray())) ?? "";

    public async Task CompleteAsync()
    {
        await _sheet.WriteAsync("</sheetData></worksheet>");
        await _sheet.FlushAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _sheet.DisposeAsync();
        _zip.Dispose();
    }
}
