using System.IO.Compression;
using System.Text;
using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects.Commerce;
using ContactConnection.Domain.ValueObjects.Exports;
using ContactConnection.Infrastructure.ApiExecution;
using ContactConnection.Infrastructure.Exports;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Exports;

public class ExportEngineTests
{
    private static readonly TimeZoneInfo Eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    private static readonly ExportTemplateEngine Engine = new(Eastern);

    private static Task<string> R(string t, Dictionary<string, object?>? m = null) =>
        Engine.RenderAsync(t, m ?? new Dictionary<string, object?>()).AsTask();

    [Fact]
    public async Task FormatTime_WritesUtcInTheExportZone_AcrossDst()
    {
        var m = new Dictionary<string, object?> { ["summer"] = "2026-07-01T01:30:00Z", ["winter"] = "2026-12-01T01:30:00Z" };
        Assert.Equal("06/30/2026 21:30", await R("{{ summer | format_time: 'MM/dd/yyyy HH:mm' }}", m));
        Assert.Equal("11/30/2026 20:30", await R("{{ winter | format_time: 'MM/dd/yyyy HH:mm' }}", m));
        Assert.Equal("18:30", await R("{{ summer | format_time: 'HH:mm', 'America/Los_Angeles' }}", m));
        Assert.Equal("", await R("{{ missing | format_time: 'HH:mm' }}"));
    }

    [Fact]
    public async Task PadFilters_AreExactWidth()
    {
        Assert.Equal("AB  ", await R("{{ 'AB' | pad_right: 4 }}"));
        Assert.Equal("ABCD", await R("{{ 'ABCDEF' | pad_right: 4 }}"));
        Assert.Equal("000042", await R("{{ 42 | pad_left: 6, '0' }}"));
    }

    [Fact]
    public async Task Digits_Number_Csv_Xml_Money()
    {
        Assert.Equal("3035550100", await R("{{ '(303) 555-0100' | digits }}"));
        Assert.Equal("005883", await R("{{ 58.83 | times: 100 | number: '000000' }}"));
        Assert.Equal("\"Jo \"\"JJ\"\"\"", await R("{{ 'Jo \"JJ\"' | csv }}"));
        Assert.Equal("a &amp; b", await R("{{ 'a & b' | xml }}"));
        Assert.Equal("49.90", await R("{{ 49.9 | money }}"));
    }

    [Fact]
    public async Task Condition_FiltersRows()
    {
        var m = new Dictionary<string, object?> { ["call"] = new Dictionary<string, object?> { ["disposition"] = "Junk" } };
        Assert.False(await Engine.TestAsync("call.disposition != \"Junk\"", m));
        Assert.True(await Engine.TestAsync("call.disposition == \"Junk\"", m));
        Assert.NotNull(ExportTemplateEngine.Validate(ExportTemplateEngine.ConditionTemplate("call.x ==")));
    }

    [Fact]
    public void NormalizeLines_DropsBlankLines_AndUsesTheChosenEnding()
    {
        Assert.Equal("A\r\nB\r\n", ExportTemplateEngine.NormalizeLines("A\n   \nB\n", "crlf", skipBlankLines: true));
        Assert.Equal("A\n\nB\n", ExportTemplateEngine.NormalizeLines("A\r\n\r\nB\r\n", "lf", skipBlankLines: false));
    }

    [Fact]
    public async Task DelimitedWriter_QuotesByColumnSetting()
    {
        var spec = new ExportSpec { Delimiter = ",", LineEnding = "lf" };
        var cols = new List<ExportColumn> { new("Name", ""), new("Code", "", Quote: "always"), new("Qty", "", Quote: "never") };
        using var ms = new MemoryStream();
        await using (var w = new DelimitedWriter(ms, spec))
        {
            await w.WriteHeaderAsync(cols);
            await w.WriteRowAsync(cols, ["Smith, Jo", "TEMS", "1"]);
            await w.CompleteAsync();
        }
        Assert.Equal("Name,Code,Qty\n\"Smith, Jo\",\"TEMS\",1\n", Encoding.UTF8.GetString(ms.ToArray()));
    }

