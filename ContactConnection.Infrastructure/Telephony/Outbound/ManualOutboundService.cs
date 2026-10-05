using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Telephony.Outbound;

/// <summary>See <see cref="IManualOutboundService"/>.</summary>
public class ManualOutboundService(ScopedTenantDbContextFactory factory, TenantContext tenantContext) : IManualOutboundService
{
    private TenantDbContext? _db;
    private TenantDbContext Db => _db ??= factory.Create();

    private string? TenantDefaultCallerId => tenantContext.Current?.Settings.DefaultOutboundCallerId;

    public async Task<OutboundDialOptions> GetOptionsAsync(Guid agentId, bool canDirectDial, CancellationToken ct = default)
    {
        var campaigns = await AssignedCampaignsAsync(agentId, null, ct);
        var clients = campaigns
            .GroupBy(c => c.ClientId)
            .Select(g => new OutboundClientOption(
                g.Key,
                g.First().Client?.Name ?? "Client",
                g.OrderBy(c => c.Name).Select(c =>
                {
                    var choice = OutboundCallerId.Resolve(c, TenantDefaultCallerId);
                    return new OutboundCampaignOption(c.Id, c.Name, choice.CallerId, choice.IsTenantDefault,
                        c.EffectiveOutboundHoursStart.ToString("HH:mm"), c.EffectiveOutboundHoursEnd.ToString("HH:mm"));
                }).ToList()))
            .OrderBy(c => c.Name)
            .ToList();
        var direct = canDirectDial ? OutboundCallerId.Resolve(null, TenantDefaultCallerId).CallerId : null;
        return new OutboundDialOptions(clients, canDirectDial, direct);
    }

    public async Task<OutboundDialDecision> PrepareAsync(OutboundDialRequest request, DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        var tenant = tenantContext.Current ?? throw new InvalidOperationException("No tenant.");
        Campaign? campaign = null;
        if (request.CampaignId is { } campaignId)
        {
            campaign = (await AssignedCampaignsAsync(request.AgentId, campaignId, ct)).FirstOrDefault();
            if (campaign is null) return Denied("You're not assigned to that manual outbound campaign.");
        }
        else if (!request.CanDirectDial)
            return Denied("Direct dial isn't enabled for your role — choose a client and campaign.");

        var number = OutboundNumber.ToE164(request.Number);
        if (number is null) return Denied("Enter a 10-digit US phone number.");

        var callerId = OutboundCallerId.Resolve(campaign, TenantDefaultCallerId).CallerId;
        if (callerId is null)
            return await BlockAsync(request, campaign, number, null, "No outbound caller ID is set for this campaign or the tenant.", null, null, ct);

        // Calling hours, in the callee's local time — the best time zone we know for this number.
        var (zones, source) = await CalleeTimeZonesAsync(number, tenant.Timezone, ct);
        var start = campaign?.EffectiveOutboundHoursStart ?? Campaign.DefaultOutboundHoursStart;
        var end = campaign?.EffectiveOutboundHoursEnd ?? Campaign.DefaultOutboundHoursEnd;
        var verdict = OutboundCallingWindow.Check(start, end, zones, nowUtc);
        var zoneText = string.Join(", ", zones);
        if (!verdict.Allowed)
            return await BlockAsync(request, campaign, number, callerId, verdict.Reason!, zoneText, source, ct);

        // Allowed: the call record (client + campaign) and the agent's interaction, so scripts, commerce and reports attach.
        var record = CallRecord.CreateManualOutbound(tenant.Id, request.AgentId, number, callerId, campaign?.ClientId, campaign?.Id);
        var interaction = record.AddInteraction(InteractionType.OutboundFollowUp);
        interaction.AssignTo(request.AgentId, campaign?.Id ?? Guid.Empty);
        var attempt = OutboundDialAttempt.Create(request.AgentId, campaign?.Id, number, callerId, OutboundDialResult.Placed,
            null, zoneText, source, record.Id);
        Db.CallRecords.Add(record);
        Db.CallInteractions.Add(interaction);
        Db.OutboundDialAttempts.Add(attempt);
        await Db.SaveChangesAsync(ct);

        return new OutboundDialDecision(true, null, number, callerId, campaign?.Id, campaign?.Name, record.Id, attempt.Id,
            campaign?.AfterCallWorkSeconds ?? 0);
    }

