namespace ContactConnection.Domain.Entities;

public static class RemoteActionType
{
    /// <summary>The portal reports its health (browser, softphone, mic, extension, connections…).</summary>
    public const string Diagnostics = "diagnostics";
    /// <summary>The portal asks the browser extension to report in again.</summary>
    public const string Extension = "extension";
    /// <summary>The softphone drops and redoes its SIP registration (refused on a call).</summary>
    public const string Reregister = "reregister";
    /// <summary>A call panel stuck with no call actually connected is cleared (refused when one is).</summary>
    public const string ClearCall = "clear-call";
    /// <summary>The portal page reloads (refused on a live call unless forced).</summary>
    public const string Refresh = "refresh";

    public static readonly IReadOnlyList<string> All = [Diagnostics, Extension, Reregister, ClearCall, Refresh];
    /// <summary>These change something on the agent's machine — override permission; the others only look.</summary>
    public static bool Disruptive(string a) => a is Reregister or ClearCall or Refresh;
}

/// <summary>
/// A supervisor's remote fix on an agent's portal (S184): what was asked, by whom, and what the portal reported back.
/// The agent sees a notice each time; this is the audit trail.
/// </summary>
public class RemoteAction
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid AgentId { get; private set; }
    public Guid RequestedById { get; private set; }
    public string RequestedByName { get; private set; } = "";
    public string Action { get; private set; } = "";
    public DateTimeOffset RequestedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public bool? Ok { get; private set; }
    /// <summary>What the portal reported (diagnostics as JSON, otherwise a sentence).</summary>
    public string? Detail { get; private set; }

    private RemoteAction() { }

    public static RemoteAction Create(Guid tenantId, Guid agentId, Guid requestedById, string requestedByName, string action)
    {
        if (!RemoteActionType.All.Contains(action)) throw new ArgumentException("Unknown remote action.");
        return new RemoteAction
        {
            Id = Guid.NewGuid(), TenantId = tenantId, AgentId = agentId, RequestedById = requestedById,
            RequestedByName = requestedByName, Action = action, RequestedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Complete(bool ok, string? detail)
    {
        if (CompletedAt is not null) return;
        CompletedAt = DateTimeOffset.UtcNow;
        Ok = ok;
        Detail = detail is null ? null : detail.Length > 8000 ? detail[..8000] : detail;
    }
}
