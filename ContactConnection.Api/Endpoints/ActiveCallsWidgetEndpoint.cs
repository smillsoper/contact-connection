using ContactConnection.Api.Telephony;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// Active Calls dashboard widget (S180, Sprint 2 item 1): every call an agent is on right now — inbound / callback calls
/// bridged to an agent (telephony sessions) and manual outbound calls in progress (call records; they have no session).
/// Each row says who's talking to whom, since when, and the call's state (on hold, secure capture, recording), plus whether
/// the supervisor tools (Monitor / Coach / Barge / Take over) can reach it. The widget refetches on the supervisor pushes
/// (call-state, agent-state, agent-sessions) — never polls.
/// </summary>
public static class ActiveCallsWidgetEndpoint
{
    public static IEndpointRouteBuilder MapActiveCallsWidget(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/dashboard-widgets/active-calls", Get).RequireAuthorization("ReportsView");
        return app;
    }

    private static async Task<IResult> Get(
        Guid? campaignId, Guid? clientId, Guid? groupId, string? dnis, IAgentGroupRepository agentGroups,
        ICampaignRepository campaigns, IAgentRepository agents, ITelephonyCallSessionStore sessions, IFlowEngine flowEngine,
        ScopedTenantDbContextFactory dbFactory, TenantContext tenantContext, CancellationToken ct)
    {
        if (tenantContext.Current is null) return Results.Unauthorized();
        var tenantId = tenantContext.Current.Id;

        HashSet<Guid>? scope = null;
        if (campaignId is { } cid) scope = [cid];
        else if (clientId is { } clid) scope = (await campaigns.GetAllAsync(clid, ct)).Select(c => c.Id).ToHashSet();
        // S181: the agent on the call in this group; the number the caller dialed.
        var groupAgents = await WidgetFilters.GroupAgentsAsync(groupId, agentGroups, ct);
        var dnisKeys = WidgetFilters.Dnis(dnis);

        // Inbound / callback calls with an agent on the line.
        var live = (await sessions.GetAllAsync(ct))
            .Where(s => s.TenantId == tenantId
                        && Guid.TryParse(s.Vars.GetValueOrDefault("_assigned_agent_id"), out _)
                        && !string.IsNullOrEmpty(SupervisorCallService.AgentLeg(s))
                        && s.Vars.GetValueOrDefault("_queued") != "true")
            .ToList();

        await using var db = dbFactory.Create();
        var liveRecordIds = live.Select(s => s.CallRecordId).ToList();
        var since = DateTimeOffset.UtcNow.AddHours(-12);
        var records = await db.CallRecords.AsNoTracking().Include(r => r.Interactions)
            .Where(r => liveRecordIds.Contains(r.Id)
                        // Manual outbound in progress (S179): no telephony session — the record is the source.
                        || (r.Source == CallSource.Outbound && r.AgentId != null && r.CallEndAt == null && r.DisconnectedAt == null
                            && r.CreatedAt > since && r.RunMode == CallRunMode.Production))
            .ToListAsync(ct);
        var byId = records.ToDictionary(r => r.Id);

        var campaignNames = (await campaigns.GetAllAsync(null, ct)).ToDictionary(c => c.Id, c => c.Name);
        var agentList = await agents.GetAllAsync(ct);
        var agentNames = agentList.ToDictionary(a => a.Id, a => a.FullName);

        var rows = new List<ActiveCallRow>();
        foreach (var s in live)
        {
            var agentId = Guid.Parse(s.Vars["_assigned_agent_id"]);
            byId.TryGetValue(s.CallRecordId, out var record);
            var interaction = record?.Interactions.Where(i => i.AgentId == agentId).OrderByDescending(i => i.InteractionNumber).FirstOrDefault();
            var campaign = interaction?.CampaignId is { } ic && ic != Guid.Empty ? ic : s.CampaignId;
            if (scope is not null && !scope.Contains(campaign)) continue;
            if (groupAgents is not null && !groupAgents.Contains(agentId)) continue;
            if (!Domain.ValueObjects.PhoneKey.Matches(dnisKeys, s.DestinationNumber)) continue;
            rows.Add(new ActiveCallRow(
                s.CallRecordId, s.Vars.GetValueOrDefault("_queue_callback") == "true" ? "callback" : "inbound",
                s.CallerNumber, s.DestinationNumber, campaign, campaignNames.GetValueOrDefault(campaign), agentId,
                agentNames.GetValueOrDefault(agentId), interaction?.StartedAt ?? record?.CallStartAt,
                interaction?.RoutedTierLabel,
                OnHold: s.Vars.GetValueOrDefault("_on_hold") == "true",
                SecureCapture: s.Vars.GetValueOrDefault("_sc_in_progress") == "true",
                Recording: record is { RecordingStartedAt: not null, RecordingStoppedAt: null },
                Supervisable: true, ScriptName: null, SectionName: null));
        }

        foreach (var r in records.Where(r => r.Source == CallSource.Outbound && !liveRecordIds.Contains(r.Id)))
        {
            if (scope is not null && !scope.Contains(r.CampaignId)) continue;
            if (groupAgents is not null && !groupAgents.Contains(r.AgentId!.Value)) continue;
            if (dnisKeys is not null) continue;   // a manual outbound call has no dialed-in number
            rows.Add(new ActiveCallRow(
                r.Id, "outbound", r.CallerId, r.Dnis, r.CampaignId == Guid.Empty ? null : r.CampaignId,
                r.CampaignId == Guid.Empty ? "Direct dial" : campaignNames.GetValueOrDefault(r.CampaignId),
                r.AgentId!.Value, agentNames.GetValueOrDefault(r.AgentId!.Value), r.CallStartAt, null,
                OnHold: false, SecureCapture: false, Recording: false,
                // Monitor / Take over find the agent's call through its telephony session; manual outbound has none yet.
                Supervisable: false, ScriptName: null, SectionName: null));
        }

        // The CRM script each agent has open on that call.
        if (rows.Count > 0)
        {
            // The latest-started script on the call wins (a sub-flow / second tab is where the agent is now).
            var scripts = (await flowEngine.GetLiveSessionsForAgentsAsync(rows.Select(r => r.AgentId).Distinct().ToList(), ct))
                .GroupBy(x => (x.AgentId, x.CallRecordId)).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.StartedAt).First());
            rows = rows.Select(r => scripts.TryGetValue((r.AgentId, r.CallRecordId), out var sc)
                ? r with { ScriptName = sc.FlowName, SectionName = sc.SectionName } : r).ToList();
        }

        return Results.Ok(rows.OrderBy(r => r.ConnectedAt ?? DateTimeOffset.MaxValue));
    }

    public sealed record ActiveCallRow(
        Guid CallRecordId, string Direction, string? CustomerNumber, string? OurNumber, Guid? CampaignId, string? CampaignName,
        Guid AgentId, string? AgentName, DateTimeOffset? ConnectedAt, string? TierLabel,
        bool OnHold, bool SecureCapture, bool Recording, bool Supervisable, string? ScriptName, string? SectionName);
}