    [Fact]
    public void FixedWidthCell_PadsAlignsAndCuts()
    {
        Assert.Equal("ab   ", FixedWidthWriter.Cell(new ExportColumn("", "", 5), "ab"));
        Assert.Equal("00012", FixedWidthWriter.Cell(new ExportColumn("", "", 5, "right", "0"), "12"));
        Assert.Equal("a b c", FixedWidthWriter.Cell(new ExportColumn("", "", 5), "a\nb\nc\nd"));
    }

    [Fact]
    public async Task XlsxWriter_ProducesAWorkbookWithNumbersAndText()
    {
        var cols = new List<ExportColumn> { new("Sku", ""), new("Total", "", Type: "number") };
        using var ms = new MemoryStream();
        await using (var w = new XlsxWriter(ms))
        {
            await w.WriteHeaderAsync(cols);
            await w.WriteRowAsync(cols, ["A&B", "58.83"]);
            await w.CompleteAsync();
        }
        using var zip = new ZipArchive(new MemoryStream(ms.ToArray()), ZipArchiveMode.Read);
        Assert.NotNull(zip.GetEntry("[Content_Types].xml"));
        using var sheet = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        var xml = sheet.ReadToEnd();
        Assert.Contains("A&amp;B", xml);
        Assert.Contains("<c><v>58.83</v></c>", xml);
    }

    /// <summary>Cannella LF-style CALL / REVENUE rows in TRUE Eastern time (Stephen, S180 — CRMPro's double +3 h shift
    /// is not reproduced), rendered from a real call model.</summary>
    [Fact]
    public async Task CannellaLfStyleDocument_UsesTrueEasternTime()
    {
        var record = CallRecord.CreateInbound(Guid.Empty, "13035550100");
        typeof(CallRecord).GetProperty(nameof(CallRecord.CallStartAt))!
            .SetValue(record, new DateTimeOffset(2026, 10, 5, 2, 15, 0, TimeSpan.Zero)); // 10/4 10:15 PM Eastern
        record.SetDnis("8005550100");
        var ix = record.AddInteraction(InteractionType.OrderSale);
        ix.SetOrderNumber("LIFSEA-1");
        ix.SetCart(CartDocument.Empty() with { CartTotal = 58.83m, SalesTax = 1.65m });
        typeof(CallInteraction).GetProperty(nameof(CallInteraction.OrderSubmittedAt))!.SetValue(ix, DateTimeOffset.UtcNow);

        var lookups = new ExportCallModel.Lookups(new Dictionary<Guid, string>(), new Dictionary<Guid, string>(), new Dictionary<Guid, string>());
        var call = FluidLiquidTemplateRenderer.ToPlain(ExportCallModel.Build(record, lookups, []));
        const string template = """
            {%- for call in calls -%}
            "TEMS","{{ call.order_number }}","{{ call.started_at | format_time: 'MM/dd/yyyy' }}","{{ call.started_at | format_time: 'HH:mm:ss' }}","CALL",{{ call.area_code }}
            {% if call.has_order %}"TEMS","{{ call.order_number }}","REVENUE",{{ call.order_total | minus: call.order_tax | money }}
            {% endif %}
            {%- endfor -%}
            """;
        var text = ExportTemplateEngine.NormalizeLines(
            await Engine.RenderAsync(template, new Dictionary<string, object?> { ["calls"] = new List<object?> { call } }, document: true),
            "crlf", true);
        Assert.Equal(
            "\"TEMS\",\"LIFSEA-1\",\"10/04/2026\",\"22:15:00\",\"CALL\",303\r\n\"TEMS\",\"LIFSEA-1\",\"REVENUE\",57.18\r\n", text);
    }

    [Fact]
    public void AreaCode_SkipsTheCountryCode()
    {
        Assert.Equal("303", ExportCallModel.AreaCode("+1 (303) 555-0100"));
        Assert.Equal("", ExportCallModel.AreaCode("anonymous"));
    }

    [Fact]
    public void ApplyTestSuffix_GoesBeforeTheExtension()
    {
        Assert.Equal("NERQ_TMS_100426_TEST.txt", ExportRun.ApplyTestSuffix("NERQ_TMS_100426.txt", "_TEST"));
        Assert.Equal("x.csv", ExportRun.ApplyTestSuffix("x.csv", ""));
    }
}
