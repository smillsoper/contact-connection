using System.Text.Json;
using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.CallTrace;
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
    private readonly ICallTraceRecorder _traceRecorder;
    private readonly ICallTraceSubscriptionRegistry _traceRegistry;
    private readonly ICallTraceNotifier _traceNotifier;
    private readonly ILogger<TelephonyFlowEngine> _logger;

    public TelephonyFlowEngine(
        ITenantDbContextFactory factory,
        ITelephonyCallSessionStore sessionStore,
        IEnumerable<ITelephonyNodeHandler> handlers,
        ICallTraceRecorder traceRecorder,
        ICallTraceSubscriptionRegistry traceRegistry,
        ICallTraceNotifier traceNotifier,
        ILogger<TelephonyFlowEngine> logger)
    {
        _factory        = factory;
        _sessionStore   = sessionStore;
        _handlers       = handlers.ToDictionary(h => h.NodeType, StringComparer.OrdinalIgnoreCase);
        _traceRecorder  = traceRecorder;
        _traceRegistry  = traceRegistry;
        _traceNotifier  = traceNotifier;
        _logger         = logger;
    }

    // ── Initial execution (CHANNEL_PARK) ─────────────────────────────────────

    public async Task ExecuteAsync(TelephonyFlowContext ctx, CancellationToken ct = default)
    {
        await using var db = _factory.Create(ctx.TenantSchemaName);

        var phoneNumber = await db.PhoneNumbers
            .FirstOrDefaultAsync(p => p.Number == ctx.DestinationNumber && p.IsActive, ct);

        var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == ctx.CampaignId, ct);

        // FlowIdOverride wins — a fired scheduled callback names the exact flow to run so it can't
        // re-enter the inbound flow and re-offer itself.
        var flowId = (ctx.FlowIdOverride is { } o && o != Guid.Empty ? o : (Guid?)null)
                     ?? phoneNumber?.TelephonyFlowId ?? campaign?.InboundFlowId;
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

        // Trace matching only ever considers calls that start after a trace subscription was
        // created — no attempt to backfill calls already in progress.
        var matchedSubscriptionIds = await _traceRegistry.MatchNewCallAsync(
            ctx.TenantId, ctx.CampaignId, flow.Id, ctx.DestinationNumber, ctx.CallerNumber, ct);
        foreach (var subscriptionId in matchedSubscriptionIds)
        {
            var capReached = await _traceRegistry.AttachCallAsync(subscriptionId, ctx.CallRecordId, ct);
            await _traceNotifier.NotifyCallMatchedAsync(subscriptionId, ctx.CallRecordId, ct);
            if (capReached)
            {
                await _traceRegistry.StopTraceAsync(subscriptionId, "call-limit-reached", ct);
                await _traceNotifier.NotifyTraceStoppedAsync(subscriptionId, "call-limit-reached", ct);
            }
        }

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
        ApplyPendingSessionMutations(session, ctx);
        await _sessionStore.SaveAsync(session, ct);
    }

    /// <summary>
    /// Node handlers can't persist session-level fields directly (the engine re-saves its own
    /// session copy after the node runs, clobbering a handler's separate write). Instead they
    /// leave a sentinel var and the engine applies it here before every post-execution save.
    /// tf_transfer's "campaign_queue" destination uses <c>_switch_campaign_id</c>.
    /// </summary>
    private static void ApplyPendingSessionMutations(TelephonyCallSession session, TelephonyFlowContext ctx)
    {
        // Handlers can't delete session vars through ctx.Vars (the sync above only copies in) —
        // honour the explicit removal list.
        foreach (var key in ctx.VarsToRemove)
            session.Vars.Remove(key);

        if (session.Vars.Remove("_switch_campaign_id", out var cid) && Guid.TryParse(cid, out var newCampaignId))
            session.CampaignId = newCampaignId;
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
        ApplyPendingSessionMutations(session, ctx);
        await _sessionStore.SaveAsync(session, ct);
    }

    // ── Flow handoff (tf_transfer → telephony_flow) ──────────────────────────

    public async Task<bool> SwitchFlowAsync(
        string channelUuid, Guid targetFlowId, IEslCommander esl, CancellationToken ct = default)
    {
        var session = await _sessionStore.GetAsync(channelUuid, ct);
        if (session is null)
        {
            _logger.LogWarning("TelephonyFlowEngine.SwitchFlowAsync: no session for channel {Uuid}", channelUuid);
            return false;
        }

        await using var db = _factory.Create(session.TenantSchemaName);
        var flow = await db.Flows.FirstOrDefaultAsync(
            f => f.Id == targetFlowId && f.IsActive && f.FlowType == FlowType.Telephony, ct);
        if (flow is null)
        {
            _logger.LogWarning(
                "TelephonyFlowEngine.SwitchFlowAsync [{Uuid}]: target flow {FlowId} not found / not an active telephony flow",
                channelUuid, targetFlowId);
            return false;
        }

        if (!TryParseDefinition(flow, out _, out var nodes, out var entryNodeId) || nodes is null)
            return false;

        session.FlowId             = flow.Id;
        session.FlowDefinitionJson = flow.Definition;
        session.EventHandlers      = ScanEventHandlers(nodes);
        await _sessionStore.SaveAsync(session, ct);

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

        ctx.Trace = new TelephonyFlowTrace { FlowId = flow.Id, FlowName = flow.Name, StartedAt = DateTimeOffset.UtcNow };

        _logger.LogInformation(
            "TelephonyFlowEngine.SwitchFlowAsync [{Uuid}]: → flow '{FlowName}' ({FlowId}) entry={Entry}",
            channelUuid, flow.Name, flow.Id, entryNodeId);

        await ExecuteFromNodeAsync(ctx, flow.Id, flow.Name, nodes, entryNodeId!, ct);

        foreach (var (k, v) in ctx.Vars)
            session.Vars[k] = v;
        ApplyPendingSessionMutations(session, ctx);
        await _sessionStore.SaveAsync(session, ct);
        return true;
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
        ApplyPendingSessionMutations(session, ctx);
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
                await RecordStepAsync(ctx, flowId, currentNodeId, "unknown", null, null, null, terminationReason, ct);
                break;
            }

            var nodeType = nodeObj["type"]?.GetValue<string>();
            if (string.IsNullOrEmpty(nodeType))
            {
                terminationReason = $"node '{currentNodeId}' has no type field";
                _logger.LogWarning("TelephonyFlowEngine [{Uuid}]: {Reason}", ctx.ChannelUuid, terminationReason);
                trace.Steps.Add(new TelephonyFlowTraceStep
                    { NodeId = currentNodeId, NodeType = "unknown", At = DateTimeOffset.UtcNow, Error = terminationReason });
                await RecordStepAsync(ctx, flowId, currentNodeId, "unknown", null, null, null, terminationReason, ct);
                break;
            }

            if (!_handlers.TryGetValue(nodeType, out var handler))
            {
                terminationReason = $"no handler registered for node type '{nodeType}'";
                _logger.LogWarning("TelephonyFlowEngine [{Uuid}]: {Reason}", ctx.ChannelUuid, terminationReason);
                trace.Steps.Add(new TelephonyFlowTraceStep
                    { NodeId = currentNodeId, NodeType = nodeType, At = DateTimeOffset.UtcNow, Error = terminationReason });
                await RecordStepAsync(ctx, flowId, currentNodeId, nodeType, null, null, null, terminationReason, ct);
                break;
            }

            // The flow definition keys nodes by id; individual node objects don't carry their own
            // id. Handlers that need it (tf_voicemail stashing _vm_node_id for the deferred
            // vm_done email lookup, tf_record for RecordingEvent traceability) read node["nodeId"],
            // so stamp it on the parsed node before dispatch. This mutates only the per-execution
            // parsed copy — session.FlowDefinitionJson keeps the original string.
            nodeObj["nodeId"] = currentNodeId;

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
                await RecordStepAsync(ctx, flowId, currentNodeId, nodeType, null, null, null, ex.Message, ct);
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
            await RecordStepAsync(ctx, flowId, currentNodeId, nodeType, result.TransitionTaken, result.NextNodeId, null, null, ct);

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

    private Task RecordStepAsync(
        TelephonyFlowContext ctx, Guid flowId, string nodeId, string nodeType,
        string? transitionTaken, string? nextNodeId, string? detail, string? exitReason, CancellationToken ct)
    {
        var snapshot = CallTraceSnapshot.BuildTelephonySnapshot(
            ctx.Vars, ctx.ChannelVars, ctx.CallerNumber, ctx.DestinationNumber, ctx.ChannelUuid);

        return _traceRecorder.RecordStepAsync(
            ctx.TenantId, ctx.TenantSchemaName, ctx.CallRecordId, TraceEngine.Telephony, nodeId, nodeType,
            label: null, detail, transitionTaken, nextNodeId, exitReason,
            ctx.CampaignId, flowId, ctx.DestinationNumber, ctx.CallerNumber, snapshot, ct);
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
