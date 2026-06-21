using System.Text.Json;
using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Telephony;

public class TelephonyFlowEngine : ITelephonyFlowEngine
{
    private const int MaxIterations = 50;

    private readonly ITenantDbContextFactory _factory;
    private readonly IReadOnlyDictionary<string, ITelephonyNodeHandler> _handlers;
    private readonly ILogger<TelephonyFlowEngine> _logger;

    public TelephonyFlowEngine(
        ITenantDbContextFactory factory,
        IEnumerable<ITelephonyNodeHandler> handlers,
        ILogger<TelephonyFlowEngine> logger)
    {
        _factory = factory;
        _handlers = handlers.ToDictionary(h => h.NodeType, StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    public async Task ExecuteAsync(TelephonyFlowContext ctx, CancellationToken ct = default)
    {
        await using var db = _factory.Create(ctx.TenantSchemaName);

        // Load campaign to get the telephony flow id
        var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == ctx.CampaignId, ct);
        if (campaign?.FlowId is null)
        {
            _logger.LogWarning(
                "TelephonyFlowEngine: campaign {CampaignId} has no telephony flow assigned — call {Uuid} will not be routed by flow",
                ctx.CampaignId, ctx.ChannelUuid);
            return;
        }

        var flow = await db.Flows.FirstOrDefaultAsync(
            f => f.Id == campaign.FlowId && f.IsActive && f.FlowType == FlowType.Telephony, ct);

        if (flow is null)
        {
            _logger.LogWarning(
                "TelephonyFlowEngine: no active telephony flow {FlowId} for campaign {CampaignId} — call {Uuid} unhandled",
                campaign.FlowId, ctx.CampaignId, ctx.ChannelUuid);
            return;
        }

        JsonObject definition;
        try
        {
            definition = JsonNode.Parse(flow.Definition)!.AsObject();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TelephonyFlowEngine: failed to parse flow definition for flow {FlowId}", flow.Id);
            return;
        }

        var entryNodeId = definition["entry_node"]?.GetValue<string>();
        if (string.IsNullOrEmpty(entryNodeId))
        {
            _logger.LogWarning("TelephonyFlowEngine: flow {FlowId} has no entry_node", flow.Id);
            return;
        }

        var nodes = definition["nodes"]?.AsObject();
        if (nodes is null)
        {
            _logger.LogWarning("TelephonyFlowEngine: flow {FlowId} has no nodes", flow.Id);
            return;
        }

        var currentNodeId = entryNodeId;
        int iterations = 0;

        while (!string.IsNullOrEmpty(currentNodeId) && iterations++ < MaxIterations)
        {
            if (!nodes.TryGetPropertyValue(currentNodeId, out var nodeValue) || nodeValue is not JsonObject nodeObj)
            {
                _logger.LogWarning(
                    "TelephonyFlowEngine: node {NodeId} not found in flow {FlowId}", currentNodeId, flow.Id);
                break;
            }

            var nodeType = nodeObj["type"]?.GetValue<string>();
            if (string.IsNullOrEmpty(nodeType))
            {
                _logger.LogWarning("TelephonyFlowEngine: node {NodeId} has no type", currentNodeId);
                break;
            }

            if (!_handlers.TryGetValue(nodeType, out var handler))
            {
                _logger.LogWarning("TelephonyFlowEngine: no handler for node type '{NodeType}'", nodeType);
                break;
            }

            _logger.LogDebug(
                "TelephonyFlowEngine: executing node {NodeId} ({NodeType}) for call {Uuid}",
                currentNodeId, nodeType, ctx.ChannelUuid);

            TelephonyNodeResult result;
            try
            {
                result = await handler.ExecuteAsync(nodeObj, ctx, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "TelephonyFlowEngine: error in node {NodeId} ({NodeType}) for call {Uuid}",
                    currentNodeId, nodeType, ctx.ChannelUuid);
                break;
            }

            currentNodeId = result.NextNodeId ?? string.Empty;
        }

        if (iterations >= MaxIterations)
            _logger.LogWarning("TelephonyFlowEngine: hit max iteration limit for call {Uuid}", ctx.ChannelUuid);
    }
}
