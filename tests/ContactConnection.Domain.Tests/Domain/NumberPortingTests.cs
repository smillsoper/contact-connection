using ContactConnection.Domain.Entities;
using Xunit;

namespace ContactConnection.Domain.Tests.Domain;

/// <summary>Number porting (S184): parsing pasted numbers, and an order's life from request to completion.</summary>
public class NumberPortingTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Pasted_numbers_are_formatted_deduped_and_bad_ones_listed()
    {
        var p = PortNumbers.Parse("(503) 555-1234\n1-503-555-1234, +1 800 555 0100; 555-1234\tabc-def-ghij\n0035551234");
        Assert.Equal(["+15035551234", "+18005550100"], p.Numbers);
        Assert.Equal(["555-1234", "abc-def-ghij", "0035551234"], p.Invalid);
        Assert.True(PortNumbers.IsTollFree("+18885550100"));
        Assert.False(PortNumbers.IsTollFree("+15035551234"));
        Assert.Equal("(503) 555-1234", PortNumbers.Display("+15035551234"));
    }

    private static PortOrder Order(string kind = PortOrderKind.Local, params string[] numbers) =>
        PortOrder.Create(Guid.NewGuid(), kind, numbers.Length > 0 ? numbers : ["+15035551234"], "Business", "Acme", null, null,
            Guid.NewGuid(), "Pat", "pat@x.com", "owner@x.com", T0);

    private static PortSignerDetails Good => new()
    {
        AuthorizedName = "Jane Owner", AuthorizedTitle = "CEO", BillingName = "Acme Inc", BillingPhone = "5035551234",
        CurrentProvider = "Lumen", ServiceStreet = "1 Main St", ServiceCity = "Portland", ServiceState = "OR", ServiceZip = "97204",
    };

    [Fact]
    public void Local_and_toll_free_never_share_an_order() =>
        Assert.Throws<ArgumentException>(() => Order(PortOrderKind.Local, "+15035551234", "+18005550100"));

    [Fact]
    public void Signing_link_only_works_until_it_expires_or_is_replaced()
    {
        var o = Order();
        var first = o.IssueSigningLink(T0);
        var second = o.IssueSigningLink(T0);
        Assert.NotEqual(PortOrder.HashToken(first), o.TokenHash);
        Assert.Equal(PortOrder.HashToken(second), o.TokenHash);
        Assert.True(o.CanSign(T0.AddDays(13)));
        Assert.False(o.CanSign(T0.AddDays(15)));
    }

    [Fact]
    public void Signing_checks_the_paperwork()
    {
        var o = Order(); o.IssueSigningLink(T0);
        Assert.Throws<ArgumentException>(() => o.Sign(Good, "pin", false, "Jane Owner", "1.2.3.4", null, T0));      // no bill yet
        o.AttachBill("k", "bill.pdf", "application/pdf");
        Assert.Throws<ArgumentException>(() => o.Sign(Good with { AuthorizedName = "Acme" }, "pin", false, "Acme", "ip", null, T0));
        Assert.Throws<ArgumentException>(() => o.Sign(Good with { ServiceStreet = "PO Box 12" }, "pin", false, "Jane Owner", "ip", null, T0));
        Assert.Throws<ArgumentException>(() => o.Sign(Good, null, false, "Jane Owner", "ip", null, T0));            // PIN or N/A
        Assert.Throws<ArgumentException>(() => o.Sign(Good, "pin", false, "J Owner", "ip", null, T0));               // typed name must match
        o.Sign(Good, null, true, "jane owner", "ip", "UA", T0);
        Assert.Equal(PortOrderStatus.ReadyToSubmit, o.Status);
        Assert.Null(o.TokenHash);
        Assert.Equal(30, o.SignatureDaysLeft(T0));
    }

    [Fact]
    public void Correction_then_full_life_to_completion_and_purge()
    {
        var o = Order(); o.IssueSigningLink(T0); o.AttachBill("bill-key", "bill.pdf", "application/pdf");
        o.Sign(Good, "pin", false, "Jane Owner", "ip", null, T0);
        var token = o.RequestCorrection("Account number doesn't match the bill", "Stephen", T0.AddDays(1));
        Assert.Equal(PortOrderStatus.NeedsCorrection, o.Status);
        Assert.True(o.CanSign(T0.AddDays(2)) && o.TokenHash == PortOrder.HashToken(token));
        o.Sign(Good with { AccountNumber = "999" }, "pin", false, "Jane Owner", "ip", null, T0.AddDays(2));

        Assert.Throws<InvalidOperationException>(() => o.ConfirmFoc(new DateOnly(2026, 10, 20), "S", T0));   // not submitted yet
        o.MarkSubmitted("SW-123", "Stephen", T0.AddDays(3));
        o.ConfirmFoc(new DateOnly(2026, 10, 20), "Stephen", T0.AddDays(5));
        o.Complete("Stephen", T0.AddDays(12));
        Assert.Equal(PortOrderStatus.Completed, o.Status);

        Assert.Null(o.PurgeSensitive(T0.AddDays(20)));                  // kept 30 days after closing
        Assert.Equal("bill-key", o.PurgeSensitive(T0.AddDays(43)));
        Assert.Null(o.PinProtected);
        Assert.True(o.Events.Count >= 7);
    }
}

/// <summary>S184 Phase B: where the numbers go, and the load / unload record.</summary>
public class NumberPortingLoadTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private static PortOrder Order() =>
        PortOrder.Create(Guid.NewGuid(), PortOrderKind.Local, ["+15035551234"], "Business", "Acme", null, null,
            Guid.NewGuid(), "Pat", "pat@x.com", "owner@x.com", T0);

    [Fact]
    public void Pre_assignment_needs_a_campaign_for_flows_and_locks_once_loaded()
    {
        var o = Order();
        Assert.Throws<ArgumentException>(() => o.SetPreAssignment(null, Guid.NewGuid(), null, "Pat", T0));
        var c = Guid.NewGuid();
        o.SetPreAssignment(c, Guid.NewGuid(), null, "Pat", T0);
        Assert.Equal(c, o.PreAssignCampaignId);
        o.MarkNumbersLoaded(1, [], "campaign X", T0);
        Assert.Throws<InvalidOperationException>(() => o.SetPreAssignment(null, null, null, "Pat", T0));
        Assert.Equal($"Port {o.Reference}", o.Label);
    }

    [Fact]
    public void Unloading_clears_the_loaded_mark_and_says_why()
    {
        var o = Order();
        o.MarkNumbersLoaded(1, ["+15035550000"], "Reserve", T0);
        Assert.Contains("(503) 555-0000", o.Events[^1].Text);
        o.MarkNumbersUnloaded(1, T0);
        Assert.Null(o.NumbersLoadedAt);
    }
}
