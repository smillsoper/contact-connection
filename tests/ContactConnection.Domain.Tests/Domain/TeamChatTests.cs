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
        Assert.Null(Channel().PostBlockedReason(Bob, null, isChatManager: false));
    }

    [Fact]
    public void Restricted_posting_allows_only_selected_people_and_managers()
    {
        var c = Channel();
        c.Configure(isPrivate: false, postingRestricted: true, posterIds: [Ann], membershipLocked: false, assignedRoleIds: []);
        Assert.Null(c.PostBlockedReason(Ann, null, false));
        Assert.NotNull(c.PostBlockedReason(Bob, null, false));
        Assert.Null(c.PostBlockedReason(Bob, null, isChatManager: true));
    }

    [Fact]
    public void Restricted_posting_follows_poster_roles()
    {
        var leads = Guid.NewGuid();
        var c = Channel();
        c.Configure(false, postingRestricted: true, posterIds: [], membershipLocked: false, assignedRoleIds: [], posterRoleIds: [leads]);
        Assert.Null(c.PostBlockedReason(Bob, leads, false));                 // holds the role today
        Assert.NotNull(c.PostBlockedReason(Bob, Guid.NewGuid(), false));     // role changed — no longer
        Assert.NotNull(c.PostBlockedReason(Bob, null, false));
    }

    [Fact]
    public void Retired_channel_is_read_only_for_everyone_until_unretired()
    {
        var c = Channel();
        c.Retire();
        Assert.True(c.IsRetired);
        Assert.NotNull(c.PostBlockedReason(Ann, null, isChatManager: true));
        c.Unretire();
        Assert.Null(c.PostBlockedReason(Ann, null, false));
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
    public void Pinning_for_everyone_follows_people_roles_and_managers()
    {
        var leads = Guid.NewGuid();
        var c = Channel();
        Assert.False(c.CanPinForEveryone(Bob, null, false));
        Assert.True(c.CanPinForEveryone(Bob, null, isChatManager: true));
        c.SetPinners([Cy], [leads]);
        Assert.True(c.CanPinForEveryone(Cy, null, false));
        Assert.True(c.CanPinForEveryone(Bob, leads, false));
        c.Retire();
        Assert.False(c.CanPinForEveryone(Cy, null, true));
        Assert.True(ChatChannel.CreateDirect(Tenant, [Ann, Bob], Ann).CanPinForEveryone(Bob, null, false));   // a DM's members
    }

    [Fact]
    public void Deleting_others_messages_follows_moderators_roles_and_managers()
    {
        var leads = Guid.NewGuid();
        var c = Channel();
        Assert.False(c.CanDeleteOthers(Bob, null, false));
        Assert.True(c.CanDeleteOthers(Bob, null, isChatManager: true));
        c.SetModerators([Cy], [leads]);
        Assert.True(c.CanDeleteOthers(Cy, null, false));
        Assert.True(c.CanDeleteOthers(Bob, leads, false));
        Assert.False(c.CanDeleteOthers(Bob, Guid.NewGuid(), false));
        c.Retire();
        Assert.False(c.CanDeleteOthers(Cy, null, false));                  // retired history is read-only…
        Assert.True(c.CanDeleteOthers(Cy, null, isChatManager: true));     // …except to chat managers
        Assert.False(ChatChannel.CreateDirect(Tenant, [Ann, Bob], Ann).CanDeleteOthers(Bob, null, false));
    }

    [Fact]
    public void Pins_survive_until_unpinned_and_deleting_unpins()
    {
        var m = ChatMessage.Create(Guid.NewGuid(), Ann, "the refund policy", null);
        m.Pin(Bob);
        var at = m.PinnedAt;
        m.Pin(Cy);                                   // already pinned — first pin stands
        Assert.Equal(at, m.PinnedAt);
        Assert.Equal(Bob, m.PinnedById);
        m.Delete();
        Assert.Null(m.PinnedAt);
        Assert.Throws<InvalidOperationException>(() => m.Pin(Bob));
    }

    [Fact]
    public void Channel_mention_and_attachments()
    {
        var file = new ChatAttachment { Id = Guid.NewGuid(), Name = "rates.pdf", Size = 1234, ContentType = "application/pdf" };
        var m = ChatMessage.CreateRich(Guid.NewGuid(), Ann, "<p><span data-mention=\"channel\">@channel</span> new rates</p>", "@channel new rates",
            false, null, [file]);
        Assert.True(m.MentionsChannel);
        Assert.Empty(m.MentionIds);
        Assert.Equal([file.Id], m.AttachmentIds);
        var onlyFile = ChatMessage.CreateRich(Guid.NewGuid(), Ann, "<p></p>", "", false, null, [file]);
        Assert.Equal("[file]", onlyFile.BodyText);
        onlyFile.Delete();
        Assert.Empty(onlyFile.AttachmentIds);   // unreferenced — the cleanup job removes the file
    }

    [Theory]
    [InlineData("..\\..\\secret\\report.pdf", "report.pdf")]
    [InlineData("a<b>c\"d.txt", "abcd.txt")]
    [InlineData("   ", "file")]
    public void File_names_are_cleaned(string input, string expected) => Assert.Equal(expected, ChatFile.CleanName(input));

    [Fact]
    public void Programs_are_blocked() =>
        Assert.Contains(".exe", ChatFile.BlockedExtensions);

    [Fact]
    public void Nobody_supervises_themselves()
    {
        Assert.Throws<ArgumentException>(() => AgentSupervisor.Create(Ann, Ann));
    }
}
