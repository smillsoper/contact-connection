namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// Manual outbound dialing (S179, Sprint 1 item 1b, Slice A). Decides everything about an agent's outbound call
/// server-side — which campaigns they may dial under, the caller ID, the callee's calling window — and records every
/// attempt. The call itself is placed by the API (it rings the agent's softphone and bridges to the customer).
/// </summary>
public interface IManualOutboundService
{
    /// <summary>What the softphone's Place call panel offers this agent, with each choice's caller ID preview.</summary>
    Task<OutboundDialOptions> GetOptionsAsync(Guid agentId, bool canDirectDial, CancellationToken ct = default);

    /// <summary>
    /// Checks the dial (permission / assignment, number, caller ID, calling hours), audits it, and — when allowed —
    /// creates the call record (client + campaign + an interaction for the agent). Blocked dials create nothing.
    /// </summary>
    Task<OutboundDialDecision> PrepareAsync(OutboundDialRequest request, DateTimeOffset nowUtc, CancellationToken ct = default);

    /// <summary>The originate didn't go through: marks the attempt failed and closes the call record.</summary>
    Task FailAsync(OutboundDialDecision decision, string reason, CancellationToken ct = default);
}

public record OutboundCampaignOption(
    Guid CampaignId, string Name, string? CallerId, bool CallerIdIsTenantDefault, string HoursStart, string HoursEnd);

public record OutboundClientOption(Guid ClientId, string Name, IReadOnlyList<OutboundCampaignOption> Campaigns);

public record OutboundDialOptions(
    IReadOnlyList<OutboundClientOption> Clients, bool CanDirectDial, string? DirectDialCallerId);

/// <summary>CampaignId null = a direct dial (needs <paramref name="CanDirectDial"/>).</summary>
public record OutboundDialRequest(Guid AgentId, bool CanDirectDial, Guid? CampaignId, string Number);

public record OutboundDialDecision(
    bool Allowed,
    string? Error,
    string? Number,
    string? CallerId,
    Guid? CampaignId,
    string? CampaignName,
    Guid? CallRecordId,
    Guid? AttemptId,
    int AfterCallWorkSeconds);
