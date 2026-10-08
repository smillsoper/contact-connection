using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ContactConnection.Domain.Entities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ContactConnection.Infrastructure.Porting;

/// <summary>
/// The port-in paperwork (S184). SignalWire requires its own LOA forms, which are flat PDFs — so the answers and the
/// signer's drawn signature are written onto an overlay page at the exact blanks (measured from the forms) and laid over
/// SignalWire's original page. The e-signature record is a separate PDF (the toll-free LOA must stay one page).
/// </summary>
public static class LoaDocuments
{
    static LoaDocuments()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.ThrowOnMissingTextGlyphs = false;
    }

    /// <summary>What goes on the form besides the order and the signer's answers.</summary>
    public sealed record FormContext(string SpaceName, string ProjectId, string SignedDate, byte[] SignaturePng);

    public static byte[] FillLoa(PortOrder order, FormContext ctx)
    {
        var s = order.Signer ?? throw new InvalidOperationException("The order isn't signed.");
        var overlay = order.Kind == PortOrderKind.TollFree ? TollFreeOverlay(order, s, ctx) : LocalOverlay(order, s, ctx);
        var form = order.Kind == PortOrderKind.TollFree ? "signalwire-tollfree-loa.pdf" : "signalwire-local-loa.pdf";

        var dir = Path.Combine(Path.GetTempPath(), "cc-loa-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var formPath = Path.Combine(dir, "form.pdf");
            var overlayPath = Path.Combine(dir, "overlay.pdf");
            var outPath = Path.Combine(dir, "out.pdf");
            File.WriteAllBytes(formPath, ReadForm(form));
            File.WriteAllBytes(overlayPath, overlay);
            DocumentOperation.LoadFile(formPath)
                .OverlayFile(new DocumentOperation.LayerConfiguration { FilePath = overlayPath })
                .Save(outPath);
            return File.ReadAllBytes(outPath);
        }
        finally { try { Directory.Delete(dir, true); } catch { /* temp */ } }
    }

    private static byte[] ReadForm(string name)
    {
        var asm = typeof(LoaDocuments).Assembly;
        var resource = asm.GetManifestResourceNames().Single(n => n.EndsWith("Porting.Forms." + name, StringComparison.Ordinal));
        using var stream = asm.GetManifestResourceStream(resource)!;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>A text item at (x, y) points from the page's top-left, clipped to width.</summary>
    private sealed record Item(float X, float Y, float W, string? Text, float Size = 9, byte[]? Image = null, float H = 0);

    private static byte[] Render(IEnumerable<Item> items) =>
        Document.Create(c => c.Page(page =>
        {
            page.Size(612, 792, Unit.Point);
            page.Margin(0);
            page.PageColor(Colors.Transparent);
            page.DefaultTextStyle(t => t.FontFamily("Lato").FontColor("#0b2a6f"));
            page.Content().Layers(layers =>
            {
                layers.PrimaryLayer().Width(612).Height(792);
                foreach (var i in items)
                {
                    var layer = layers.Layer().OffsetX(i.X).OffsetY(i.Y).Width(i.W);
                    if (i.Image is not null) layer.Height(i.H).AlignLeft().Image(i.Image).FitArea();
                    else if (!string.IsNullOrWhiteSpace(i.Text)) layer.Text(i.Text).FontSize(i.Size).ClampLines(i.Size < 9 ? 3 : 1);
                }
            });
        })).GeneratePdf();

    private static string NumbersSummary(PortOrder o, int fit)
    {
        var shown = o.Numbers.Take(fit).Select(PortNumbers.Display);
        return o.Numbers.Count <= fit
            ? string.Join(", ", shown)
            : $"{string.Join(", ", shown)} … and {o.Numbers.Count - fit} more ({o.Numbers.Count} total — full list attached)";
    }

    /// <summary>SignalWire "Letter of Authorization for Local Number Porting Request" (April 2020).</summary>
    private static byte[] LocalOverlay(PortOrder o, PortSignerDetails s, FormContext ctx) => Render(
    [
        new(250, 188, 322, NumbersSummary(o, 9), 7.5f),
        new(250, 224, 196, s.CurrentProvider ?? o.CurrentProviderHint),
        new(453, 249, 120, s.LongDistanceProvider, 8),
        new(250, 280, 322, s.BillingName),
        new(250, 322, 322, s.AccountNumber),
        new(250, 354, 322, s.ServiceAddressLine),
        new(250, 386, 322, s.MailingAddress),
        new(250, 418, 322, s.AlternateContact),
        new(92, 613, 280, ctx.SpaceName),
        new(441, 614, 134, ctx.ProjectId, 6.3f),
        new(166, 628, 205, null, Image: ctx.SignaturePng, H: 18),
        new(415, 633, 158, ctx.SignedDate),
        new(296, 649, 78, o.SignatureName, 6.5f),
    ]);

    /// <summary>SignalWire "Responsible Organization Letter of Authorization" (RespOrg LQX01) — one page, 10 numbers.</summary>
    private static byte[] TollFreeOverlay(PortOrder o, PortSignerDetails s, FormContext ctx)
    {
        var lineY = new float[] { 373.3f, 391.5f, 409.6f, 427.7f, 445.8f, 464.0f, 482.1f, 500.2f, 518.3f, 536.4f };
        var items = new List<Item>
        {
            new(168, 243, 168, s.BillingName),
            new(168, 279, 168, $"{s.ServiceStreet}{(string.IsNullOrWhiteSpace(s.ServiceUnit) ? "" : " " + s.ServiceUnit)}"),
            new(168, 307, 168, s.ServiceCity),
            new(418, 308, 70, s.ServiceState),
            new(512, 308, 62, s.ServiceZip),
            new(168, 640, 250, null, Image: ctx.SignaturePng, H: 18),
            new(480, 640, 95, ctx.SignedDate),
            new(168, 666, 250, o.SignatureName),
            new(480, 666, 95, s.AuthorizedTitle),
        };
        for (var i = 0; i < Math.Min(10, o.Numbers.Count); i++)
            items.Add(new(168, lineY[i] - 12, 166, PortNumbers.Display(o.Numbers[i])));
        if (o.Numbers.Count > 10)
            items.Add(new(340, 525, 230, $"{o.Numbers.Count} numbers in total — all listed on the attached spreadsheet", 7));
        return Render(items);
    }

    /// <summary>All the order's numbers, one per row (SignalWire: "attach them all on a spreadsheet").</summary>
    public static byte[] NumbersCsv(PortOrder o)
    {
        var sb = new StringBuilder("Number,Formatted\r\n");
        foreach (var n in o.Numbers) sb.Append(n).Append(',').Append(PortNumbers.Display(n)).Append("\r\n");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>The electronic-signature record: who signed what, when, from where, and the fingerprint of the signed LOA.</summary>
    public static byte[] Certificate(PortOrder o, string loaSha256, string tenantName, string zoneLabel, TimeZoneInfo zone)
    {
        var s = o.Signer!;
        string When(DateTimeOffset? t) => t is { } v ? TimeZoneInfo.ConvertTime(v, zone).ToString("yyyy-MM-dd h:mm:ss tt", CultureInfo.InvariantCulture) + $" {zoneLabel}" : "";
        var rows = new (string, string)[]
        {
            ("Order", $"{o.Reference} — {(o.Kind == PortOrderKind.TollFree ? "toll-free" : "local")} port, {o.Numbers.Count} number(s)"),
            ("Account (tenant)", tenantName),
            ("Requested by", $"{o.RequestedByName} <{o.RequestedByEmail}> on {When(o.CreatedAt)}"),
            ("Signing link sent to", o.SignerEmail),
            ("Signed by", $"{o.SignatureName}{(string.IsNullOrWhiteSpace(s.AuthorizedTitle) ? "" : ", " + s.AuthorizedTitle)}"),
            ("Signed at", When(o.SignedAt)),
            ("IP address", o.SignerIp ?? ""),
            ("Browser", o.SignerUserAgent ?? ""),
            ("Consent", "Agreed to sign electronically and that the electronic signature is the legal equivalent of a handwritten signature."),
            ("Document", o.Kind == PortOrderKind.TollFree ? "SignalWire Responsible Organization Letter of Authorization (RespOrg LQX01)" : "SignalWire Letter of Authorization for Local Number Porting Request"),
            ("Document SHA-256", loaSha256),
        };
        return Document.Create(c => c.Page(page =>
        {
            page.Size(PageSizes.Letter);
            page.Margin(48);
            page.DefaultTextStyle(t => t.FontSize(10).FontFamily("Lato"));
            page.Header().Column(col =>
            {
                col.Item().Text("Electronic signature record").FontSize(18).Bold();
                col.Item().Text($"ContactConnection — port-in order {o.Reference}").FontColor(Colors.Grey.Darken1);
            });
            page.Content().PaddingTop(16).Column(col =>
            {
                foreach (var (label, value) in rows)
                    col.Item().PaddingBottom(6).Row(r =>
                    {
                        r.ConstantItem(150).Text(label).SemiBold();
                        r.RelativeItem().Text(value);
                    });
                col.Item().PaddingTop(12).Text("Numbers").SemiBold();
                col.Item().Text(string.Join(", ", o.Numbers.Select(PortNumbers.Display))).FontSize(9);
                col.Item().PaddingTop(16).Text("The signed letter of authorization is identified by the SHA-256 fingerprint above; any change to that file changes its fingerprint.")
                    .FontSize(8).FontColor(Colors.Grey.Darken1);
            });
        })).GeneratePdf();
    }
}
