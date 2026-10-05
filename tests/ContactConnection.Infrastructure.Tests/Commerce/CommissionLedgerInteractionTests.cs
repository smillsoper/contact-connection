using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects.Commerce;
using ContactConnection.Infrastructure.Commerce;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Commerce;

/// <summary>
/// Interaction-scoped commissions (S178): on a call transferred sales → CS, the sales agent earns under the sales
/// campaign from the call's own work, and a CS agent who placed their own order earns under the CS campaign's rules.
/// A call that wasn't transferred calculates exactly as before.
/// </summary>
public class CommissionLedgerInteractionTests
{
    private static readonly Guid Tenant = Guid.NewGuid(), Client = Guid.NewGuid();
    private static readonly Guid Sales = Guid.NewGuid(), Cs = Guid.NewGuid();
    private static readonly Guid SalesAgent = Guid.NewGuid(), CsAgent = Guid.NewGuid();

    private static CommissionRule FlatPerOrder(Guid campaign, decimal amount)
    {
        var r = CommissionRule.Create(Tenant, null, campaign);
        r.Set($"Flat {amount}", CommissionKind.FlatPerOrder, amount, null, null, null, null, null, true);
        return r;
    }

    // An order needs at least one line (CommissionCalculator: hasOrder = submitted && cart has items).
    private static CartDocument OneItem() => CartDocument.Empty() with
    {
        Items = [new CartItem(Guid.NewGuid(), Guid.NewGuid(), "SKU", "Item", 1, 10m, 10m, 0, 0, 0, false, false, false, false, 0,
            false, 0, null, null, null, null, [], [], [], 0, 0, 0, 0)],
        CartSubtotal = 10m, CartTotal = 10m,
    };

    private static readonly CommissionLedger.RuleBook Rules = new([FlatPerOrder(Sales, 2m), FlatPerOrder(Cs, 1m)]);

    private static (CallRecord Record, CallInteraction SalesIx, CallInteraction CsIx) TransferredCall()
    {
        var record = CallRecord.Create(Tenant, Client, Sales);
        record.SetAgent(SalesAgent);
        var salesIx = record.AddInteraction(InteractionType.CustomerService);
        salesIx.AssignTo(SalesAgent, Sales);
        salesIx.SetCart(OneItem());
        salesIx.MarkOrderSubmitted(DateTimeOffset.UtcNow);
        var csIx = record.AddInteraction(InteractionType.CustomerService);
        csIx.AssignTo(CsAgent, Cs);
        csIx.SetCart(OneItem());
        return (record, salesIx, csIx);
    }

    [Fact]
    public void Transferred_CsAgentsOwnOrder_EarnsUnderTheCsCampaign_SalesKeepsItsLine()
    {
        var (record, _, csIx) = TransferredCall();
        csIx.MarkOrderSubmitted(DateTimeOffset.UtcNow);

        var desired = CommissionLedger.DesiredFor(record, Rules);

        Assert.Equal(2, desired.Lines.Count);
        Assert.Contains(desired.Lines, l => l.AgentId == SalesAgent && l.CampaignId == Sales && l.Line.Amount == 2m);
        Assert.Contains(desired.Lines, l => l.AgentId == CsAgent && l.CampaignId == Cs && l.Line.Amount == 1m);
    }

    [Fact]
    public void Transferred_CsWithoutAnOrder_OnlyTheSalesLine()
    {
        var (record, _, _) = TransferredCall();

        var line = Assert.Single(CommissionLedger.DesiredFor(record, Rules).Lines);
        Assert.Equal((SalesAgent, Sales), (line.AgentId, line.CampaignId));
    }

    [Fact]
    public void SameCampaignRelaunch_IsNotPaidTwice()
    {
        var (record, _, _) = TransferredCall();
        var again = record.AddInteraction(InteractionType.CustomerService);
        again.AssignTo(SalesAgent, Sales);               // same campaign — part of the call's own work
        again.MarkOrderSubmitted(DateTimeOffset.UtcNow);

        var desired = CommissionLedger.DesiredFor(record, Rules);

        Assert.Single(desired.Lines, l => l.AgentId == SalesAgent);
    }
}
