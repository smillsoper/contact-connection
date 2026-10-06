using System.Text;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects;
using ContactConnection.Domain.ValueObjects.Commerce;
using ContactConnection.Domain.ValueObjects.Exports;
using ContactConnection.Infrastructure.ApiExecution;
using ContactConnection.Infrastructure.Exports;
using ICSharpCode.SharpZipLib.Zip;
using Moq;
using PgpCore;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Exports;

public class ExportTemplateAndDeliveryTests
{
    private static readonly TimeZoneInfo Eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    /// <summary>An SF-style Cannella call with an order: 10/4 10:15 PM Eastern.</summary>
    private static Dictionary<string, object?> SampleCall(string adType, bool order)
    {
        var record = CallRecord.CreateInbound(Guid.Empty, "+15415550100");
        typeof(CallRecord).GetProperty(nameof(CallRecord.CallStartAt))!.SetValue(record, new DateTimeOffset(2026, 10, 5, 2, 15, 0, TimeSpan.Zero));
        record.SetDnis("+18005550199");
        record.SetAddresses(new CallAddresses
        {
            Billing = new AddressData { FirstName = "Jane", LastName = "Sample", Street = "1 Main St", City = "Bend", State = "OR", Zip = "97701", Zip4 = "1234" },
        });
        record.SetMediaAttribution(new MediaAttribution(Guid.Empty, "local", "Cannella", "KQEN", "Radio", adType,
            new DateOnly(2026, 10, 1), "+18005550199",
            new Dictionary<string, string> { ["access_code"] = "AB12", ["client_code"] = "13156", ["product_code"] = "NQ1" }));
        var offerId = Guid.NewGuid();
        if (order)
        {
            var ix = record.AddInteraction(InteractionType.OrderSale);
            ix.SetOrderNumber("LIFSEA-1");
            var item = new CartItem(offerId, Guid.Empty, "283-1", "Neuro-Q", 2, 49.95m, 99.90m, 0m, 0m, 0m, false, false, false, false, 0,
                false, 0, null, null, null, null, [], [], [], 0m, 0m, 0m, 0m);
            ix.SetCart(CartDocument.Empty() with { Items = [item], CartTotal = 105.55m, SalesTax = 5.65m });
            typeof(CallInteraction).GetProperty(nameof(CallInteraction.OrderSubmittedAt))!.SetValue(ix, DateTimeOffset.UtcNow);
        }
        var lookups = new ExportCallModel.Lookups(new Dictionary<Guid, string>(), new Dictionary<Guid, string>(), new Dictionary<Guid, string>(),
            new Dictionary<Guid, IReadOnlyList<ProductFlag>> { [offerId] = [new ProductFlag("Cannella Upsell SKU", "NQ-UP")] });
        return (Dictionary<string, object?>)FluidLiquidTemplateRenderer.ToPlain(ExportCallModel.Build(record, lookups, []))!;
    }

    private static async Task<string> Render(ExportStarterTemplates.StarterTemplate t, bool rerun = false, params Dictionary<string, object?>[] calls)
    {
        var engine = new ExportTemplateEngine(Eastern);
        var model = new Dictionary<string, object?>
        {
            ["calls"] = calls.Cast<object?>().ToList(),
            ["export"] = new Dictionary<string, object?> { ["is_rerun"] = rerun, ["name"] = t.Name },
        };
        return ExportTemplateEngine.NormalizeLines(await engine.RenderAsync(t.Spec.DocumentTemplate!, model, document: true), "lf", true);
    }

    [Fact]
    public void StarterTemplates_AllValidate()
    {
        var generator = new ExportGenerator(null!);
        foreach (var t in ExportStarterTemplates.All)
        {
            Assert.Null(generator.Validate(t.Spec));
            Assert.Null(ExportScheduleCalculator.Validate(t.Schedule));
        }
    }

    [Fact]
    public async Task CannellaSf_Is108Wide_WithResponseCodes_AndEasternTime()
    {
        var lines = (await Render(ExportStarterTemplates.CannellaSf, false, SampleCall("SF", order: true))).TrimEnd('\n').Split('\n');
        Assert.Equal(["VCAL", "CALL", "ORD ", "GREV"], lines.Select(l => l.Substring(59, 4)).ToArray());
        Assert.All(lines, l => Assert.Equal(108, l.Length));
        var first = lines[0];
        Assert.StartsWith("TMSSO", first);
        Assert.Equal("TVLIFE", first.Substring(17, 6));
        Assert.Equal("AB12", first.Substring(31, 4));
        Assert.Equal("KQEN        ", first.Substring(35, 12));
        Assert.Equal("202610042215", first.Substring(47, 12));   // true Eastern: 10/4 22:15
        Assert.Equal("000106", lines[3].Substring(63, 6));        // GREV: $105.55 → 106
        Assert.Equal("8005550199", first.Substring(69, 10));
        Assert.Equal("97701", first.Substring(79, 5));
        Assert.Equal("541", first.Substring(84, 3));
        Assert.StartsWith("TMSSR", (await Render(ExportStarterTemplates.CannellaSf, true, SampleCall("SF", false))).Split('\n')[0]);
    }

