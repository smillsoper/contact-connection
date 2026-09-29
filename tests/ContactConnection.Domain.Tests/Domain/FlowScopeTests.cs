using ContactConnection.Domain.Entities;
using Xunit;

namespace ContactConnection.Domain.Tests.Domain;

/// <summary>A flow's optional home client/campaign (S164) — drives agent-portal previews; shared
/// flows (sub-flows) leave both empty.</summary>
public class FlowScopeTests
{
    private static Flow NewFlow() => Flow.Create(Guid.NewGuid(), Guid.NewGuid(), "NeuroQ - V1", FlowType.Crm, "{}");

    [Fact]
    public void SetScope_CampaignAndClient_ThenShared()
    {
        var flow = NewFlow();
        var (client, campaign) = (Guid.NewGuid(), Guid.NewGuid());

        flow.SetScope(client, campaign);
        Assert.Equal((client, campaign), (flow.ClientId!.Value, flow.CampaignId!.Value));

        flow.SetScope(null, null);
        Assert.Null(flow.ClientId);
        Assert.Null(flow.CampaignId);
    }

    [Fact]
    public void SetScope_ClientWide_IsAllowed_ButCampaignWithoutClientIsNot()
    {
        var flow = NewFlow();
        var client = Guid.NewGuid();
        flow.SetScope(client, null);
        Assert.Equal(client, flow.ClientId);
        Assert.Throws<ArgumentException>(() => flow.SetScope(null, Guid.NewGuid()));
    }
}
