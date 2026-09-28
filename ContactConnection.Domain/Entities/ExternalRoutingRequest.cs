namespace ContactConnection.Domain.Entities;

/// <summary>
/// One request from an external router (e.g. RingSquared) to our external-routing API — a routing
/// decision ("will you take this call?") or a CallInfo report of a call it placed. Every routing
/// decision is logged, accepted or rejected, so reject volume shows in reporting (TMS wrote rejects
/// into its CDR as 1-second "IsReject" interactions) and CallInfo rows give billing/attribution
/// reconciliation. See docs/design/parallel-queuing.md.
/// </summary>
public class ExternalRoutingRequest
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid NumberProviderId { get; private set; }
    /// <summary><see cref="ExternalRoutingRequestKind"/>.</summary>
    public string Kind { get; private set; } = ExternalRoutingRequestKind.Routing;
    public Guid? CampaignId { get; private set; }
    /// <summary>The <c>?group=</c> a routing decision was asked for (e.g. Elite), if any.</summary>
    public Guid? AgentGroupId { get; private set; }
    /// <summary>The router's own id for the call (RingSquared's SessionId / CallInfo ClientID).</summary>
    public string? ExternalSessionId { get; private set; }
    /// <summary>The public number the caller dialed (client TFN), digits only.</summary>
    public string? Dnis { get; private set; }
    public string? Ani { get; private set; }
    /// <summary>Routing: whether we accepted. Null for CallInfo.</summary>
    public bool? Accepted { get; private set; }
    /// <summary>Why rejected ("No Agents Available", "Number not configured"), or the accept basis.</summary>
    public string? Reason { get; private set; }
    /// <summary>Delivery number(s) we returned, comma-separated.</summary>
    public string? Targets { get; private set; }
    /// <summary>CallInfo: the call date as the router reported it (kept verbatim).</summary>
    public string? ReportedCallDate { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private ExternalRoutingRequest() { }

    public static ExternalRoutingRequest Routing(
        Guid tenantId, Guid providerId, Guid campaignId, Guid? groupId,
        string? sessionId, string? dnis, string? ani, bool accepted, string? reason, IEnumerable<string> targets) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        NumberProviderId = providerId,
        Kind = ExternalRoutingRequestKind.Routing,
        CampaignId = campaignId,
        AgentGroupId = groupId,
        ExternalSessionId = Trunc(sessionId, 200),
        Dnis = Trunc(dnis, 50),
        Ani = Trunc(ani, 50),
        Accepted = accepted,
        Reason = Trunc(reason, 200),
        Targets = Trunc(string.Join(",", targets), 500),
        CreatedAt = DateTimeOffset.UtcNow,
    };

    public static ExternalRoutingRequest CallInfo(
        Guid tenantId, Guid providerId, string? clientId, string? callDate, string? dialedTfn, string? ani) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        NumberProviderId = providerId,
        Kind = ExternalRoutingRequestKind.CallInfo,
        ExternalSessionId = Trunc(clientId, 200),
        ReportedCallDate = Trunc(callDate, 50),
        Dnis = Trunc(dialedTfn, 50),
        Ani = Trunc(ani, 50),
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static string? Trunc(string? s, int max) =>
        string.IsNullOrEmpty(s) ? null : s.Length <= max ? s : s[..max];
}

public static class ExternalRoutingRequestKind
{
    public const string Routing  = "routing";
    public const string CallInfo = "call_info";
}

/// <summary>
/// How a campaign answers an external router's "will you take a call?" (TMS's reject_limits):
/// every mode first requires at least one eligible agent logged in.
/// </summary>
public static class ExternalRoutingAcceptMode
{
    /// <summary>Accept while fewer than Limit calls are queued (default Limit 1 = nobody waiting).</summary>
    public const string QueueCount     = "queue_count";
    /// <summary>Accept while the longest-waiting call has waited under Limit seconds.</summary>
    public const string QueueWait      = "queue_wait";
    /// <summary>Accept only when an eligible agent is available right now.</summary>
    public const string AgentAvailable = "agent_available";

    public static bool IsValid(string? mode) => mode is QueueCount or QueueWait or AgentAvailable;
}
