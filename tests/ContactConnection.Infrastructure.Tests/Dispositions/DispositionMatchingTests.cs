using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Dispositions;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Dispositions;

public class DispositionMatchingTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Sale = Guid.NewGuid();
    private static readonly Guid Cs = Guid.NewGuid();
    private static readonly Guid ClientA = Guid.NewGuid();
    private static readonly Guid SalesCampaign = Guid.NewGuid();
    private static readonly Guid CsCampaign = Guid.NewGuid();

    [Fact]
    public void Matches_NameOrAlias_IgnoringCaseAndSpacing()
    {
        var d = Disposition.Create(Tenant, "Transferred to CS", null, Cs, null, null,
            ["Referred to Customer Service", "Transferred to Customer Service"]);
        Assert.True(d.Matches("transferred  to cs"));
        Assert.True(d.Matches(" Referred to customer service "));
        Assert.False(d.Matches("Order"));
        Assert.False(d.Matches(""));
    }

    [Fact]
    public void Resolve_NarrowestScopeWins_AndRetiredStillMatchesHistory()
    {
        var tenantOrder = Disposition.Create(Tenant, "Order", null, Sale, null, null);
        var campaignOrder = Disposition.Create(Tenant, "Order", "ORD", Sale, ClientA, SalesCampaign);
        var catalog = new[] { tenantOrder, campaignOrder };

        Assert.Same(campaignOrder, DispositionService.Resolve(catalog, "order", ClientA, SalesCampaign));
        Assert.Same(tenantOrder, DispositionService.Resolve(catalog, "Order", ClientA, CsCampaign));   // other campaign → tenant one
        Assert.Null(DispositionService.Resolve(catalog, "Junk", ClientA, SalesCampaign));

        var retired = Disposition.Create(Tenant, "Old wording", null, Cs, null, null);
        retired.SetActive(false);
        Assert.Same(retired, DispositionService.Resolve([retired], "old wording", ClientA, SalesCampaign));
    }

    [Fact]
    public void Aliases_DropDuplicatesAndTheNameItself()
    {
        var d = Disposition.Create(Tenant, "Order", null, Sale, null, null, ["order", "Sale", " sale ", ""]);
        Assert.Equal(["Sale"], d.Aliases);
        d.AddAlias("SALE");
        Assert.Single(d.Aliases);
    }

    /// <summary>Stephen's example: sales logs "Transferred to CS", the CS agent on the same record logs "Cancelled
    /// Subscription". Each interaction keeps its own; the call reads both.</summary>
    [Fact]
    public void TransferredCall_EachInteractionKeepsItsOwn_AndTheCallIsCompound()
    {
        var record = CallRecord.CreateInbound(Tenant, "5415550100");
        record.SetCampaign(SalesCampaign, ClientA);
        var sales = record.AddInteraction(InteractionType.OrderSale);
        sales.AssignTo(Guid.NewGuid(), SalesCampaign);
        var cs = record.AddInteraction(InteractionType.CustomerService);
        cs.AssignTo(Guid.NewGuid(), CsCampaign);

        record.UpdateCustomFieldsSnapshot("{\"disposition\":\"Transferred to CS\"}");   // the first campaign's field
        cs.SetCustomField("disposition", "Cancelled Subscription");                     // the transferred script's own

        Assert.Equal("Transferred to CS", DispositionService.RecordedText(record, sales));
        Assert.Equal("Cancelled Subscription", DispositionService.RecordedText(record, cs));

        sales.Complete("Transferred to CS");
        cs.Complete("Cancelled Subscription");
        Assert.Equal("Transferred to CS + Cancelled Subscription", record.CompoundDisposition);
    }

    [Fact]
    public void SystemCategories_SixBuiltIns_TestCallsExcludedFromKpis()
    {
        var all = DispositionCategory.SystemDefaults(Tenant).ToList();
        Assert.Equal(6, all.Count);
        Assert.True(all.Single(c => c.Key == DispositionCategoryKey.Sale).SalesOpportunity);
        Assert.True(all.Single(c => c.Key == DispositionCategoryKey.OpportunityNoSale).SalesOpportunity);
        Assert.False(all.Single(c => c.Key == DispositionCategoryKey.CustomerService).SalesOpportunity);
        Assert.True(all.Single(c => c.Key == DispositionCategoryKey.Test).ExcludedFromKpis);
        Assert.Throws<InvalidOperationException>(() => all[0].SetActive(false));
    }
}
