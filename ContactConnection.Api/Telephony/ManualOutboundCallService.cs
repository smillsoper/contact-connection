using System.Text.Json;
using ContactConnection.Api.Hubs;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Api.Telephony;

/// <summary>A manual outbound call in progress, keyed by the agent's leg (see <see cref="ManualOutboundCallService"/>).</summary>
public record ManualOutboundLeg(
    Guid TenantId, string TenantSchema, Guid AgentId, Guid CallRecordId, int AfterCallWorkSeconds, bool IsCampaignDial,
    AgentStateEntry? Previous);

public record ManualOutboundResult(bool Ok, string? Error, object? Data = null);

/// <summary>
/// Places an agent's manual outbound call server-side (S179, Sprint 1 item 1b). <see cref="IManualOutboundService"/>
/// decides whether the dial is allowed and with which caller ID; this rings the agent's softphone (auto-answer) and
/// bridges it to the customer. The agent leg is the handle: EslBackgroundService sees its CHANNEL_BRIDGE (customer
/// answered → <see cref="AnsweredAsync"/>) and its hangup (→ <see cref="EndAsync"/>: record closed, after-call work).
/// The agent is On Call — out of queue delivery — from the moment the dial starts.
/// </summary>
public sealed class ManualOutboundCallService(
    IManualOutboundService outbound,
    IEslCommanderFactory eslFactory,
    ITelephonyCallSessionStore telephonySessions,
    IAgentRegistrationStore registrations,
    IAgentStateStore agentStates,
    IHubContext<FlowHub, IFlowHubClient> hub,
    TenantContext tenantContext,
    IConfiguration config,
    ILogger<ManualOutboundCallService> logger)
{
    private static readonly TimeSpan LegTtl = TimeSpan.FromHours(8);
    public const string OutboundLabel = "Outbound Call";
    public static string LegKey(string legUuid) => $"manual_outbound_leg:{legUuid}";

    public async Task<ManualOutboundResult> DialAsync(Agent agent, bool canDirectDial, Guid? campaignId, string number, CancellationToken ct)
    {
        var tenant = tenantContext.Current!;
        if (string.IsNullOrWhiteSpace(agent.SipExtension) || registrations.Get(tenant.Id, agent.SipExtension) is null)
            return new(false, "Your softphone isn't registered.");
        var previous = await agentStates.GetAsync(tenant.Id, agent.Id, ct);
        if (previous?.Code == AgentStateCodes.OnCall)
            return new(false, "You're already on a call.");

        var decision = await outbound.PrepareAsync(new OutboundDialRequest(agent.Id, canDirectDial, campaignId, number), DateTimeOffset.UtcNow, ct);
        if (!decision.Allowed) return new(false, decision.Error);

        // Out of queue delivery before the phone rings.
        var busy = new AgentStateEntry(AgentStateCodes.OnCall, OutboundLabel, null, DateTimeOffset.UtcNow);
        await agentStates.SetAsync(tenant.Id, agent.Id, tenant.SchemaName, busy, ct);
        await hub.Clients.Group($"agent:{agent.Id}").ReceiveAgentStateChange(busy.Code, busy.Label, null);

        // Registered before dialing: the call can end before originate even returns.
        var legUuid = Guid.NewGuid().ToString();
        var leg = new ManualOutboundLeg(tenant.Id, tenant.SchemaName, agent.Id, decision.CallRecordId!.Value,
            decision.AfterCallWorkSeconds, decision.CampaignId is not null, previous);
        await telephonySessions.SetKeyAsync(LegKey(legUuid), JsonSerializer.Serialize(leg), LegTtl, ct);

        // Arm the softphone to answer this INVITE itself, before it arrives.
        await hub.Clients.Group($"agent:{agent.Id}").ReceiveOutboundConnecting(decision.CallRecordId.Value.ToString(), decision.Number!);

        string? error;
        try
        {
            await using var esl = await eslFactory.CreateAsync(ct);
            error = await esl.OriginateManualOutboundAsync(legUuid, agent.SipExtension!, tenant.Subdomain,
                decision.Number!, decision.CallerId!, decision.CallRecordId.Value, Gateway, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Manual outbound: originate failed for {Agent}", agent.Id);
            error = "The phone system isn't reachable right now.";
        }

        if (error is not null)
        {
            await telephonySessions.DeleteKeyAsync(LegKey(legUuid), ct);
            await outbound.FailAsync(decision, error, ct);
            await RestoreAsync(agentStates, hub, tenant.Id, tenant.SchemaName, agent.Id, previous, ct);
            return new(false, error);
        }

        return new(true, null, new
        {
            callRecordId = decision.CallRecordId,
            legUuid,
            number = decision.Number,
            callerId = decision.CallerId,
            campaignId = decision.CampaignId,
            campaignName = decision.CampaignName,
        });
    }

    private string Gateway => config["FreeSWITCH:DefaultGateway"] ?? "signalwire";

    /// <summary>The agent leg bridged — the customer answered.</summary>
    public static async Task AnsweredAsync(ManualOutboundLeg leg, IHubContext<FlowHub, IFlowHubClient> hub) =>
        await hub.Clients.Group($"agent:{leg.AgentId}").ReceiveOutboundAnswered(leg.CallRecordId.ToString());

    /// <summary>
    /// The agent leg hung up — the call is over. Closes the record, gives the agent back the status they had before the
    /// dial (Stephen, S179: never force an agent into Available) and tells the softphone why it ended.
    /// </summary>
    public static async Task EndAsync(
        ManualOutboundLeg leg, string? farEndCause, ITenantDbContextFactory dbFactory, IAgentStateStore states,
        IHubContext<FlowHub, IFlowHubClient> hub, CancellationToken ct)
    {
        await using (var db = dbFactory.Create(leg.TenantSchema))
        {
            var record = await db.CallRecords.Include(r => r.Interactions).FirstOrDefaultAsync(r => r.Id == leg.CallRecordId, ct);
            if (record is { CallEndAt: null })
            {
                record.Disconnect();
                await db.SaveChangesAsync(ct);
            }
        }

        await RestoreAsync(states, hub, leg.TenantId, leg.TenantSchema, leg.AgentId, leg.Previous, ct);

        await hub.Clients.Group($"agent:{leg.AgentId}").ReceiveOutboundEnded(leg.CallRecordId.ToString(), Outcome(farEndCause));
    }

    /// <summary>A plain outcome for the agent from the far end's hangup cause; null for a normal hang-up.</summary>
    public static string? Outcome(string? cause) => cause switch
    {
        "USER_BUSY" => "Busy",
        "NO_ANSWER" or "NO_USER_RESPONSE" or "ALLOTTED_TIMEOUT" => "No answer",
        "CALL_REJECTED" => "Call rejected",
        "UNALLOCATED_NUMBER" or "INVALID_NUMBER_FORMAT" or "NO_ROUTE_DESTINATION" => "Number not in service",
        "NORMAL_TEMPORARY_FAILURE" or "NETWORK_OUT_OF_ORDER" or "RECOVERY_ON_TIMER_EXPIRE" or "DESTINATION_OUT_OF_ORDER"
            or "GATEWAY_DOWN" or "SERVICE_UNAVAILABLE" => "The call couldn't be completed",
        _ => null,
    };

    private static async Task RestoreAsync(IAgentStateStore states, IHubContext<FlowHub, IFlowHubClient> hub,
        Guid tenantId, string schema, Guid agentId, AgentStateEntry? previous, CancellationToken ct)
    {
        var current = await states.GetAsync(tenantId, agentId, ct);
        if (current is not null && (current.Code != AgentStateCodes.OnCall || current.Label != OutboundLabel)) return;   // changed meanwhile
        // The status from before the dial. ACW is the exception: its countdown belonged to the earlier call and has gone,
        // so the agent comes back Unavailable (never pushed into Available behind their back).
        var back = previous is null || previous.Code is AgentStateCodes.OnCall or AgentStateCodes.Acw
            ? new AgentStateEntry(AgentStateCodes.Unavailable, "Unavailable", null, DateTimeOffset.UtcNow)
            : previous with { SetAt = DateTimeOffset.UtcNow };
        await states.SetAsync(tenantId, agentId, schema, back, ct);
        await hub.Clients.Group($"agent:{agentId}").ReceiveAgentStateChange(back.Code, back.Label, null);
    }
}
