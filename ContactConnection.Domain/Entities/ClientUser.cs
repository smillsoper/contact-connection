using System.Security.Cryptography;
using System.Text;

namespace ContactConnection.Domain.Entities;

/// <summary>
/// A tenant's customer (or a vendor the tenant invites, e.g. a media agency) who signs in to view client dashboards
/// (S181, docs/client-dashboards-plan.md §B). Deliberately NOT an agent: no agent record, no role, no permissions —
/// its tokens carry their own audience, so the agent / admin APIs reject them outright. Stored in the tenant schema.
/// </summary>
public class ClientUser
{
    public Guid Id { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    /// <summary>Null until the invite is accepted.</summary>
    public string? PasswordHash { get; private set; }
    public bool MfaEnabled { get; private set; }
    public string? MfaSecret { get; private set; }
    public bool IsActive { get; private set; } = true;
    /// <summary>The tenant decides who may play call recordings — off by default (a media agency gets the numbers only).</summary>
    public bool CanPlayRecordings { get; private set; }

    /// <summary>SHA-256 of the outstanding invite / set-password link's token — the raw token is only ever in the email.</summary>
    public string? InviteTokenHash { get; private set; }
    public DateTimeOffset? InviteExpiresAt { get; private set; }

    /// <summary>Viewer preferences: the zone dates show in (null = the tenant's) and the dashboard opened first.</summary>
    public string? TimeZone { get; private set; }
    public Guid? DefaultDashboardId { get; private set; }

    public DateTimeOffset? LastLoginAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedByAgentId { get; private set; }

    public List<ClientUserDashboard> Dashboards { get; private set; } = [];

    private ClientUser() { }

    public static ClientUser Create(string email, string firstName, string lastName, bool canPlayRecordings, Guid? createdByAgentId) => new()
    {
        Id = Guid.NewGuid(),
        Email = email.Trim().ToLowerInvariant(),
        FirstName = firstName.Trim(),
        LastName = lastName.Trim(),
        CanPlayRecordings = canPlayRecordings,
        CreatedAt = DateTimeOffset.UtcNow,
        CreatedByAgentId = createdByAgentId,
    };

    public string FullName => $"{FirstName} {LastName}".Trim();
    public bool HasPassword => PasswordHash is not null;
    public bool CanSignIn => IsActive && PasswordHash is not null;

    /// <summary>Issues a fresh invite / set-password link (7 days) and returns the raw token for the email. Any earlier
    /// link stops working. An existing password keeps working until the new link is used.</summary>
    public string IssueInvite(TimeSpan? lifetime = null)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        InviteTokenHash = HashToken(token);
        InviteExpiresAt = DateTimeOffset.UtcNow.Add(lifetime ?? TimeSpan.FromDays(7));
        return token;
    }

    public bool InviteMatches(string token) =>
        InviteTokenHash is not null && InviteExpiresAt > DateTimeOffset.UtcNow
        && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(InviteTokenHash), Encoding.ASCII.GetBytes(HashToken(token)));

    /// <summary>Accepting the link sets the password (and optionally the name) and burns the link.</summary>
    public void AcceptInvite(string passwordHash, string? firstName, string? lastName)
    {
        PasswordHash = passwordHash;
        if (!string.IsNullOrWhiteSpace(firstName)) FirstName = firstName.Trim();
        if (!string.IsNullOrWhiteSpace(lastName)) LastName = lastName.Trim();
        InviteTokenHash = null;
        InviteExpiresAt = null;
    }

    public static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public void Update(string firstName, string lastName, bool isActive, bool canPlayRecordings)
    {
        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        IsActive = isActive;
        CanPlayRecordings = canPlayRecordings;
    }

    /// <summary>Replaces the dashboards this user may open; a default that's no longer assigned is cleared.</summary>
    public void SetDashboards(IEnumerable<Guid> dashboardIds)
    {
        var ids = dashboardIds.Distinct().ToList();
        Dashboards.RemoveAll(d => !ids.Contains(d.DashboardId));
        foreach (var id in ids.Where(id => Dashboards.All(d => d.DashboardId != id)))
            Dashboards.Add(new ClientUserDashboard(Id, id));
        if (DefaultDashboardId is { } def && !ids.Contains(def)) DefaultDashboardId = null;
    }

    public bool CanOpen(Guid dashboardId) => Dashboards.Any(d => d.DashboardId == dashboardId);

    public void SetPreferences(string? timeZone, Guid? defaultDashboardId)
    {
        TimeZone = string.IsNullOrWhiteSpace(timeZone) ? null : timeZone.Trim();
        DefaultDashboardId = defaultDashboardId is { } d && CanOpen(d) ? d : null;
    }

    public void StoreMfaSecret(string secret) => MfaSecret = secret;
    public void EnableMfa() => MfaEnabled = true;
    public void ResetMfa() { MfaEnabled = false; MfaSecret = null; }
    public void RecordLogin() => LastLoginAt = DateTimeOffset.UtcNow;
}

/// <summary>One dashboard a client user may open (many per user, across clients — e.g. a media agency).</summary>
public class ClientUserDashboard
{
    public Guid ClientUserId { get; private set; }
    public Guid DashboardId { get; private set; }

    private ClientUserDashboard() { }
    public ClientUserDashboard(Guid clientUserId, Guid dashboardId) { ClientUserId = clientUserId; DashboardId = dashboardId; }
}

/// <summary>Client-portal audit trail: sign-ins (and failures), recording plays, exports, admin changes to the account.</summary>
public class ClientUserAuditEntry
{
    public Guid Id { get; private set; }
    public Guid? ClientUserId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string? Detail { get; private set; }
    public string? IpAddress { get; private set; }
    /// <summary>Set when a tenant admin did it (invite, edit, reset); null when the client user did.</summary>
    public Guid? ByAgentId { get; private set; }
    public DateTimeOffset At { get; private set; }

    private ClientUserAuditEntry() { }

    public static ClientUserAuditEntry Create(Guid? clientUserId, string action, string? detail, string? ip, Guid? byAgentId = null) => new()
    {
        Id = Guid.NewGuid(), ClientUserId = clientUserId, Action = action, Detail = detail, IpAddress = ip, ByAgentId = byAgentId,
        At = DateTimeOffset.UtcNow,
    };
}

public static class ClientUserAuditAction
{
    public const string SignIn = "sign_in";
    public const string SignInFailed = "sign_in_failed";
    public const string InviteAccepted = "invite_accepted";
    public const string Invited = "invited";
    public const string Updated = "updated";
    public const string MfaReset = "mfa_reset";
    public const string MfaEnabled = "mfa_enabled";
    public const string DashboardViewed = "dashboard_viewed";
    public const string RecordingPlayed = "recording_played";
    public const string Exported = "exported";
}
