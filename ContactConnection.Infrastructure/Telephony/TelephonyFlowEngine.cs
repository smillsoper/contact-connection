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
    private readonly ITelephonyCallSessionStore _sessionStore;
    private readonly IReadOnlyDictionary<string, ITelephonyNodeHandler> _handlers;
    private readonly ILogger<TelephonyFlowEngine> _logger;

    public TelephonyFlowEngine(
        ITenantDbContextFactory factory,
        ITelephonyCallSessionStore sessionStore,
        IEnumerable<ITelephonyNodeHandler> handlers,
        ILogger<TelephonyFlowEngine> logger)
    {
        _factory      = factory;
        _sessionStore = sessionStore;
        _handlers     = handlers.ToDictionary(h => h.NodeType, StringComparer.OrdinalIgnoreCase);
        _logger       = logger;
    }

    // ── Initial execution (CHANNEL_PARK) ─────────────────────────────────────

    public async Task ExecuteAsync(TelephonyFlowContext ctx, CancellationToken ct = default)
    {
        await using var db = _factory.Create(ctx.TenantSchemaName);

        var phoneNumber = await db.PhoneNumbers
            .FirstOrDefaultAsync(p => p.Number == ctx.DestinationNumber && p.IsActive, ct);

        var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == ctx.CampaignId, ct);

        var flowId = phoneNumber?.TelephonyFlowId ?? campaign?.InboundFlowId;
        if (flowId is null)
        {
            _logger.LogWarning(
                "TelephonyFlowEngine: campaign {CampaignId} has no telephony flow — call {Uuid} unrouted",
                ctx.CampaignId, ctx.ChannelUuid);
            return;
        }

        var flow = await db.Flows.FirstOrDefaultAsync(
            f => f.Id == flowId && f.IsActive && f.FlowType == FlowType.Telephony, ct);

        if (flow is null)
        {
            _logger.LogWarning(
                "TelephonyFlowEngine: no active telephony flow {FlowId} for campaign {CampaignId} — call {Uuid} unhandled",
                flowId, ctx.CampaignId, ctx.ChannelUuid);
            return;
        }

        if (!TryParseDefinition(flow, out _, out var nodes, out var entryNodeId))
            return;

        // Scan the flow for event listener nodes and build the event handler map
        var eventHandlers = ScanEventHandlers(nodes!);

        // Persist call session to Redis so event branches can fire for the lifetime of this call
        var session = new TelephonyCallSession
        {
            ChannelUuid        = ctx.ChannelUuid,
            CallRecordId       = ctx.CallRecordId,
            TenantId           = ctx.TenantId,
            CampaignId         = ctx.CampaignId,
            TenantSubdomain    = ctx.TenantSubdomain,
            TenantSchemaName   = ctx.TenantSchemaName,
            TenantTimezone     = ctx.TenantTimezone,
            CallerNumber       = ctx.CallerNumber,
            DestinationNumber  = ctx.DestinationNumber,
            FlowId             = flow.Id,
            FlowDefinitionJson = flow.Definition,
            EventHandlers      = eventHandlers,
        };
        await _sessionStore.SaveAsync(session, ct);

        ctx.Trace = new TelephonyFlowTrace
        {
            FlowId    = flow.Id,
            FlowName  = flow.Name,
            StartedAt = DateTimeOffset.UtcNow,
        };

        _logger.LogInformation(
            "TelephonyFlowEngine [{Uuid}]: starting flow '{FlowName}' ({FlowId}) — entry={EntryNode}, events=[{Events}]",
            ctx.ChannelUuid, flow.Name, flow.Id, entryNodeId,
            string.Join(", ", eventHandlers.Keys));

        await ExecuteFromNodeAsync(ctx, flow.Id, flow.Name, nodes!, entryNodeId!, ct);

        // Propagate any vars written during the main branch back to the session
        foreach (var (k, v) in ctx.Vars)
            session.Vars[k] = v;
        await _sessionStore.SaveAsync(session, ct);
    }

    // ── Audio playback continuation (PLAYBACK_STOP) ───────────────────────────

    public async Task ResumeFromNodeAsync(
        string channelUuid,
        string nodeId,
        IEslCommander esl,
        CancellationToken ct = default)
    {
        var session = await _sessionStore.GetAsync(channelUuid, ct);
        if (session is null)
        {
            _logger.LogDebug(
                "TelephonyFlowEngine.ResumeFromNodeAsync: no session for channel {Uuid}", channelUuid);
            return;
        }

        JsonObject? nodes;
        try
        {
            var def = JsonNode.Parse(session.FlowDefinitionJson)!.AsObject();
            nodes   = def["nodes"]?.AsObject();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "TelephonyFlowEngine.ResumeFromNodeAsync: failed to parse cached flow definition for channel {Uuid}",
                channelUuid);
            return;
        }

        if (nodes is null) return;

        var ctx = new TelephonyFlowContext
        {
            ChannelUuid       = session.ChannelUuid,
            CallerNumber      = session.CallerNumber,
            DestinationNumber = session.DestinationNumber,
            TenantId          = session.TenantId,
            CampaignId        = session.CampaignId,
            CallRecordId      = session.CallRecordId,
            TenantSubdomain   = session.TenantSubdomain,
            TenantSchemaName  = session.TenantSchemaName,
            TenantTimezone    = session.TenantTimezone,
            Esl               = esl,
        };

        foreach (var (k, v) in session.Vars)
            ctx.Vars[k] = v;

        ctx.Trace = new TelephonyFlowTrace { FlowId = session.FlowId, StartedAt = DateTimeOffset.UtcNow };

        _logger.LogInformation(
            "TelephonyFlowEngine.ResumeFromNodeAsync [{Uuid}]: resuming at node {NodeId}", channelUuid, nodeId);

        await ExecuteFromNodeAsync(ctx, session.FlowId, "", nodes, nodeId, ct);

        foreach (var (k, v) in ctx.Vars)
            session.Vars[k] = v;
        await _sessionStore.SaveAsync(session, ct);
    }

    // ── Event branch execution ────────────────────────────────────────────────

    public async Task<FireEventResult> FireEventAsync(
        string channelUuid,
        string eventName,
        FireEventContext eventCtx,
        CancellationToken ct = default)
    {
        var session = await _sessionStore.GetAsync(channelUuid, ct);
        if (session is null)
        {
            _logger.LogDebug(
                "TelephonyFlowEngine.FireEventAsync: no session for channel {Uuid} (event={Event})",
                channelUuid, eventName);
            return new FireEventResult { Handled = false };
        }

        if (!session.EventHandlers.TryGetValue(eventName, out var handlerNodeId))
        {
            _logger.LogDebug(
                "TelephonyFlowEngine.FireEventAsync: no handler for event '{Event}' on channel {Uuid}",
                eventName, channelUuid);
            return new FireEventResult { Handled = false };
        }

        // Parse the cached flow definition
        JsonObject? nodes;
        try
        {
            var def = JsonNode.Parse(session.FlowDefinitionJson)!.AsObject();
            nodes   = def["nodes"]?.AsObject();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "TelephonyFlowEngine.FireEventAsync: failed to parse cached flow definition for channel {Uuid}",
                channelUuid);
            return new FireEventResult { Handled = false };
        }

        if (nodes is null) return new FireEventResult { Handled = false };

        // Build the branch context from the live session + event-specific data
        var ctx = new TelephonyFlowContext
        {
            ChannelUuid       = session.ChannelUuid,
            CallerNumber      = session.CallerNumber,
            DestinationNumber = session.DestinationNumber,
            TenantId          = session.TenantId,
            CampaignId        = session.CampaignId,
            CallRecordId      = session.CallRecordId,
            TenantSubdomain   = session.TenantSubdomain,
            TenantSchemaName  = session.TenantSchemaName,
            TenantTimezone    = session.TenantTimezone,
            Esl               = eventCtx.Esl,
            AnsweringAgentId  = eventCtx.AgentId,
            InteractionId     = eventCtx.InteractionId,
            FlowEngine        = eventCtx.FlowEngine,
        };

        foreach (var (k, v) in session.Vars)
            ctx.Vars[k] = v;
        foreach (var (k, v) in eventCtx.AdditionalVars)
            ctx.Vars[k] = v;

        ctx.Trace = new TelephonyFlowTrace
        {
            FlowId    = session.FlowId,
            FlowName  = "",
            StartedAt = DateTimeOffset.UtcNow,
        };

        _logger.LogInformation(
            "TelephonyFlowEngine.FireEventAsync [{Uuid}]: '{Event}' → node {NodeId}",
            channelUuid, eventName, handlerNodeId);

        await ExecuteFromNodeAsync(ctx, session.FlowId, "", nodes, handlerNodeId, ct);

        // Merge branch vars back into the session (shared state across all branches)
        foreach (var (k, v) in ctx.Vars)
            session.Vars[k] = v;

        // _crm_session_json must not persist into later event branches — consume it now.
        // If tf_script_pop ran it wrote the key; if not the key may be stale from a prior
        // branch (e.g. agent_selected). Either way the caller of FireEventAsync is the only
        // thing that should act on it.
        session.Vars.Remove("_crm_session_json");
        await _sessionStore.SaveAsync(session, ct);

        // Extract CRM session state if tf_script_pop fired during the branch
        FlowNodeState? crmSession = null;
        if (ctx.Vars.TryGetValue("_crm_session_json", out var sessionJson))
        {
            try
            {
                crmSession = JsonSerializer.Deserialize<FlowNodeState>(
                    sessionJson,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "TelephonyFlowEngine.FireEventAsync: failed to deserialize CRM session JSON for channel {Uuid}",
                    channelUuid);
            }
        }

        return new FireEventResult { Handled = true, CrmFlowSession = crmSession };
    }

    // ── Shared execution loop ─────────────────────────────────────────────────

    private async Task ExecuteFromNodeAsync(
        TelephonyFlowContext ctx,
        Guid flowId,
        string flowName,
        JsonObject nodes,
        string startNodeId,
        CancellationToken ct)
    {
        var trace = ctx.Trace!;
        var currentNodeId = startNodeId;
        int iterations = 0;
        string terminationReason = "completed";

        while (!string.IsNullOrEmpty(currentNodeId) && iterations++ < MaxIterations)
        {
            if (ctx.IsCancelled)
            {
                terminationReason = $"cancelled: {ctx.CancelMessage}";
                break;
            }

            if (!nodes.TryGetPropertyValue(currentNodeId, out var nodeValue) || nodeValue is not JsonObject nodeObj)
            {
                terminationReason = $"node '{currentNodeId}' not found in flow definition";
                _logger.LogWarning("TelephonyFlowEngine [{Uuid}]: {Reason}", ctx.ChannelUuid, terminationReason);
                trace.Steps.Add(new TelephonyFlowTraceStep
                    { NodeId = currentNodeId, NodeType = "unknown", At = DateTimeOffset.UtcNow, Error = terminationReason });
                break;
            }

            var nodeType = nodeObj["type"]?.GetValue<string>();
            if (string.IsNullOrEmpty(nodeType))
            {
                terminationReason = $"node '{currentNodeId}' has no type field";
                _logger.LogWarning("TelephonyFlowEngine [{Uuid}]: {Reason}", ctx.ChannelUuid, terminationReason);
                trace.Steps.Add(new TelephonyFlowTraceStep
                    { NodeId = currentNodeId, NodeType = "unknown", At = DateTimeOffset.UtcNow, Error = terminationReason });
                break;
            }

            if (!_handlers.TryGetValue(nodeType, out var handler))
            {
                terminationReason = $"no handler registered for node type '{nodeType}'";
                _logger.LogWarning("TelephonyFlowEngine [{Uuid}]: {Reason}", ctx.ChannelUuid, terminationReason);
                trace.Steps.Add(new TelephonyFlowTraceStep
                    { NodeId = currentNodeId, NodeType = nodeType, At = DateTimeOffset.UtcNow, Error = terminationReason });
                break;
            }

            _logger.LogInformation(
                "TelephonyFlowEngine [{Uuid}]: → {NodeId} ({NodeType})",
                ctx.ChannelUuid, currentNodeId, nodeType);

            TelephonyNodeResult result;
            try
            {
                result = await handler.ExecuteAsync(nodeObj, ctx, ct);
            }
            catch (Exception ex)
            {
                terminationReason = $"exception in node '{currentNodeId}' ({nodeType}): {ex.Message}";
                _logger.LogError(ex,
                    "TelephonyFlowEngine [{Uuid}]: error in node {NodeId} ({NodeType})",
                    ctx.ChannelUuid, currentNodeId, nodeType);
                trace.Steps.Add(new TelephonyFlowTraceStep
                    { NodeId = currentNodeId, NodeType = nodeType, At = DateTimeOffset.UtcNow, Error = ex.Message });
                break;
            }

            _logger.LogInformation(
                "TelephonyFlowEngine [{Uuid}]:   transition='{Transition}' next={NextNodeId}",
                ctx.ChannelUuid, result.TransitionTaken, result.NextNodeId ?? "(terminal)");

            trace.Steps.Add(new TelephonyFlowTraceStep
            {
                NodeId          = currentNodeId,
                NodeType        = nodeType,
                At              = DateTimeOffset.UtcNow,
                TransitionTaken = result.TransitionTaken,
                NextNodeId      = result.NextNodeId,
            });

            currentNodeId = result.NextNodeId ?? string.Empty;
        }

        if (iterations >= MaxIterations)
        {
            terminationReason = "max iteration limit reached";
            _logger.LogWarning("TelephonyFlowEngine [{Uuid}]: hit max iteration limit", ctx.ChannelUuid);
        }

        trace.CompletedAt       = DateTimeOffset.UtcNow;
        trace.TerminationReason = terminationReason;

        _logger.LogInformation(
            "TelephonyFlowEngine [{Uuid}]: segment complete — {StepCount} step(s), reason={Reason}",
            ctx.ChannelUuid, trace.Steps.Count, terminationReason);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Dictionary<string, string> ScanEventHandlers(JsonObject nodes)
    {
        var handlers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (nodeId, nodeValue) in nodes)
        {
            if (nodeValue is not JsonObject nodeObj) continue;
            var nodeType = nodeObj["type"]?.GetValue<string>();
            switch (nodeType)
            {
                case "tf_on_agent_selected":    handlers["agent_selected"]    = nodeId; break;
                case "tf_on_agent_answer":      handlers["agent_answer"]      = nodeId; break;
                case "tf_on_call_disconnected": handlers["call_disconnected"] = nodeId; break;
                case "tf_on_custom_event":
                    var name = nodeObj["eventName"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(name))
                        handlers[$"custom:{name}"] = nodeId;
                    break;
            }
        }
        return handlers;
    }

    private bool TryParseDefinition(
        Flow flow,
        out JsonObject? definition,
        out JsonObject? nodes,
        out string? entryNodeId)
    {
        definition  = null;
        nodes       = null;
        entryNodeId = null;

        try
        {
            definition = JsonNode.Parse(flow.Definition)!.AsObject();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "TelephonyFlowEngine: failed to parse flow definition for flow {FlowId}", flow.Id);
            return false;
        }

        entryNodeId = definition["entry_node"]?.GetValue<string>();
        if (string.IsNullOrEmpty(entryNodeId))
        {
            _logger.LogWarning("TelephonyFlowEngine: flow {FlowId} has no entry_node", flow.Id);
            return false;
        }

        nodes = definition["nodes"]?.AsObject();
        if (nodes is null)
        {
            _logger.LogWarning("TelephonyFlowEngine: flow {FlowId} has no nodes", flow.Id);
            return false;
        }

        return true;
    }
}
