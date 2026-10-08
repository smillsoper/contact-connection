using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Porting;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Porting;

/// <summary>S184: SignalWire's LOA forms are filled in (overlay on the original page) and stay one page.</summary>
public class LoaDocumentsTests
{
    // 1×1 transparent PNG — stands in for the drawn signature.
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    internal static PortOrder Signed(string kind, int count)
    {
        var start = kind == PortOrderKind.TollFree ? 8005550100L : 5035550100L;
        var numbers = Enumerable.Range(0, count).Select(i => "+1" + (start + i)).ToList();
        var now = DateTimeOffset.UtcNow;
        var o = PortOrder.Create(Guid.NewGuid(), kind, numbers, "Business", "Life Seasons", "Lumen", null,
            Guid.NewGuid(), "Pat Admin", "pat@example.com", "owner@example.com", now);
        o.IssueSigningLink(now);
        o.AttachBill("k", "bill.pdf", "application/pdf");
        o.Sign(new PortSignerDetails
        {
            AuthorizedName = "Jane Q Owner", AuthorizedTitle = "CEO", BillingName = "Life Seasons Inc", AccountNumber = "8812-3344",
            BillingPhone = "(503) 555-0199", CurrentProvider = "Lumen", LongDistanceProvider = "AT&T", ServiceStreet = "123 Main St",
            ServiceUnit = "Suite 4", ServiceCity = "Portland", ServiceState = "OR", ServiceZip = "97204",
            MailingAddress = "PO Box 9, Portland, OR 97207", AlternateContact = "jane@example.com",
        }, "pin-token", false, "Jane Q Owner", "203.0.113.9", "Test", now);
        return o;
    }

    [Theory]
    [InlineData(PortOrderKind.Local, 3)]
    [InlineData(PortOrderKind.Local, 40)]
    [InlineData(PortOrderKind.TollFree, 4)]
    [InlineData(PortOrderKind.TollFree, 14)]
    public void Fills_the_form_on_one_page(string kind, int count)
    {
        var o = Signed(kind, count);
        var pdf = LoaDocuments.FillLoa(o, new LoaDocuments.FormContext("contactconnection", "11111111-2222-3333-4444-555555555555", "10/08/2026", Png));
        Assert.True(pdf.Length > 10_000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(System.Text.Encoding.Latin1.GetString(pdf), @"/Type\s*/Page[^s]"));
        if (Environment.GetEnvironmentVariable("CC_LOA_PREVIEW") is { Length: > 0 } dir)
            File.WriteAllBytes(Path.Combine(dir, $"loa-{kind}-{count}.pdf"), pdf);
    }

    [Fact]
    public void Certificate_and_number_list_are_produced()
    {
        var o = Signed(PortOrderKind.TollFree, 12);
        var cert = LoaDocuments.Certificate(o, new string('a', 64), "Life Seasons", "PT", TimeZoneInfo.Utc);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(cert, 0, 4));
        var csv = System.Text.Encoding.UTF8.GetString(LoaDocuments.NumbersCsv(o));
        Assert.Equal(13, csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("(800) 555-0100", csv);
    }
}
