using ContactConnection.Domain.Entities;
using Xunit;

namespace ContactConnection.Domain.Tests.Domain;

/// <summary>S181: reporting by agent group uses the groups the agent was in at the time, or the group the call was routed through.</summary>
public class InteractionAgentGroupTests
{
    [Fact]
    public void Counts_for_groups_at_the_time_and_the_routed_group()
    {
        Guid alpha = Guid.NewGuid(), cs = Guid.NewGuid(), other = Guid.NewGuid();
        var ix = CallInteraction.Create(Guid.NewGuid(), 1, "inbound_call");
        ix.SetAgentGroups([cs, cs]);
        ix.SetRoutedTier(alpha, 1, "Alpha");

        Assert.True(ix.InGroup(alpha));
        Assert.True(ix.InGroup(cs));
        Assert.False(ix.InGroup(other));
        Assert.Single(ix.AgentGroupIds);
    }
}
