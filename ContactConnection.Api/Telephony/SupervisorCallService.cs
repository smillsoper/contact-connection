using System.Text.Json;
using ContactConnection.Api.Hubs;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using Microsoft.AspNetCore.SignalR;

namespace ContactConnection.Api.Telephony;

/// <summary>Supervisor listen-in modes — eavesdrop DTMF digits in parentheses.</summary>
public static class MonitorMode
{
    public const string Listen = "listen";   // (0) nobody hears the supervisor
    public const string Coach  = "coach";    // (2) only the agent hears the supervisor
    public const string Barge  = "barge";    // (3) three-way: caller and agent both hear

    public static string? Digit(string mode) => mode switch { Listen => "0", Coach => "2", Barge => "3", _ => null };
}

public record SupervisorMonitorState(
    Guid SupervisorId, Guid AgentId, string AgentName, Guid CallRecordId, string LegUuid, string Mode, DateTimeOffset StartedAt);

public record SupervisorResult(bool Ok, string? Error, object? Data = null);

/// <summary>A running supervisor → agent internal call, keyed by the supervisor's leg. The previous
/// states are restored when it ends (if nothing else changed them meanwhile).</summary>
public record IntercomState(
    Guid TenantId, string TenantSchema, Guid SupervisorId, Guid AgentId,
    AgentStateEntry? SupervisorPrevious, AgentStateEntry? AgentPrevious);

