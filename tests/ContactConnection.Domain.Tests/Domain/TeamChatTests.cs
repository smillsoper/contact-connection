using ContactConnection.Domain.Entities;
using Xunit;

namespace ContactConnection.Domain.Tests.Domain;

/// <summary>S183 team chat: channel controls, direct-message identity, mentions, raise-hand lifecycle.</summary>
public class TeamChatTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Ann = Guid.NewGuid(), Bob = Guid.NewGuid(), Cy = Guid.NewGuid();

    private static ChatChannel Channel() => ChatChannel.CreateChannel(Tenant, "#floor", "Floor news", false, Ann);

    [Fact]
    public void Channel_name_drops_the_hash_and_is_required()
    {
        Assert.Equal("floor", Channel().Name);
        Assert.Throws<ArgumentException>(() => ChatChannel.CreateChannel(Tenant, "  # ", null, false, null));
    }

    [Fact]
    public void Anyone_in_an_open_channel_can_post()
    {
        Assert.Null(Channel().PostBlockedReason(Bob, isChatManager: false));
    }

    [Fact]
    public void Restricted_posting_allows_only_selected_people_and_managers()
    {
        var c = Channel();
        c.Configure(isPrivate: false, postingRestricted: true, posterIds: [Ann], membershipLocked: false, assignedRoleIds: []);
        Assert.Null(c.PostBlockedReason(Ann, false));
        Assert.NotNull(c.PostBlockedReason(Bob, false));
        Assert.Null(c.PostBlockedReason(Bob, isChatManager: true));
    }

    [Fact]
    public void Retired_channel_is_read_only_for_everyone_until_unretired()
    {
        var c = Channel();
        c.Retire();
        Assert.True(c.IsRetired);
        Assert.NotNull(c.PostBlockedReason(Ann, isChatManager: true));
        c.Unretire();
        Assert.Null(c.PostBlockedReason(Ann, false));
    }

    [Fact]
    public void Direct_key_ignores_order_and_duplicates()
    {
        Assert.Equal(ChatChannel.DirectKeyFor([Ann, Bob, Cy]), ChatChannel.DirectKeyFor([Cy, Ann, Bob, Ann]));
        Assert.NotEqual(ChatChannel.DirectKeyFor([Ann, Bob]), ChatChannel.DirectKeyFor([Ann, Cy]));
    }

    [Fact]
    public void Mentions_are_parsed_once_each_in_order()
    {
        var m = ChatMessage.Create(Guid.NewGuid(), Ann, $"hey <@{Bob}> and <@{Cy}>, <@{Bob}> again <@not-a-guid>", null);
        Assert.Equal([Bob, Cy], m.MentionIds);
        m.Edit("no one now");
        Assert.Empty(m.MentionIds);
    }

    [Fact]
    public void Messages_must_have_text_within_the_limit()
    {
        Assert.Throws<ArgumentException>(() => ChatMessage.Create(Guid.NewGuid(), Ann, "   ", null));
        Assert.Throws<ArgumentException>(() => ChatMessage.Create(Guid.NewGuid(), Ann, new string('x', ChatMessage.MaxLength + 1), null));
    }

    [Fact]
    public void Deleting_clears_the_text_and_replies_are_counted()
    {
        var parent = ChatMessage.Create(Guid.NewGuid(), Ann, "question", null);
        parent.AddReply(DateTimeOffset.UtcNow);
        parent.AddReply(DateTimeOffset.UtcNow);
        parent.RemoveReply();
        Assert.Equal(1, parent.ReplyCount);
        parent.Delete();
        Assert.Equal("", parent.Body);
        Assert.NotNull(parent.DeletedAt);
    }

    [Fact]
    public void Member_who_left_rejoins_with_a_fresh_read_marker()
    {
        var m = ChatMember.Create(Guid.NewGuid(), Bob, assigned: false);
        m.Leave();
        Assert.False(m.IsActive);
        m.Rejoin(assigned: true);
        Assert.True(m.IsActive);
        Assert.True(m.IsAssigned);
    }

    [Fact]
    public void Help_request_is_claimed_once()
    {
        var h = HelpRequest.Create(Tenant, Bob, " stuck on a refund ", null, [Ann, Cy, Ann], wentToAll: false);
        Assert.Equal("stuck on a refund", h.Note);
        Assert.Equal([Ann, Cy], h.NotifiedIds);
        h.Claim(Ann, Guid.NewGuid());
        Assert.Equal(HelpRequestStatus.Claimed, h.Status);
        Assert.Throws<InvalidOperationException>(() => h.Claim(Cy, Guid.NewGuid()));
        h.Cancel();   // closed already — no change
        Assert.Equal(HelpRequestStatus.Claimed, h.Status);
    }

    [Fact]
    public void Nobody_supervises_themselves()
    {
        Assert.Throws<ArgumentException>(() => AgentSupervisor.Create(Ann, Ann));
    }
}
