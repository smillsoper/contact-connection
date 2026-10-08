using ContactConnection.Domain.Entities;
using Xunit;

namespace ContactConnection.Domain.Tests.Domain;

/// <summary>ContactConnection support in tenant portals (S184): Portal roles, session timing, permissions, the account.</summary>
public class SupportSessionTests
{
    [Theory]
    [InlineData(new[] { "Platform.Owner" }, false, "owner")]
    [InlineData(new[] { "Platform.Support", "Platform.Owner" }, true, "owner")]
    [InlineData(new[] { "platform.support" }, true, "support")]
    [InlineData(new string[0], false, "owner")]     // before enforcement: no lock-out
    [InlineData(new string[0], true, null)]         // after: no role, no Portal
    public void Portal_role_from_Entra_app_roles(string[] roles, bool enforce, string? expected) =>
        Assert.Equal(expected, PlatformRole.Resolve(roles, enforce));

    [Fact]
    public void Support_gets_every_permission_but_card_data_while_the_switch_is_off()
    {
        Assert.DoesNotContain(Permission.ExportsCardData, Permission.ForPlatformSupport(false));
        Assert.Equal(Permission.All.Count - 1, Permission.ForPlatformSupport(false).Count);
        Assert.Equal(Permission.All, Permission.ForPlatformSupport(true));
    }

    [Fact]
    public void A_session_needs_a_reason_and_lasts_an_hour()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentException>(() => SupportSession.Start(Guid.NewGuid(), "oid", "a@b.c", "A B", "support", " ", Guid.NewGuid(), now));
        var s = SupportSession.Start(Guid.NewGuid(), "oid", "a@b.c", "A B", "support", "Ticket 1", Guid.NewGuid(), now);
        Assert.True(s.IsActive(now.AddMinutes(59)));
        Assert.False(s.IsActive(now.AddMinutes(60)));
    }

    [Fact]
    public void Ending_stops_it_and_an_expired_session_ends_at_its_expiry()
    {
        var now = DateTimeOffset.UtcNow;
        var s = SupportSession.Start(Guid.NewGuid(), "oid", "a@b.c", "A B", "owner", "Ticket 2", Guid.NewGuid(), now);
        s.End(now.AddMinutes(5));
        Assert.False(s.IsActive(now.AddMinutes(6)));
        Assert.Equal(now.AddMinutes(5), s.EndedAt);

        var late = SupportSession.Start(Guid.NewGuid(), "oid", "a@b.c", "A B", "owner", "Ticket 3", Guid.NewGuid(), now);
        late.End(now.AddHours(3));
        Assert.Equal(late.ExpiresAt, late.EndedAt);
    }

    [Fact]
    public void Support_account_is_named_and_cannot_use_a_password()
    {
        var a = Agent.CreatePlatformSupport(Guid.NewGuid(), "oid-1", "Sam", "Helper");
        Assert.True(a.IsPlatformSupport);
        Assert.Equal("Sam", a.FirstName);
        Assert.Equal("Helper (ContactConnection Support)", a.LastName);
        Assert.Equal("!", a.PasswordHash);    // not a BCrypt hash — no password ever matches
        Assert.Null(a.SipExtension);
    }
}