/// <summary>
/// Supervisor tools on a live call (S167) — Monitor / Coach / Barge / Take Over.
///
/// Audio runs through the supervisor's own agent-portal softphone: the server rings it (auto-answer)
/// straight into FreeSWITCH's eavesdrop on the agent's leg. One connection covers Monitor, Coach and
/// Barge — switching is a uuid_recv_dtmf on the supervisor's leg, so nobody hears a tone. Monitoring
/// is silent to the agent (QA); Coach/Barge are audible by nature. Recording is untouched.
///
/// Take Over: the supervisor's portal (popped in a new window) calls <see cref="TakeOverAsync"/> once
/// its softphone is registered — a new supervisor leg is originated and parked, the caller is
/// re-bridged onto it, the agent's leg is dropped, and the CRM script moves to the supervisor. The
/// call (and its commission) stays attributed to the original agent; the take-over is audited.
/// ESL guards: _takeover_in_progress keeps the re-bridge's CHANNEL_UNBRIDGE from hanging up the caller;
/// _takeover_old_leg keeps the dropped agent leg's CHANNEL_HANGUP from ending the call.
/// </summary>
public sealed class SupervisorCallService(
    IEslCommanderFactory eslFactory,
    ITelephonyCallSessionStore telephonySessions,
    IAgentRepository agents,
    IAgentRegistrationStore registrations,
    IAgentStateStore agentStates,
    ICampaignRepository campaigns,
    IFlowSessionRepository flowSessions,
    IFlowEngine flowEngine,
    ICallRecordAuditRepository audit,
    IHubContext<FlowHub, IFlowHubClient> hub,
    TenantContext tenantContext,
    ILogger<SupervisorCallService> logger)
{
    private static readonly TimeSpan MonitorTtl = TimeSpan.FromHours(4);
    private static string MonitorKey(Guid supervisorId) => $"monitor:{supervisorId}";
    public static string MonitorLegKey(string legUuid) => $"monitor_leg:{legUuid}";

    /// <summary>The live telephony call an agent is on (bridged), if any.</summary>
    public async Task<TelephonyCallSession?> FindLiveCallAsync(Guid agentId, CancellationToken ct)
    {
        var tenantId = tenantContext.Current!.Id;
        return (await telephonySessions.GetAllAsync(ct)).FirstOrDefault(s =>
            s.TenantId == tenantId
            && s.Vars.GetValueOrDefault("_assigned_agent_id") == agentId.ToString()
            && !string.IsNullOrEmpty(AgentLeg(s)));
    }

    public static string? AgentLeg(TelephonyCallSession s) =>
        s.Vars.GetValueOrDefault("_bridged_peer_uuid") is { Length: > 0 } peer ? peer : s.Vars.GetValueOrDefault("_agent_uuid");

    public async Task<SupervisorMonitorState?> GetMonitorAsync(Guid supervisorId, CancellationToken ct)
    {
        var json = await telephonySessions.GetKeyAsync(MonitorKey(supervisorId), ct);
        return json is null ? null : JsonSerializer.Deserialize<SupervisorMonitorState>(json);
    }

    // ── Monitor / Coach / Barge ─────────────────────────────────────────────

    public async Task<SupervisorResult> StartMonitorAsync(Agent supervisor, Guid agentId, string mode, CancellationToken ct)
    {
        if (MonitorMode.Digit(mode) is null) return new(false, "Unknown mode.");
        if (agentId == supervisor.Id) return new(false, "You can't monitor your own call.");
        var target = await agents.GetByIdAsync(agentId, ct);
        if (target is null) return new(false, "Agent not found.");
        var call = await FindLiveCallAsync(agentId, ct);
        if (call is null) return new(false, $"{target.FullName} isn't on a live call right now.");
        if (!SoftphoneReady(supervisor, out var why)) return new(false, why);

        // One listen-in at a time: end any previous one first.
        await StopMonitorAsync(supervisor.Id, ct);

        // Arm the supervisor's softphone to auto-answer this INVITE as a monitor session.
        await hub.Clients.Group($"agent:{supervisor.Id}").ReceiveSupervisorConnecting("monitor", target.FullName, mode);

        await using var esl = await eslFactory.CreateAsync(ct);
        var (legUuid, error) = await esl.OriginateEavesdropAsync(
            supervisor.SipExtension!, tenantContext.Current!.Subdomain, AgentLeg(call)!, $"Monitor {target.FirstName}", ct);
        if (legUuid is null)
        {
            logger.LogWarning("Monitor: couldn't reach supervisor {Supervisor}'s softphone: {Error}", supervisor.Id, error);
            await hub.Clients.Group($"agent:{supervisor.Id}").ReceiveMonitorEnded();
            return new(false, "Your softphone didn't answer — make sure your agent portal is open and registered.");
        }

        if (mode != MonitorMode.Listen)
        {
            await Task.Delay(700, ct);   // eavesdrop must be running before it sees the digit
            await esl.RecvDtmfAsync(legUuid, MonitorMode.Digit(mode)!, ct);
        }

        var state = new SupervisorMonitorState(supervisor.Id, agentId, target.FullName, call.CallRecordId, legUuid, mode, DateTimeOffset.UtcNow);
        await telephonySessions.SetKeyAsync(MonitorKey(supervisor.Id), JsonSerializer.Serialize(state), MonitorTtl, ct);
        await telephonySessions.SetKeyAsync(MonitorLegKey(legUuid), supervisor.Id.ToString(), MonitorTtl, ct);
        await Audit(call.CallRecordId, supervisor, $"Supervisor {Describe(mode)} started ({target.FullName})", ct);
        return new(true, null, state);
    }

    public async Task<SupervisorResult> SetMonitorModeAsync(Agent supervisor, string mode, CancellationToken ct)
    {
        var digit = MonitorMode.Digit(mode);
        if (digit is null) return new(false, "Unknown mode.");
        var state = await GetMonitorAsync(supervisor.Id, ct);
        if (state is null) return new(false, "You're not monitoring a call.");

        await using var esl = await eslFactory.CreateAsync(ct);
        await esl.RecvDtmfAsync(state.LegUuid, digit, ct);
        state = state with { Mode = mode };
        await telephonySessions.SetKeyAsync(MonitorKey(supervisor.Id), JsonSerializer.Serialize(state), MonitorTtl, ct);
        await hub.Clients.Group($"agent:{supervisor.Id}").ReceiveSupervisorConnecting("mode", state.AgentName, mode);
        await Audit(state.CallRecordId, supervisor, $"Supervisor switched to {Describe(mode)} ({state.AgentName})", ct);
        return new(true, null, state);
    }

    public async Task StopMonitorAsync(Guid supervisorId, CancellationToken ct)
    {
        var state = await GetMonitorAsync(supervisorId, ct);
        if (state is null) return;
        await telephonySessions.DeleteKeyAsync(MonitorKey(supervisorId), ct);
        await telephonySessions.DeleteKeyAsync(MonitorLegKey(state.LegUuid), ct);
        try
        {
            await using var esl = await eslFactory.CreateAsync(ct);
            await esl.HangupChannelAsync(state.LegUuid, ct);
        }
        catch (Exception ex) { logger.LogDebug(ex, "Monitor leg {Leg} already gone", state.LegUuid); }
        await hub.Clients.Group($"agent:{supervisorId}").ReceiveMonitorEnded();
    }

    // ── Call an agent (internal, off-call) ──────────────────────────────────

    public const string IntercomLabel = "On Call - Supervisor call";
    public static string IntercomLegKey(string legUuid) => $"intercom_leg:{legUuid}";

    /// <summary>Supervisor → agent internal call (QA review, training): not a customer call — no call
    /// record, no call screen. Both sides are held out of queue delivery while it's up and get their
    /// previous status back when it ends (EslBackgroundService, on the supervisor leg's hangup).</summary>
    public async Task<SupervisorResult> CallAgentAsync(Agent supervisor, Guid agentId, CancellationToken ct)
    {
        if (agentId == supervisor.Id) return new(false, "You can't call yourself.");
        var target = await agents.GetByIdAsync(agentId, ct);
        if (target is null) return new(false, "Agent not found.");
        if (!SoftphoneReady(supervisor, out var why)) return new(false, why);
        var tenant = tenantContext.Current!;
        if (string.IsNullOrWhiteSpace(target.SipExtension) || registrations.Get(tenant.Id, target.SipExtension) is null)
            return new(false, $"{target.FullName}'s softphone isn't registered.");
        if (await FindLiveCallAsync(agentId, ct) is not null)
            return new(false, $"{target.FullName} is on a customer call — use Monitor or Coach instead.");

        var supervisorPrev = await agentStates.GetAsync(tenant.Id, supervisor.Id, ct);
        var agentPrev = await agentStates.GetAsync(tenant.Id, agentId, ct);

        // Arm both softphones: the supervisor's auto-answers, the agent's rings with Answer / Decline.
        await hub.Clients.Group($"agent:{supervisor.Id}").ReceiveSupervisorConnecting("intercom", target.FullName, "caller");
        await hub.Clients.Group($"agent:{agentId}").ReceiveSupervisorConnecting("intercom-incoming", supervisor.FullName, "callee");

        // Out of queue delivery while the call is up.
        var busy = new AgentStateEntry(AgentStateCodes.OnCall, IntercomLabel, null, DateTimeOffset.UtcNow);
        await agentStates.SetAsync(tenant.Id, supervisor.Id, tenant.SchemaName, busy, ct);
        await agentStates.SetAsync(tenant.Id, agentId, tenant.SchemaName, busy, ct);
        await hub.Clients.Group($"agent:{supervisor.Id}").ReceiveAgentStateChange(busy.Code, busy.Label, null);
        await hub.Clients.Group($"agent:{agentId}").ReceiveAgentStateChange(busy.Code, busy.Label, null);

        // Registered before dialing: a decline can hang the leg up before originate even returns,
        // and the hangup handler needs this to give both their status back.
        var legUuid = Guid.NewGuid().ToString();
        var state = new IntercomState(tenant.Id, tenant.SchemaName, supervisor.Id, agentId, supervisorPrev, agentPrev);
        await telephonySessions.SetKeyAsync(IntercomLegKey(legUuid), JsonSerializer.Serialize(state), MonitorTtl, ct);

        await using var esl = await eslFactory.CreateAsync(ct);
        var (upLeg, error) = await esl.OriginateIntercomAsync(
            legUuid, supervisor.SipExtension!, target.SipExtension!, tenant.Subdomain, supervisor.FullName, target.FullName, ct);
        if (upLeg is null)
        {
            logger.LogWarning("Call agent: originate failed: {Error}", error);
            // The hangup handler may already have done this; EndIntercomAsync is idempotent.
            if (await telephonySessions.GetKeyAsync(IntercomLegKey(legUuid), ct) is not null)
            {
                await telephonySessions.DeleteKeyAsync(IntercomLegKey(legUuid), ct);
                await EndIntercomAsync(state, agentStates, hub, ct);
            }
            return new(false, "Couldn't connect the call — make sure both softphones are registered.");
        }
        logger.LogInformation("Supervisor {Supervisor} calling agent {Agent} (leg {Leg})", supervisor.Id, agentId, legUuid);
        return new(true, null, new { legUuid, agentName = target.FullName });
    }

    /// <summary>Restores both sides' previous status (only where it's still the internal-call one —
    /// a lock or a newer choice wins) and tells both screens the call is over.</summary>
    public static async Task EndIntercomAsync(IntercomState state, IAgentStateStore states,
        IHubContext<FlowHub, IFlowHubClient> hub, CancellationToken ct)
    {
        foreach (var (who, previous) in new[] { (state.SupervisorId, state.SupervisorPrevious), (state.AgentId, state.AgentPrevious) })
        {
            var current = await states.GetAsync(state.TenantId, who, ct);
            if (current?.Label != IntercomLabel) continue;
            var restore = previous is null || previous.Code == AgentStateCodes.OnCall
                ? new AgentStateEntry(AgentStateCodes.Unavailable, "Unavailable", null, DateTimeOffset.UtcNow)
                : previous with { SetAt = DateTimeOffset.UtcNow };
            await states.SetAsync(state.TenantId, who, state.TenantSchema, restore, ct);
            await hub.Clients.Group($"agent:{who}").ReceiveAgentStateChange(restore.Code, restore.Label, null);
        }
        await hub.Clients.Group($"agent:{state.SupervisorId}").ReceiveIntercomEnded();
        await hub.Clients.Group($"agent:{state.AgentId}").ReceiveIntercomEnded();
    }

    // ── Take Over ───────────────────────────────────────────────────────────

    public async Task<SupervisorResult> TakeOverAsync(Agent supervisor, Guid agentId, CancellationToken ct)
    {
        if (agentId == supervisor.Id) return new(false, "That's already your call.");
        var target = await agents.GetByIdAsync(agentId, ct);
        if (target is null) return new(false, "Agent not found.");
        var tenant = tenantContext.Current!;

        var call = await FindLiveCallAsync(agentId, ct);
        var openScripts = (await flowEngine.GetLiveSessionsForAgentsAsync([agentId], ct)).ToList();
        if (call is null && openScripts.Count == 0)
            return new(false, $"{target.FullName} has no live call or open script to take over.");
        // A script-only take-over (no phone call — e.g. a preview) needs no softphone.
        if (call is not null && !SoftphoneReady(supervisor, out var why)) return new(false, why);

        // Taking over replaces any listen-in the supervisor had going.
        await StopMonitorAsync(supervisor.Id, ct);

        var callRecordId = call?.CallRecordId ?? openScripts[0].CallRecordId;
        var message = $"Call taken over by {supervisor.FullName}";
        string? phoneError = null;

        if (call is not null)
        {
            var oldLeg = AgentLeg(call)!;
            await using var esl = await eslFactory.CreateAsync(ct);

            // Guards first — the re-bridge and the dropped leg must not read as the call ending.
            call.Vars["_takeover_in_progress"] = "true";
            call.Vars["_takeover_old_leg"] = oldLeg;
            await telephonySessions.SaveAsync(call, ct);

            // The supervisor's softphone treats the next INVITE like a delivered call (auto-answer).
            await hub.Clients.Group($"agent:{supervisor.Id}").ReceiveAutoConnecting(
                call.CallRecordId.ToString(), call.CallerNumber, call.CallerNumber, call.DestinationNumber,
                call.CampaignId.ToString(), null);

            var (newLeg, error) = await esl.OriginateAndParkAsync(
                supervisor.SipExtension!, tenant.Subdomain, call.CallerNumber, ct);
            if (newLeg is null)
            {
                call.Vars.Remove("_takeover_in_progress");
                call.Vars.Remove("_takeover_old_leg");
                await telephonySessions.SaveAsync(call, ct);
                await hub.Clients.Group($"agent:{supervisor.Id}").ReceiveAutoConnectFailed(call.CallRecordId.ToString());
                logger.LogWarning("Take-over: supervisor softphone unreachable: {Error}", error);
                return new(false, "Your softphone didn't answer — the call stays with the agent.");
            }

            call.Vars["_takeover_new_leg"] = newLeg;
            call.Vars["_assigned_agent_id"] = supervisor.Id.ToString();
            call.Vars["_agent_uuid"] = newLeg;
            call.Vars["_bridged_peer_uuid"] = newLeg;
            call.Vars["_taken_over_from_agent_id"] = agentId.ToString();
            await telephonySessions.SaveAsync(call, ct);

            // The old leg must end, not re-park, once it's unbridged.
            await esl.SetChannelVarAsync(oldLeg, "park_after_bridge", "false", ct);
            await esl.BridgeChannelsAsync(call.ChannelUuid, newLeg, ct);
            await esl.HangupChannelAsync(oldLeg, ct);

            // Agent → after-call work; supervisor → on the call.
            var campaign = call.CampaignId == Guid.Empty ? null : await campaigns.GetByIdAsync(call.CampaignId, ct);
            await AfterCallWork.StartAsync(agentStates, hub, tenant.Id, agentId, tenant.SchemaName,
                campaign?.AfterCallWorkSeconds ?? 30, ct);
            await agentStates.SetAsync(tenant.Id, supervisor.Id, tenant.SchemaName,
                new AgentStateEntry(AgentStateCodes.OnCall, "On Call", null, DateTimeOffset.UtcNow), ct);
            await hub.Clients.Group($"agent:{supervisor.Id}").ReceiveAgentStateChange(AgentStateCodes.OnCall, "On Call", null);
            phoneError = null;
        }

        // The CRM script(s) on this call follow the supervisor.
        var moved = 0;
        foreach (var script in openScripts.Where(s => s.CallRecordId == callRecordId))
        {
            var node = await flowEngine.TakeOverSessionAsync(script.SessionId, supervisor.Id, message, ct);
            if (node is null) continue;
            moved++;
            await hub.Clients.Group($"agent:{supervisor.Id}").ReceiveScriptPop(
                JsonSerializer.Serialize(node, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        }

        await Audit(callRecordId, supervisor,
            $"Call taken over from {target.FullName} by {supervisor.FullName}" +
            (call is null ? " (script only)" : "") + (moved > 0 ? $" · {moved} script{(moved == 1 ? "" : "s")} moved" : ""), ct);
        return new(true, phoneError, new { callRecordId, phone = call is not null, scriptsMoved = moved });
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private bool SoftphoneReady(Agent supervisor, out string why)
    {
        why = "";
        if (string.IsNullOrWhiteSpace(supervisor.SipExtension))
        {
            why = "You don't have a softphone extension.";
            return false;
        }
        if (registrations.Get(tenantContext.Current!.Id, supervisor.SipExtension) is null)
        {
            why = "Open your agent portal first — your softphone must be registered to listen in or take over.";
            return false;
        }
        return true;
    }

    private static string Describe(string mode) => mode switch
    {
        MonitorMode.Coach => "coaching",
        MonitorMode.Barge => "barge-in",
        _ => "monitoring",
    };

    private Task Audit(Guid callRecordId, Agent supervisor, string summary, CancellationToken ct) =>
        audit.AddAsync(CallRecordAuditEntry.Create(
            callRecordId, CallAuditAction.Supervisor, summary, "{}", supervisor.Id, supervisor.FullName), ct);
}