    [Fact]
    public async Task CannellaLf_WritesCallOrderUpsellRevenueRows()
    {
        var lines = (await Render(ExportStarterTemplates.CannellaLf, false, SampleCall("LF", order: true))).TrimEnd('\n').Split('\n');
        Assert.Equal(4, lines.Length);
        Assert.Equal("\"TEMS\",\"TEMS\",\"13156\",\"LIFSEA-1\",\"10/04/2026\",\"22:15:00\",\"CALL\",\"\",1,\"Valid\",\"8005550199\",\"KQEN\",\"NQ1\",541,\"\",\"97701\",\"Order\",5415550100,\"1 Main St\",\"\",\"Bend\",\"OR\",\"97701-1234\"", lines[0]);
        Assert.Contains("\"ORDER\",\"\",1,", lines[1]);
        Assert.Contains("\"UPSELL\",\"NQ-UP\",2,", lines[2]);
        Assert.Contains("\"REVENUE\",\"\",99.90,", lines[3]);

        var noOrder = (await Render(ExportStarterTemplates.CannellaLf, false, SampleCall("LF", order: false))).TrimEnd('\n').Split('\n');
        Assert.Single(noOrder);
        Assert.Contains("\"Inquiry\"", noOrder[0]);
    }

    // ── Delivery: encryption round trips through the email path ───────────────

    private static (ExportDeliveryService Sut, List<EmailMessage> Sent) Service()
    {
        var creds = new Mock<ITenantCredentialStore>();
        creds.Setup(c => c.GetForTenantAsync("t", "zip-pw", It.IsAny<CancellationToken>())).ReturnsAsync("s3cret!");
        var sent = new List<EmailMessage>();
        var email = new Mock<IEmailService>();
        email.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Callback<EmailMessage, CancellationToken>((m, _) => sent.Add(m)).Returns(Task.CompletedTask);
        return (new ExportDeliveryService(creds.Object, email.Object), sent);
    }

    private static string TempFile(string text)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, text);
        return path;
    }

    private static ExportDeliveryRequest Req(ExportDeliveryTarget t, string path) => new("t", t, path, "NERQ_TMS_100426.txt", "text/plain",
        new Dictionary<string, object?> { ["export"] = new Dictionary<string, object?> { ["name"] = "Cannella SF" } }, "America/New_York");

    [Fact]
    public async Task Email_ZipEncrypted_OpensWithThePassword()
    {
        var (sut, sent) = Service();
        var target = new ExportDeliveryTarget
        {
            Name = "Ops", Type = ExportDeliveryType.Email, EmailTo = ["ops@example.com"], Encryption = ExportEncryption.Zip, ZipPasswordCredential = "zip-pw",
        };
        var path = TempFile("hello vendor");
        try
        {
            Assert.Equal("ops@example.com", await sut.DeliverAsync(Req(target, path)));
            var att = Assert.Single(Assert.Single(sent).Attachments);
            Assert.Equal("NERQ_TMS_100426.zip", att.FileName);
            Assert.Equal("Cannella SF — NERQ_TMS_100426.zip", sent[0].Subject);
            using var zip = new ZipFile(new MemoryStream(att.Content)) { Password = "s3cret!" };
            var entry = zip.GetEntry("NERQ_TMS_100426.txt");
            Assert.True(entry.IsCrypted);
            Assert.Equal(256, entry.AESKeySize);
            using var reader = new StreamReader(zip.GetInputStream(entry));
            Assert.Equal("hello vendor", reader.ReadToEnd());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Email_PgpEncrypted_DecryptsWithTheRecipientsKey()
    {
        using var pub = new MemoryStream();
        using var priv = new MemoryStream();
        new PGP().GenerateKey(pub, priv, "vendor@example.com", "pw");
        var (sut, sent) = Service();
        var target = new ExportDeliveryTarget
        {
            Name = "Ops", Type = ExportDeliveryType.Email, EmailTo = ["ops@example.com"], Encryption = ExportEncryption.Pgp,
            PgpPublicKey = Encoding.UTF8.GetString(pub.ToArray()), EmailSubject = "{{ export.name }} file {{ file_name }}",
        };
        var path = TempFile("secret rows");
        try
        {
            await sut.DeliverAsync(Req(target, path));
            var att = Assert.Single(Assert.Single(sent).Attachments);
            Assert.Equal("NERQ_TMS_100426.txt.pgp", att.FileName);
            Assert.Equal("Cannella SF file NERQ_TMS_100426.txt.pgp", sent[0].Subject);
            using var output = new MemoryStream();
            await new PGP(new EncryptionKeys(Encoding.UTF8.GetString(priv.ToArray()), "pw")).DecryptAsync(new MemoryStream(att.Content), output);
            Assert.Equal("secret rows", Encoding.UTF8.GetString(output.ToArray()));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Sftp_WithoutAPinnedHostKey_IsRefused_Permanently()
    {
        var (sut, _) = Service();
        var target = new ExportDeliveryTarget { Name = "Cannella", Type = ExportDeliveryType.Sftp, Host = "sftp.invalid", Username = "u", PasswordCredential = "pw" };
        var ex = await Assert.ThrowsAsync<ExportDeliveryException>(() => sut.DeliverAsync(Req(target, "unused")));
        Assert.True(ex.Permanent);
        Assert.Contains("host key", ex.Message);
    }

    [Fact]
    public void Fingerprints_CompareIgnoringPrefixAndPadding()
    {
        Assert.True(ExportDeliveryService.Same("SHA256:abc+/def=", "abc+/def"));
        Assert.False(ExportDeliveryService.Same("abc", "abd"));
        Assert.False(ExportDeliveryService.Same(null, "abc"));
    }
}
