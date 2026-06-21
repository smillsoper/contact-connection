using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

public class CheckAgentAvailabilityNodeHandler : ITelephonyNodeHandler
{
    public string NodeType => "tf_check_agent_availability";

    private readonly ITenantDbContextFactory _factory;

    public CheckAgentAvailabilityNodeHandler(ITenantDbContextFactory factory) => _factory = factory;

    public async Task<TelephonyNodeResult> ExecuteAsync(
        JsonObject node, TelephonyFlowContext ctx, CancellationToken ct = default)
    {
        // Optional override: node can specify a different campaign to check
        var campaignIdOverride = node["campaignId"]?.GetValue<string>() is { Length: > 0 } s
            && Guid.TryParse(s, out var g) ? g : (Guid?)null;

        var campaignId = campaignIdOverride ?? ctx.CampaignId;

        await using var db = _factory.Create(ctx.TenantSchemaName);

        // Phase 1: any active direct assignment or any active group assignment = available.
        // Phase 2 will add presence-based filtering (online/busy/etc. via Redis).
        var hasDirect = await db.AgentCampaignAssignments
            .AnyAsync(a => a.CampaignId == campaignId && a.IsActive, ct);

        bool hasGroup = false;
        if (!hasDirect)
        {
            var groupIds = await db.GroupCampaignAssignments
                .Where(g => g.CampaignId == campaignId)
                .Select(g => g.GroupId)
                .ToListAsync(ct);

            if (groupIds.Count > 0)
                hasGroup = await db.AgentGroupMembers.AnyAsync(m => groupIds.Contains(m.GroupId), ct);
        }

        var available = hasDirect || hasGroup;
        var transition = available ? "available" : "unavailable";
        var nextNodeId = node["transitions"]?[transition]?.GetValue<string>()
                      ?? node["transitions"]?["default"]?.GetValue<string>();

        return new TelephonyNodeResult(nextNodeId, transition);
    }
}