    public async Task FailAsync(OutboundDialDecision decision, string reason, CancellationToken ct = default)
    {
        if (decision.AttemptId is { } attemptId && await Db.OutboundDialAttempts.FindAsync([attemptId], ct) is { } attempt)
            attempt.MarkFailed(reason.Length > 500 ? reason[..500] : reason);
        if (decision.CallRecordId is { } recordId
            && await Db.CallRecords.Include(r => r.Interactions).FirstOrDefaultAsync(r => r.Id == recordId, ct) is { CallEndAt: null } record)
            record.Disconnect();
        await Db.SaveChangesAsync(ct);
    }

    private static OutboundDialDecision Denied(string error) => new(false, error, null, null, null, null, null, null, 0);

    private async Task<OutboundDialDecision> BlockAsync(OutboundDialRequest request, Campaign? campaign, string number,
        string? callerId, string reason, string? zones, string? source, CancellationToken ct)
    {
        var attempt = OutboundDialAttempt.Create(request.AgentId, campaign?.Id, number, callerId, OutboundDialResult.Blocked,
            reason, zones, source);
        Db.OutboundDialAttempts.Add(attempt);
        await Db.SaveChangesAsync(ct);
        return new OutboundDialDecision(false, reason, number, callerId, campaign?.Id, campaign?.Name, null, attempt.Id, 0);
    }

    /// <summary>Active manual outbound campaigns the agent is assigned to — directly, or through a group they're in and
    /// not excluded from. <paramref name="onlyId"/> narrows to one campaign.</summary>
    private Task<List<Campaign>> AssignedCampaignsAsync(Guid agentId, Guid? onlyId, CancellationToken ct)
    {
        var groupIds = Db.AgentGroupMembers.Where(m => m.AgentId == agentId).Select(m => m.GroupId);
        var q = Db.Campaigns.Include(c => c.Client).Where(c =>
            c.Direction == CampaignDirection.Outbound && c.DialMode == CampaignDialMode.Manual && c.Status == CampaignStatus.Active
            && (Db.AgentCampaignAssignments.Any(a => a.CampaignId == c.Id && a.AgentId == agentId && a.IsActive)
                || Db.GroupCampaignAssignments.Any(g => g.CampaignId == c.Id && g.IsActive && groupIds.Contains(g.GroupId)
                    && !Db.AgentGroupMemberCampaignExclusions.Any(x =>
                        x.GroupId == g.GroupId && x.AgentId == agentId && x.CampaignId == c.Id))));
        if (onlyId is { } id) q = q.Where(c => c.Id == id);
        return q.ToListAsync(ct);
    }

    /// <summary>
    /// The callee's time zone(s): the address state on this number's most recent prior call (strictest across a split
    /// state), else the tenant's time zone. Slice B adds the area-code / ZIP databases ahead of the tenant fallback.
    /// </summary>
    private async Task<(IReadOnlyList<string> Zones, string Source)> CalleeTimeZonesAsync(
        string e164, string tenantTimeZone, CancellationToken ct)
    {
        var ten = e164[2..];
        var like = $"%{ten}";
        var prior = await Db.CallRecords.AsNoTracking()
            .Where(r => r.Addresses != null
                        && (EF.Functions.Like(r.CallerId ?? "", like) || EF.Functions.Like(r.Phone ?? "", like)
                            || EF.Functions.Like(r.BillingPhone ?? "", like) || EF.Functions.Like(r.ShippingPhone ?? "", like)))
            .OrderByDescending(r => r.CreatedAt)
            .Take(5)
            .Select(r => r.Addresses)
            .ToListAsync(ct);
        foreach (var addresses in prior)
        {
            var state = addresses?.Billing?.State ?? addresses?.Shipping?.State;
            if (StateTimeZones.For(state) is { } zones) return (zones, $"prior_call_state:{state!.Trim().ToUpperInvariant()}");
        }
        return ([string.IsNullOrWhiteSpace(tenantTimeZone) ? "America/New_York" : tenantTimeZone], "tenant_default");
    }
}
