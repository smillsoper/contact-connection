using ContactConnection.Domain.Entities;
using Xunit;

namespace ContactConnection.Domain.Tests.Domain;

/// <summary>S181 client portal: client users (separate from agents) and client-dashboard scope locking.</summary>
public class ClientUserTests
{
    [Fact]
    public void Invite_token_is_stored_hashed_matches_once_and_is_burned_on_accept()
    {
        var u = ClientUser.Create(" Media@Agency.com ", "Pat", "Lee", canPlayRecordings: false, createdByAgentId: null);
        Assert.Equal("media@agency.com", u.Email);
        Assert.False(u.CanSignIn);

        var token = u.IssueInvite();
        Assert.NotEqual(token, u.InviteTokenHash);
        Assert.True(u.InviteMatches(token));
        Assert.False(u.InviteMatches(token + "x"));

        u.AcceptInvite("hash", "Patricia", null);
        Assert.True(u.CanSignIn);
        Assert.Equal("Patricia", u.FirstName);
        Assert.Equal("Lee", u.LastName);
        Assert.False(u.InviteMatches(token));
    }

    [Fact]
    public void A_new_link_replaces_the_old_one_and_expired_links_never_match()
    {
        var u = ClientUser.Create("a@b.com", "A", "B", false, null);
        var first = u.IssueInvite();
        var second = u.IssueInvite();
        Assert.False(u.InviteMatches(first));
        Assert.True(u.InviteMatches(second));

        var expired = u.IssueInvite(TimeSpan.FromSeconds(-1));
        Assert.False(u.InviteMatches(expired));
    }

    [Fact]
    public void Deactivated_users_cannot_sign_in()
    {
        var u = ClientUser.Create("a@b.com", "A", "B", true, null);
        u.AcceptInvite("hash", null, null);
        u.Update("A", "B", isActive: false, canPlayRecordings: true);
        Assert.False(u.CanSignIn);
    }

    [Fact]
    public void Dashboards_replace_and_the_default_must_be_assigned()
    {
        var u = ClientUser.Create("a@b.com", "A", "B", false, null);
        Guid d1 = Guid.NewGuid(), d2 = Guid.NewGuid(), d3 = Guid.NewGuid();
        u.SetDashboards([d1, d2, d2]);
        Assert.Equal(2, u.Dashboards.Count);

        u.SetPreferences("America/Los_Angeles", d3);
        Assert.Null(u.DefaultDashboardId);                     // not assigned
        u.SetPreferences(null, d2);
        Assert.Equal(d2, u.DefaultDashboardId);

        u.SetDashboards([d1]);
        Assert.False(u.CanOpen(d2));
        Assert.Null(u.DefaultDashboardId);                     // unassigned default cleared
    }

    [Fact]
    public void Client_dashboard_scope_never_widens_to_a_widget_filter_outside_it()
    {
        Guid c1 = Guid.NewGuid(), c2 = Guid.NewGuid(), c3 = Guid.NewGuid(), other = Guid.NewGuid();
        var clientCampaigns = new[] { c1, c2, c3 };
        var d = Dashboard.Create(Guid.NewGuid(), Guid.NewGuid(), "LS", false, "[]");

        d.SetClientScope(true, Guid.NewGuid(), []);
        Assert.Equal(clientCampaigns, d.EffectiveCampaigns(clientCampaigns, null));      // all the client's
        Assert.Equal([c2], d.EffectiveCampaigns(clientCampaigns, c2));                  // narrowed inside scope
        Assert.Equal(clientCampaigns, d.EffectiveCampaigns(clientCampaigns, other));   // outside → ignored

        d.SetClientScope(true, Guid.NewGuid(), [c1, c2, other]);
        Assert.Equal([c1, c2], d.EffectiveCampaigns(clientCampaigns, null));            // a campaign no longer the client's drops
        Assert.Equal([c1, c2], d.EffectiveCampaigns(clientCampaigns, c3));              // not in the scope → ignored

        d.SetClientScope(false, Guid.NewGuid(), [c1]);
        Assert.Null(d.ScopeClientId);
        Assert.Empty(d.ScopeCampaignIds);
    }
}
