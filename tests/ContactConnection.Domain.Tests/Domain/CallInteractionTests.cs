using ContactConnection.Domain.Entities;
using Xunit;

namespace ContactConnection.Domain.Tests.Domain;

/// <summary>S178: on a call transferred mid-call, each interaction carries its own agent, campaign and
/// script-written custom fields; the call record keeps the first campaign's.</summary>
public class CallInteractionTests
{
    [Fact]
    public void AssignTo_SetsAgentAndCampaign_EmptyCampaignIsNull()
    {
        var i = CallInteraction.Create(Guid.NewGuid(), 2, InteractionType.CustomerService);
        var agent = Guid.NewGuid();
        i.AssignTo(agent, Guid.Empty);
        Assert.Equal(agent, i.AgentId);
        Assert.Null(i.CampaignId);
    }

    [Fact]
    public void SetCustomField_MergesFields()
    {
        var i = CallInteraction.Create(Guid.NewGuid(), 2, InteractionType.CustomerService);
        i.SetCustomField("disposition", "Customer Service");
        i.SetCustomField("call_type", "Customer Service");
        i.SetCustomField("disposition", "Refund");

        Assert.Contains("\"disposition\":\"Refund\"", i.CustomFields);
        Assert.Contains("\"call_type\":\"Customer Service\"", i.CustomFields);
    }
}
