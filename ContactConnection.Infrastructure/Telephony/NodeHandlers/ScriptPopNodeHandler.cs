using System.Text.Json;
using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

/// <summary>
/// Starts a CRM script flow session for the answering agent.
/// Flow resolution order: node-level flowId override → campaign's FlowId.
/// Stores the serialized FlowNodeState in ctx.Vars["_crm_session_json"] so the
/// calling endpoint can return it to the agent UI.
/// Node data: { "flowId": "optional-guid-override" }
/// Requires ctx.AnsweringAgentId, ctx.InteractionId, and ctx.FlowEngine to be set
/// (only available in the ResumeAsync / HTTP request path).
/// </summary>
public class ScriptPopNodeHandler : ITelephonyNodeHandler
{
    public string NodeType => "tf_script_pop";

    private readonly ITenantDbContextFactory _factory;
    private readonly ILogger<ScriptPopNodeHandler> _logger;

    public ScriptPopNodeHandler(ITenantDbContextFactory factory, ILogger<ScriptPopNodeHandler> logger)
    {
        _factory = factory;
        _logger  = logger;
    }

    public async Task<TelephonyNodeResult> ExecuteAsync(
        JsonObject node, TelephonyFlowContext ctx, CancellationToken ct = default)
    {
        var nextNodeId = node["transitions"]?["default"]?.GetValue<string>();

        if (ctx.FlowEngine is null || ctx.AnsweringAgentId is null || ctx.InteractionId is null)
        {
            _logger.LogWarning(
                "ScriptPopNodeHandler [{Uuid}]: FlowEngine, AnsweringAgentId, or InteractionId not set — skipping script pop",
                ctx.ChannelUuid);
            return new TelephonyNodeResult(nextNodeId, "no_context");
        }

        // Resolve which CRM flow to launch
        Guid? flowId = null;

        var nodeFlowIdStr = node["flowId"]?.GetValue<string>();
        if (Guid.TryParse(nodeFlowIdStr, out var nodeFlowId))
            flowId = nodeFlowId;

        if (flowId is null)
        {
            await using var db = _factory.Create(ctx.TenantSchemaName);

            // 2nd priority: DID-level script flow override
            if (!string.IsNullOrEmpty(ctx.DestinationNumber))
            {
                var phoneNumber = await db.PhoneNumbers
                    .FirstOrDefaultAsync(p => p.Number == ctx.DestinationNumber && p.IsActive, ct);
                flowId = phoneNumber?.FlowId;
            }

            // 3rd priority: campaign fallback script flow
            if (flowId is null)
            {
                var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == ctx.CampaignId, ct);
                flowId = campaign?.FlowId;
            }
        }

        if (flowId is null)
        {
            _logger.LogWarning(
                "ScriptPopNodeHandler [{Uuid}]: no CRM flow configured for campaign {CampaignId} — skipping script pop",
                ctx.ChannelUuid, ctx.CampaignId);
            return new TelephonyNodeResult(nextNodeId, "no_flow");
        }

        try
        {
            var sessionState = await ctx.FlowEngine.StartAsync(new StartFlowRequest
            {
                FlowId        = flowId.Value,
                CallRecordId  = ctx.CallRecordId,
                InteractionId = ctx.InteractionId.Value,
                AgentId       = ctx.AnsweringAgentId.Value,
                TenantId      = ctx.TenantId,
            }, ct);

            ctx.Vars["_crm_session_json"] = JsonSerializer.Serialize(sessionState, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false,
            });

            _logger.LogInformation(
                "ScriptPopNodeHandler [{Uuid}]: started CRM session {SessionId} for agent {AgentId}",
                ctx.ChannelUuid, sessionState.SessionId, ctx.AnsweringAgentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ScriptPopNodeHandler [{Uuid}]: failed to start CRM flow {FlowId}",
                ctx.ChannelUuid, flowId);
        }

        return new TelephonyNodeResult(nextNodeId, "default");
    }
}
