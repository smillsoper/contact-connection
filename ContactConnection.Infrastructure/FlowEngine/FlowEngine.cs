using System.Text.Json;
using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects;
using ContactConnection.Infrastructure.CallTrace;
using ContactConnection.Infrastructure.FlowEngine.NodeHandlers;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ContactConnection.Infrastructure.FlowEngine;

/// <summary>
/// Server-side flow interpreter.
///
/// Execution loop:
///   1. Load FlowExecutionContext from Redis (active) or build fresh (start)
///   2. Dispatch to the correct INodeHandler
///   3. If the handler returns a next node immediately (branch, set_variable, api_call)
///      keep advancing until a node requires agent interaction or the flow ends
///   4. Save context back to Redis
///   5. On terminal node: persist FlowSession to PostgreSQL, remove from Redis
/// </summary>
public class FlowEngine : IFlowEngine
{
    private readonly IFlowRepository _flows;
    private readonly IFlowSessionRepository _sessions;
    private readonly IAgentRepository _agents;
    private readonly ICallRecordRepository _callRecords;
    private readonly IDatabase _redis;
    private readonly TenantContext _tenantContext;
    private readonly IFlowNotifier _notifier;
    private readonly ICallTraceRecorder _traceRecorder;
    private readonly ISharedCallVariableStore _sharedVars;
    private readonly Dictionary<string, INodeHandler> _handlers;
    private readonly ILogger<FlowEngine> _logger;

    // Transparent node types — engine auto-advances without waiting for agent input.
    // api_call MUST be here: it has no natural "waiting for input" signal (like script), so
    // without this it falls through to the isStart-based stop/advance fallback below — which,
    // for a chain reached at true flow start (isStart stays true the whole way through), stops
    // and displays it with a Continue button. Clicking Continue then re-invokes ExecuteAsync on
    // the same node from scratch, calling the live API a second time with real side effects.
    // scheduled_callback books a row and advances transparently (like set_variable) — it has no
    // interactive content, so without this it stops and renders as a dead node with no controls.
    private static readonly HashSet<string> AutoAdvanceTypes =
        ["branch", "set_variable", "section", "execute_flow", "transition_to_flow", "api_call", "scheduled_callback",
         "set_custom_field", "get_custom_field", "store_value", "get_value",
         "add_to_cart", "remove_cart_item", "reset_cart",
         "authorize_payment", "void_payment", "send_email", "commit"];

    private static readonly TimeSpan SessionTtl = TimeSpan.FromHours(12);

    public FlowEngine(
        IFlowRepository flows,
        IFlowSessionRepository sessions,
        IAgentRepository agents,
        ICallRecordRepository callRecords,
        IConnectionMultiplexer redis,
        TenantContext tenantContext,
        IFlowNotifier notifier,
        ICallTraceRecorder traceRecorder,
        ISharedCallVariableStore sharedVars,
        IEnumerable<INodeHandler> handlers,
        ILogger<FlowEngine> logger)
    {
        _flows         = flows;
        _sessions      = sessions;
        _agents        = agents;
        _callRecords   = callRecords;
        _redis         = redis.GetDatabase();
        _tenantContext = tenantContext;
        _notifier      = notifier;
        _traceRecorder = traceRecorder;
        _sharedVars    = sharedVars;
        _handlers      = handlers.ToDictionary(h => h.NodeType, StringComparer.OrdinalIgnoreCase);
        _logger        = logger;
    }

    public async Task<FlowNodeState> StartAsync(StartFlowRequest request, CancellationToken ct = default)
    {
        var flow = await _flows.GetByIdAsync(request.FlowId, ct)
            ?? throw new InvalidOperationException($"Flow {request.FlowId} not found.");

        if (!flow.IsActive)
            throw new InvalidOperationException($"Flow {request.FlowId} is not published.");

        var definition = JsonNode.Parse(flow.Definition)?.AsObject()
            ?? throw new InvalidOperationException("Flow definition is invalid JSON.");

        var entryNodeId = definition["entry_node"]?.GetValue<string>()
            ?? throw new InvalidOperationException("Flow definition has no entry_node.");

        var session = FlowSession.Create(
            tenantId:      request.TenantId,
            flowId:        flow.Id,
            flowVersion:   flow.Version,
            callRecordId:  request.CallRecordId,
            interactionId: request.InteractionId,
            agentId:       request.AgentId,
            entryNodeId:   entryNodeId);

        await _sessions.AddAsync(session, ct);
        await _sessions.SaveChangesAsync(ct);

        var ctx = BuildContext(session, definition, request);

        await PopulateAgentAndTenantAsync(ctx, request.AgentId, ct);

        // Populate call_record/caller context from the call record so {{call_record.*}} and
        // {{caller.*}} tags resolve correctly — previously always empty (never wired up).
        await PopulateCallContextAsync(ctx, request.CallRecordId, request.InteractionId, ct);
        ctx.SharedVars = await _sharedVars.GetAllAsync(ctx.CallRecordId, ct);

        var state = await AdvanceInternalAsync(ctx, entryNodeId, agentInput: null, transition: "default", isStart: true, ct);
        state.FlowName = flow.Name;
        AttachSectionInfo(ctx, state);

        // Save ctx AFTER advance so CurrentNodeId reflects where the engine stopped,
        // not the entry node — same pattern as AdvanceAsync. A flow that fully auto-advances
        // start-to-finish with zero agent interaction (e.g. an api_call node straight into an
        // end node) reaches IsTerminal within this same call, so this needs the identical
        // if-terminal-complete/else-save-to-redis branching AdvanceAsync already uses below —
        // previously this only ever saved to Redis, so an all-automatic flow's session was never
        // persisted as complete in Postgres, and never even landed in Redis either (found and
        // fixed Session 90, while live-verifying sensitive-field masking against a real
        // all-automatic scratch flow).
        if (state.IsTerminal)
            await CompleteSession(ctx, ct);
        else
        {
            await SaveToRedis(ctx, ct);
            await PersistProgressAsync(ctx, ct);
        }

        await _notifier.PushNodeStateAsync(session.Id, state, ct);
        await NotifyAgentSessionsChangedAsync(ctx, ct);
        await NotifyCallChangedAsync(ctx.CallRecordId, ct);
        return state;
    }

    public async Task<FlowNodeState> AdvanceAsync(AdvanceFlowRequest request, CancellationToken ct = default)
    {
        var ctx = await LoadFromRedis(request.SessionId, ct)
            ?? throw new InvalidOperationException($"No active session {request.SessionId}.");
        ctx.SharedVars = await _sharedVars.GetAllAsync(ctx.CallRecordId, ct);

        FlowNodeState state;

        if (request.JumpToSectionNodeId is not null)
        {
            // Find which definition contains the target section — current flow first,
            // then parent frames (newest to oldest) so we can unwind the call stack.
            var sectionNode = GetNode(ctx.FlowDefinition, request.JumpToSectionNodeId);
            int? unwindToStackIndex = null;

            if (sectionNode is null)
            {
                for (int i = ctx.CallStack.Count - 1; i >= 0; i--)
                {
                    var frameDef = JsonNode.Parse(ctx.CallStack[i].DefinitionJson)?.AsObject();
                    if (frameDef is not null && GetNode(frameDef, request.JumpToSectionNodeId) is not null)
                    {
                        sectionNode        = GetNode(frameDef, request.JumpToSectionNodeId);
                        ctx.FlowDefinition = frameDef;
                        unwindToStackIndex = i;
                        break;
                    }
                }
            }

            if (sectionNode is null)
                throw new InvalidOperationException($"Section node '{request.JumpToSectionNodeId}' not found.");

            EnsureJumpAllowed(ctx, request.JumpToSectionNodeId);

            // Unwind call stack: remove the target frame and all frames deeper than it
            if (unwindToStackIndex.HasValue)
                ctx.CallStack.RemoveRange(unwindToStackIndex.Value, ctx.CallStack.Count - unwindToStackIndex.Value);

            if (sectionNode["clearPreviousValues"]?.GetValue<bool>() == true)
                ClearSectionVars(ctx.FlowDefinition, request.JumpToSectionNodeId, ctx);

            // Jumping away from a section before completing it must not mark it complete.
            // Clearing section state here means SectionNodeHandler sees no previous section
            // to add to CompletedSectionNodeIds — the abandoned section stays incomplete.
            ctx.CurrentSectionNodeId = null;
            ctx.CurrentSectionName   = null;
            ctx.CurrentSectionLocked = false;

            state = await AdvanceInternalAsync(
                ctx, request.JumpToSectionNodeId, agentInput: null, transition: "default", isStart: true, ct);
        }
        else
        {
            state = await AdvanceInternalAsync(
                ctx, ctx.CurrentNodeId, request.InputValue, request.Transition, isStart: false, ct);
        }

        // Mark the current section complete when the flow ends — covers the case where
        // the last section leads directly to an end node with no subsequent section node.
        if (state.IsTerminal && !string.IsNullOrEmpty(ctx.CurrentSectionNodeId))
            ctx.CompletedSectionNodeIds.Add(ctx.CurrentSectionNodeId);

        AttachSectionInfo(ctx, state);

        if (state.IsTerminal)
            await CompleteSession(ctx, ct);
        else
        {
            await SaveToRedis(ctx, ct);
            await PersistProgressAsync(ctx, ct);
        }

        await _notifier.PushNodeStateAsync(request.SessionId, state, ct);
        await NotifyCallChangedAsync(ctx.CallRecordId, ct);
        return state;
    }

    public async Task<FlowNodeState?> GetCurrentStateAsync(Guid sessionId, CancellationToken ct = default)
    {
        var ctx = await LoadFromRedis(sessionId, ct);
        if (ctx is null) return null;
        ctx.SharedVars = await _sharedVars.GetAllAsync(ctx.CallRecordId, ct);

        var node = GetNode(ctx.FlowDefinition, ctx.CurrentNodeId);
        if (node is null) return null;

        var nodeType = node["type"]?.GetValue<string>() ?? "script";
        if (!_handlers.TryGetValue(nodeType, out var handler)) return null;

        if (ReferencesCart(node)) await RefreshCartVarsAsync(ctx, ct);
        var result = await handler.ExecuteAsync(node, ctx, agentInput: null, agentTransition: "default", ct);
        return result.State;
    }

    // ── Post-call review (Call Records admin) ───────────────────────────────

    public async Task<FlowSessionSnapshot?> GetSessionSnapshotAsync(Guid sessionId, CancellationToken ct = default)
    {
        var loaded = await LoadForReviewAsync(sessionId, ct);
        if (loaded is null) return null;
        var (ctx, _, isLive) = loaded.Value;

        var apiCalls = new List<ApiCallNodeSummary>();
        foreach (var (nodeId, node) in await CollectApiCallNodesAsync(ctx.FlowDefinition, ct))
        {
            var output = node["outputVariable"]?.GetValue<string>()?.Trim();
            var runs = ctx.ExecutionHistory.Where(h => h.NodeId == nodeId && h.NodeType == "api_call").ToList();
            string? Var(string key) =>
                !string.IsNullOrEmpty(output) && ctx.FlowVars.TryGetValue($"{output}.{key}", out var v) ? v : null;
            apiCalls.Add(new ApiCallNodeSummary
            {
                NodeId         = nodeId,
                Label          = node["label"]?.GetValue<string>() ?? nodeId,
                OutputVariable = output,
                OncePerCall    = node["oncePerCall"]?.GetValue<bool>() == true,
                RunCount       = runs.Count,
                LastRunAt      = runs.Count > 0 ? runs[^1].EnteredAt : null,
                Success        = Var("success"),
                StatusCode     = Var("status_code"),
                Error          = Var("error"),
                Response       = Var("response"),
            });
        }

        return new FlowSessionSnapshot
        {
            SessionId   = sessionId,
            IsLive      = isLive,
            FlowVars    = new(ctx.FlowVars),
            Inputs      = new(ctx.Inputs),
            CommitLabel = ctx.CommitLabel,
            ApiCalls    = apiCalls,
        };
    }

    public async Task<IReadOnlyDictionary<string, string?>> UpdateSessionVariablesAsync(
        Guid sessionId, IReadOnlyDictionary<string, string?> changes, CancellationToken ct = default)
    {
        var (ctx, session, isLive) = await LoadForReviewAsync(sessionId, ct)
            ?? throw new InvalidOperationException($"Flow session {sessionId} not found.");

        var previous = new Dictionary<string, string?>();
        foreach (var (rawKey, value) in changes)
        {
            var key = rawKey.Trim();
            if (key.Length == 0) continue;
            previous[key] = ctx.FlowVars.TryGetValue(key, out var old) ? old : null;
            if (value is null) ctx.FlowVars.Remove(key);
            else ctx.FlowVars[key] = value;
        }

        await SaveReviewedAsync(ctx, session, isLive, ct);
        return previous;
    }

    public async Task<ApiCallRerunResult> RerunApiCallNodeAsync(Guid sessionId, string nodeId, CancellationToken ct = default)
    {
        var (ctx, session, isLive) = await LoadForReviewAsync(sessionId, ct)
            ?? throw new InvalidOperationException($"Flow session {sessionId} not found.");

        var (node, definition) = await FindNodeAsync(ctx.FlowDefinition, nodeId, ct)
            ?? throw new InvalidOperationException($"Node '{nodeId}' is not in this call's flow.");
        if (node["type"]?.GetValue<string>() != "api_call")
            throw new InvalidOperationException($"Node '{nodeId}' is not an API Call node.");
        if (!_handlers.TryGetValue("api_call", out var handler))
            throw new InvalidOperationException("No handler registered for node type 'api_call'.");

        var output = node["outputVariable"]?.GetValue<string>()?.Trim();
        string? Var(string key) =>
            !string.IsNullOrEmpty(output) && ctx.FlowVars.TryGetValue($"{output}.{key}", out var v) ? v : null;
        // oncePerCall + already succeeded = the handler replays the stored result, sends nothing.
        var replayed = node["oncePerCall"]?.GetValue<bool>() == true && Var("success") == "true";

        // Fresh call data — the whole point is to send what was corrected since the call.
        ctx.CallRecord.Clear();
        ctx.Caller.Clear();
        await PopulateAgentAndTenantAsync(ctx, ctx.AgentId, ct);
        await PopulateCallContextAsync(ctx, ctx.CallRecordId, ctx.InteractionId, ct);
        ctx.SharedVars = await _sharedVars.GetAllAsync(ctx.CallRecordId, ct);
        if (ReferencesCart(node)) await RefreshCartVarsAsync(ctx, ct);

        // Run the node where it lives, then put the session back exactly where it was — the
        // flow doesn't move on, only the node's output variables (and history) change.
        var (savedNodeId, savedDefinition) = (ctx.CurrentNodeId, ctx.FlowDefinition);
        ctx.CurrentNodeId  = nodeId;
        ctx.FlowDefinition = definition;
        var result = await handler.ExecuteAsync(node, ctx, agentInput: null, agentTransition: "default", ct);
        ctx.CurrentNodeId  = savedNodeId;
        ctx.FlowDefinition = savedDefinition;

        await SaveReviewedAsync(ctx, session, isLive, ct);

        var transition = Var("timed_out") == "true" ? "timeout" : Var("success") == "true" ? "success" : "error";
        _logger.LogInformation(
            "Re-ran api_call {NodeId} on session {SessionId} (call {CallRecordId}): {Transition} -> {Next}",
            nodeId, sessionId, ctx.CallRecordId, transition, result.NextNodeId);

        return new ApiCallRerunResult(
            Success: transition == "success",
            Transition: transition,
            StatusCode: Var("status_code"),
            Error: Var("error"),
            Response: Var("response"),
            Replayed: replayed);
    }

    public async Task<bool> PushLiveUpdateAsync(Guid sessionId, string message, CancellationToken ct = default)
    {
        var ctx = await LoadFromRedis(sessionId, ct);
        if (ctx is null) return false;

        // The live session holds its own copy of the call data, loaded when the script started.
        // Overlay a fresh read so later nodes (and the pushed node below) see the correction; keys a
        // flow set itself that the call record doesn't carry (e.g. caller.* extras) are kept.
        var fresh = new FlowExecutionContext { CallRecordId = ctx.CallRecordId };
        await PopulateCallContextAsync(fresh, ctx.CallRecordId, ctx.InteractionId, ct);
        foreach (var (k, v) in fresh.CallRecord) ctx.CallRecord[k] = v;
        foreach (var (k, v) in fresh.Caller) ctx.Caller[k] = v;
        await SaveToRedis(ctx, ct);

        ctx.SharedVars = await _sharedVars.GetAllAsync(ctx.CallRecordId, ct);
        var node = GetNode(ctx.FlowDefinition, ctx.CurrentNodeId);
        var nodeType = node?["type"]?.GetValue<string>() ?? "script";
        if (node is null || !_handlers.TryGetValue(nodeType, out var handler)) return false;

        // Same re-render the agent UI gets on reconnect — the node the agent is on is always a
        // stopping node (input/script/...), whose handler only builds display state on a pass
        // with no input. The session was already saved above, so nothing this re-render touches
        // (e.g. history) is persisted.
        if (ReferencesCart(node)) await RefreshCartVarsAsync(ctx, ct);
        var result = await handler.ExecuteAsync(node, ctx, agentInput: null, agentTransition: "default", ct);
        var state = result.State;
        state.FlowName = (await _flows.GetByIdAsync(ctx.FlowId, ct))?.Name;
        AttachSectionInfo(ctx, state);

        await _notifier.PushSessionUpdatedAsync(sessionId, state, message, ct);
        return true;
    }

    public async Task<IReadOnlyList<LiveFlowSession>> GetLiveSessionsForAgentsAsync(
        IReadOnlyCollection<Guid> agentIds, CancellationToken ct = default)
    {
        // Redis holds a live session for SessionTtl after its last step, and every step now writes
        // through to flow_sessions.updated_at — so anything older can't still be live.
        var candidates = await _sessions.GetActiveForAgentsAsync(agentIds, DateTimeOffset.UtcNow - SessionTtl, ct);
        var live = new List<LiveFlowSession>();
        var flowNames = new Dictionary<Guid, string?>();
        foreach (var s in candidates)
        {
            if (!await _redis.KeyExistsAsync(RedisKey(s.Id))) continue;
            if (!flowNames.TryGetValue(s.FlowId, out var name))
                flowNames[s.FlowId] = name = (await _flows.GetByIdAsync(s.FlowId, ct))?.Name;
            live.Add(new LiveFlowSession(s.AgentId, s.Id, s.CallRecordId, name, s.StartedAt));
        }
        return live;
    }

    /// <summary>Best effort — tells an open Call Records page for this call to refresh.</summary>
    private async Task NotifyCallChangedAsync(Guid callRecordId, CancellationToken ct)
    {
        try { await _notifier.PushCallChangedAsync(callRecordId, ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "Call changed push failed for call {CallRecordId}", callRecordId); }
    }

    /// <summary>Best effort — a dashboard refresh must never break the agent's own flow.</summary>
    private async Task NotifyAgentSessionsChangedAsync(FlowExecutionContext ctx, CancellationToken ct)
    {
        try { await _notifier.PushAgentSessionsChangedAsync(ctx.TenantId, ctx.AgentId, ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "Agent sessions push failed for session {SessionId}", ctx.SessionId); }
    }

    /// <summary>The session's state — from Redis while an agent still has it open (that copy is the
    /// newer one), otherwise from flow_sessions.</summary>
    private async Task<(FlowExecutionContext Ctx, FlowSession Session, bool IsLive)?> LoadForReviewAsync(
        Guid sessionId, CancellationToken ct)
    {
        var session = await _sessions.GetByIdAsync(sessionId, ct);
        if (session is null) return null;

        var live = await LoadFromRedis(sessionId, ct);
        if (live is not null) return (live, session, true);

        var flow = await _flows.GetByIdAsync(session.FlowId, ct);
        var ctx = FlowExecutionContext.Deserialize(
            sessionId, session.FlowId, session.FlowVersion,
            session.CallRecordId, session.InteractionId, session.AgentId, session.TenantId,
            session.CurrentNodeId, flow?.Definition ?? "{}",
            session.VariableStore, session.ExecutionHistory,
            callRecord: [], caller: [], agent: [], tenant: []);
        return (ctx, session, false);
    }

    private async Task SaveReviewedAsync(FlowExecutionContext ctx, FlowSession session, bool isLive, CancellationToken ct)
    {
        if (isLive) await SaveToRedis(ctx, ct);
        session.ReplaceState(ctx.SerializeVariableStore(), ctx.SerializeExecutionHistory());
        await _sessions.SaveChangesAsync(ct);
    }

    /// <summary>A node by id in the flow or any flow it calls (execute_flow / transition_to_flow),
    /// with the definition that holds it.</summary>
    private async Task<(JsonObject Node, JsonObject Definition)?> FindNodeAsync(
        JsonObject definition, string nodeId, CancellationToken ct)
    {
        foreach (var def in await ReachableDefinitionsAsync(definition, ct))
            if (GetNode(def, nodeId) is { } node) return (node, def);
        return null;
    }

    private async Task<List<(string NodeId, JsonObject Node)>> CollectApiCallNodesAsync(
        JsonObject definition, CancellationToken ct)
    {
        var found = new List<(string, JsonObject)>();
        foreach (var def in await ReachableDefinitionsAsync(definition, ct))
            if (def["nodes"] is JsonObject nodes)
                foreach (var (id, n) in nodes)
                    if (n is JsonObject obj && obj["type"]?.GetValue<string>() == "api_call")
                        found.Add((id, obj));
        return found;
    }

    /// <summary>The flow plus every flow reachable through execute_flow / transition_to_flow
    /// (capped — a guard against a pathological chain, not a real limit).</summary>
    private async Task<List<JsonObject>> ReachableDefinitionsAsync(JsonObject root, CancellationToken ct)
    {
        var result = new List<JsonObject> { root };
        var seen = new HashSet<Guid>();
        for (var i = 0; i < result.Count && result.Count < 20; i++)
        {
            if (result[i]["nodes"] is not JsonObject nodes) continue;
            foreach (var (_, n) in nodes)
            {
                if (n?["type"]?.GetValue<string>() is not ("execute_flow" or "transition_to_flow")) continue;
                if (!Guid.TryParse(n["targetFlowId"]?.GetValue<string>(), out var flowId) || !seen.Add(flowId)) continue;
                var flow = await _flows.GetByIdAsync(flowId, ct);
                if (flow is not null && JsonNode.Parse(flow.Definition) is JsonObject def)
                    result.Add(def);
            }
        }
        return result;
    }

    // ── Internal engine loop ────────────────────────────────────────────────

    private async Task<FlowNodeState> AdvanceInternalAsync(
        FlowExecutionContext ctx, string nodeId,
        string? agentInput, string transition, bool isStart, CancellationToken ct)
    {
        const int MaxAutoAdvance = 50; // safety cap — prevents infinite loops in bad flow definitions
        var steps = 0;

        // Tracks whether the node about to be processed is the exact one this call was invoked
        // for (isStart:true entry/jump-target, or the node id AdvanceAsync's caller just acted
        // on) versus one reached transparently by auto-advancing within this same call. AdvanceAsync
        // always passes isStart:false, so — unlike isStart — this stays meaningful past the very
        // first iteration and is what lets a "script" node stop when reached mid-chain (see below).
        var isFirstNode = true;

        while (true)
        {
            var isFirstNodeThisIteration = isFirstNode;
            isFirstNode = false;

            ctx.CurrentNodeId = nodeId;
            var node = GetNode(ctx.FlowDefinition, nodeId)
                ?? throw new InvalidOperationException($"Node '{nodeId}' not found in flow definition.");

            var nodeType = node["type"]?.GetValue<string>()
                ?? throw new InvalidOperationException($"Node '{nodeId}' has no type.");

            if (!_handlers.TryGetValue(nodeType, out var handler))
                throw new InvalidOperationException($"No handler registered for node type '{nodeType}'.");

            if (ReferencesCart(node)) await RefreshCartVarsAsync(ctx, ct);
            var result = await handler.ExecuteAsync(node, ctx, agentInput, transition, ct);

            // A "script" node's Continue click re-invokes its handler purely to look up the next
            // node id (deterministic — same content/transition as the first pass) — nothing new
            // happened for the agent or the flow, so recording this as its own trace step would
            // just show the same script twice. The pass that actually stopped and displayed it
            // already got its own step below. Excludes isStart: if the node this call was invoked
            // for IS a script (e.g. it's the flow's entry node), this pass is the one that stops
            // and shows it — that display still needs to be recorded.
            var isScriptAcknowledgement = nodeType == "script" && isFirstNodeThisIteration && !isStart;
            if (!isScriptAcknowledgement)
            {
                var detail = !string.IsNullOrWhiteSpace(result.State.Content) ? Truncate(result.State.Content) : result.State.Condition;
                var sensitiveKeys = CallTraceSnapshot.FindSensitiveKeys(ctx.FlowDefinition);
                var snapshot = CallTraceSnapshot.BuildCrmSnapshot(ctx, sensitiveKeys);
                await _traceRecorder.RecordStepAsync(
                    ctx.TenantId, _tenantContext.Current!.SchemaName, ctx.CallRecordId, TraceEngine.Crm, nodeId, nodeType,
                    result.State.Label, detail, transitionTaken: transition, result.NextNodeId,
                    exitReason: result.State.IsTerminal ? "terminal" : null,
                    campaignId: null, ctx.FlowId, dnis: null, ani: null, snapshot, ct);
            }

            // Terminal node or node waiting for input — return state
            if (result.NextNodeId is null || result.State.IsTerminal)
            {
                // Sub-flow ended — pop call stack and resume parent flow transparently
                if (result.State.IsTerminal && ctx.CallStack.Count > 0)
                {
                    var frame = ctx.CallStack[^1];
                    ctx.CallStack.RemoveAt(ctx.CallStack.Count - 1);
                    ctx.FlowDefinition = JsonNode.Parse(frame.DefinitionJson)?.AsObject() ?? [];
                    nodeId     = frame.ReturnNodeId;
                    agentInput = null;
                    transition = "default";
                    continue;
                }

                return result.State;
            }

            // Auto-advancing node (branch, set_variable) — loop immediately without waiting for agent
            if (AutoAdvanceTypes.Contains(nodeType) && ++steps < MaxAutoAdvance)
            {
                nodeId     = result.NextNodeId;
                agentInput = null;
                transition = "default";
                continue;
            }

            // "script" nodes have no data to capture and no natural "waiting for input" signal
            // (they always compute a real next node), so — unlike input/email/phone/address,
            // which stop themselves by returning NextNodeId:null until answered — the engine has
            // to decide on their behalf: stop when reached by auto-advancing from an earlier node
            // in this same call (isFirstNodeThisIteration is false), advance past it when this call
            // was invoked directly against it (the agent just clicked its Continue button).
            if (nodeType == "script" && !isFirstNodeThisIteration)
                return result.State;

            // StartAsync / jump-target first display: stop here and show this node to the agent.
            if (isStart)
                return result.State;

            // AdvanceAsync: agent acted on this node — advance past it to the next node.
            nodeId     = result.NextNodeId;
            agentInput = null;
            transition = "default";
            isStart    = false;
        }
    }

    // ── Context build/persist ───────────────────────────────────────────────

    /// <summary>{{agent.*}} from the agent's row, {{tenant.*}} from the resolved tenant.</summary>
    private async Task PopulateAgentAndTenantAsync(FlowExecutionContext ctx, Guid agentId, CancellationToken ct)
    {
        var agent = await _agents.GetByIdAsync(agentId, ct);
        if (agent is not null)
        {
            ctx.Agent["id"]         = agent.Id.ToString();
            ctx.Agent["first_name"] = agent.FirstName;
            ctx.Agent["last_name"]  = agent.LastName;
            ctx.Agent["full_name"]  = agent.FullName;
            ctx.Agent["email"]      = agent.Email;
            ctx.Agent["extension"]  = agent.SipExtension ?? string.Empty;
            ctx.Agent["role"]       = agent.Role;
        }

        if (_tenantContext.Current is { } tenant)
        {
            ctx.Tenant["id"]        = tenant.Id.ToString();
            ctx.Tenant["name"]      = tenant.Name;
            ctx.Tenant["subdomain"] = tenant.Subdomain;
            ctx.Tenant["timezone"]  = tenant.Timezone;
            ctx.Tenant["plan_tier"] = tenant.PlanTier;
        }
    }

    private static FlowExecutionContext BuildContext(
        FlowSession session, JsonObject definition, StartFlowRequest request)
    {
        return new FlowExecutionContext
        {
            SessionId      = session.Id,
            FlowId         = session.FlowId,
            FlowVersion    = session.FlowVersion,
            CallRecordId   = session.CallRecordId,
            InteractionId  = session.InteractionId,
            AgentId        = session.AgentId,
            TenantId       = session.TenantId,
            CurrentNodeId  = session.CurrentNodeId,
            FlowDefinition = definition,
            // call_record/caller/agent/tenant are filled in by the caller right after this
            // returns (PopulateCallContextAsync + the agent/tenant lookups in StartAsync) —
            // just seed the one field callers need before those lookups run.
            CallRecord = [],
            Caller     = [],
            Agent      = new() { ["id"] = request.AgentId.ToString() },
            Tenant     = new() { ["id"] = request.TenantId.ToString() }
        };
    }

    /// <summary>Commit point: nothing before the point of no return can be revisited. Enforced here in
    /// the engine, not just by hiding the dropdown, so a stale UI or a direct API call can't get round
    /// it. Only the commit node's allowed sections stay reachable.</summary>
    internal static void EnsureJumpAllowed(FlowExecutionContext ctx, string sectionNodeId)
    {
        if (ctx.IsCommitted && !ctx.CommitAllowedSections.Contains(sectionNodeId))
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(ctx.CommitLabel)
                    ? "This call has passed a commit point \u2014 earlier sections can no longer be changed."
                    : ctx.CommitLabel);
    }

    private static bool ReferencesCart(JsonObject node) =>
        node.ToJsonString().Contains("{{cart.", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// {{cart.*}} — the call's current cart (commerce engine output on call_records.cart), read
    /// fresh only for nodes that reference it, since add_to_cart and tax/fee recalculation after an
    /// address change both update it mid-flow. Amounts are plain decimals ("139.90"); items_summary
    /// is "1 x NeuroQ …, 1 x Memory DHA …".
    /// </summary>
    private async Task RefreshCartVarsAsync(FlowExecutionContext ctx, CancellationToken ct)
    {
        ctx.Cart.Clear();
        var cart = (await _callRecords.GetByIdAsync(ctx.CallRecordId, ct))?.Cart;
        if (cart is null) return;

        static string Money(decimal d) => d.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        ctx.Cart["total"]         = Money(cart.CartTotal);
        ctx.Cart["subtotal"]      = Money(cart.CartSubtotal);
        ctx.Cart["shipping"]      = Money(cart.Shipping);
        ctx.Cart["sales_tax"]     = Money(cart.SalesTax);
        ctx.Cart["shipping_tax"]  = Money(cart.ShippingTax);
        ctx.Cart["fees"]          = Money(cart.Fees?.Sum(f => f.Amount) ?? 0);
        ctx.Cart["first_payment"] = Money(cart.PaymentBreakdowns.FirstOrDefault()?.Total ?? cart.CartTotal);
        ctx.Cart["item_count"]    = cart.Items.Count.ToString();
        ctx.Cart["items_summary"] = string.Join(", ", cart.Items.Select(i => $"{i.Quantity} x {i.Description}"));
    }

    /// <summary>
    /// Populates {{call_record.*}} and {{caller.*}} tags from the call record (and its current
    /// interaction, for disposition). Some designer-advertised fields have no backing data yet
    /// (call_record.list_id, call_record.notes) and are left unset — the resolver returns the
    /// tag unresolved rather than erroring, so this degrades gracefully as those are added later.
    /// </summary>
    private async Task PopulateCallContextAsync(
        FlowExecutionContext ctx, Guid callRecordId, Guid interactionId, CancellationToken ct)
    {
        var record = await _callRecords.GetByIdWithInteractionsAsync(callRecordId, ct);
        if (record is null) return;

        ctx.CallRecord["id"] = record.Id.ToString();
        ctx.CallRecord["status"] = record.OverallStatus;
        ctx.CallRecord["call_source"] = record.Source;
        ctx.CallRecord["record_type"] = record.RecordType;
        ctx.CallRecord["phone_number"] = record.Phone ?? string.Empty;
        ctx.CallRecord["dnis"] = record.Dnis ?? string.Empty;
        ctx.CallRecord["account_number"] = record.AccountNumber ?? string.Empty;
        ctx.CallRecord["order_number"] = record.OrderNumber ?? string.Empty;
        ctx.CallRecord[CallAddressVars.Email]         = record.Email ?? string.Empty;
        ctx.CallRecord[CallAddressVars.FirstName]     = record.FirstName ?? string.Empty;
        ctx.CallRecord[CallAddressVars.LastName]      = record.LastName ?? string.Empty;
        ctx.CallRecord[CallAddressVars.BillingPhone]  = record.BillingPhone ?? string.Empty;
        ctx.CallRecord[CallAddressVars.ShippingPhone] = record.ShippingPhone ?? string.Empty;
        // Address objects (same shape as an address node's output) — {{call_record.shipping_address.city}}
        if (record.Addresses?.Billing is { } billing)
            ctx.CallRecord[CallAddressVars.Billing] = CallAddressJson.ToJsonObject(billing).ToJsonString();
        if (record.Addresses?.Shipping is { } shipping)
            ctx.CallRecord[CallAddressVars.Shipping] = CallAddressJson.ToJsonObject(shipping).ToJsonString();
        ctx.CallRecord["campaign_id"] = record.CampaignId.ToString();
        ctx.CallRecord["call_started_at"] = record.CallStartAt?.ToString("O") ?? string.Empty;
        ctx.CallRecord["call_ended_at"] = record.CallEndAt?.ToString("O") ?? string.Empty;
        ctx.CallRecord["handle_time_seconds"] = record.HandleTimeSeconds?.ToString() ?? string.Empty;

        var interaction = record.Interactions.FirstOrDefault(i => i.Id == interactionId);
        ctx.CallRecord["disposition"] = interaction?.Disposition ?? string.Empty;

        var fullName = string.Join(" ", new[] { record.FirstName, record.LastName }
            .Where(n => !string.IsNullOrWhiteSpace(n)));
        ctx.Caller["name"] = fullName;
        ctx.Caller["first_name"] = record.FirstName ?? string.Empty;
        ctx.Caller["last_name"] = record.LastName ?? string.Empty;
        // The ANI is always available for a real inbound call; CallerId is that source of
        // truth for "the number the caller is calling from" — distinct from CallRecord.Phone,
        // which is a general contact-phone field an agent may capture/correct later.
        ctx.Caller["phone"] = record.CallerId ?? string.Empty;
        ctx.Caller["email"] = record.Email ?? string.Empty;
        ctx.Caller["account_number"] = record.AccountNumber ?? string.Empty;
        ctx.Caller["billing_address"] = FormatAddress(record.Addresses?.Billing);
        ctx.Caller["shipping_address"] = FormatAddress(record.Addresses?.Shipping);
    }

    private static string FormatAddress(AddressData? address)
    {
        if (address is null) return string.Empty;

        var street = string.Join(" ", new[] { address.Prefix, address.Street }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        var unit = string.Join(" ", new[] { address.UnitPrefix, address.Unit }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        var zip = string.IsNullOrWhiteSpace(address.Zip4) ? address.Zip : $"{address.Zip}-{address.Zip4}";

        var parts = new[] { street, unit, address.City, address.State, zip, address.Country }
            .Where(s => !string.IsNullOrWhiteSpace(s));
        return string.Join(", ", parts);
    }

    private async Task SaveToRedis(FlowExecutionContext ctx, CancellationToken ct)
    {
        var key   = RedisKey(ctx.SessionId);
        var value = JsonSerializer.Serialize(new RedisCacheEntry(ctx));
        await _redis.StringSetAsync(key, value, SessionTtl);
    }

    /// <summary>
    /// Write-through of the live state to flow_sessions on every step (S165). Redis stays the
    /// working copy, but it expires 12h after the last step — a script the agent never finished
    /// (closed the tab, dropped call) used to lose its variables entirely, leaving nothing to
    /// review or correct afterwards (e.g. resubmitting a rejected order).
    /// </summary>
    private async Task PersistProgressAsync(FlowExecutionContext ctx, CancellationToken ct)
    {
        var session = await _sessions.GetByIdAsync(ctx.SessionId, ct);
        if (session is null) return;
        session.AdvanceTo(ctx.CurrentNodeId, ctx.SerializeVariableStore(), ctx.SerializeExecutionHistory());
        await _sessions.SaveChangesAsync(ct);
    }

    private async Task<FlowExecutionContext?> LoadFromRedis(Guid sessionId, CancellationToken ct)
    {
        var key  = RedisKey(sessionId);
        var json = await _redis.StringGetAsync(key);
        if (!json.HasValue) return null;

        var entry = JsonSerializer.Deserialize<RedisCacheEntry>(json.ToString());
        if (entry is null) return null;

        // Re-parse definition (stored as string in cache entry)
        var definition = JsonNode.Parse(entry.DefinitionJson)?.AsObject() ?? [];

        return FlowExecutionContext.Deserialize(
            sessionId:          sessionId,
            flowId:             entry.FlowId,
            flowVersion:        entry.FlowVersion,
            callRecordId:       entry.CallRecordId,
            interactionId:      entry.InteractionId,
            agentId:            entry.AgentId,
            tenantId:           entry.TenantId,
            currentNodeId:      entry.CurrentNodeId,
            definitionJson:     entry.DefinitionJson,
            variableStoreJson:  entry.VariableStoreJson,
            executionHistoryJson: entry.ExecutionHistoryJson,
            callRecord:         entry.CallRecord,
            caller:             entry.Caller,
            agent:              entry.Agent,
            tenant:             entry.Tenant);
    }

    private async Task CompleteSession(FlowExecutionContext ctx, CancellationToken ct)
    {
        // Persist final state to PostgreSQL
        var session = await _sessions.GetByIdAsync(ctx.SessionId, ct);
        if (session is not null)
        {
            session.Complete(ctx.SerializeVariableStore(), ctx.SerializeExecutionHistory());
            await _sessions.SaveChangesAsync(ct);
        }

        // Remove from Redis
        await _redis.KeyDeleteAsync(RedisKey(ctx.SessionId));
        await NotifyAgentSessionsChangedAsync(ctx, ct);

        // A captured card is kept after authorization for re-auth on an order change (see
        // PaymentService); the script is done with it now.
        var record = await _callRecords.GetByIdAsync(ctx.CallRecordId, ct);
        if (record is not null && !string.IsNullOrEmpty(record.SensitiveData))
        {
            record.WipeSensitiveData("flow_completed");
            await _callRecords.SaveChangesAsync(ct);
        }
    }

    private static JsonObject? GetNode(JsonObject definition, string nodeId) =>
        definition["nodes"]?[nodeId]?.AsObject();

    private static string Truncate(string content, int maxLength = 200) =>
        content.Length <= maxLength ? content : content[..maxLength] + "…";

    private static string RedisKey(Guid sessionId) => $"flow:session:{sessionId}";

    // ── Section helpers ─────────────────────────────────────────────────────

    private void AttachSectionInfo(FlowExecutionContext ctx, FlowNodeState state)
    {
        state.CurrentSectionName = ctx.CurrentSectionName;
        state.SectionLocked      = ctx.CurrentSectionLocked;
        if (ctx.IsCommitted)
            state.CommitLabel = string.IsNullOrWhiteSpace(ctx.CommitLabel) ? "Committed — no further changes" : ctx.CommitLabel;

        if (HasAnySections(ctx))
        {
            var targets = BuildJumpTargets(ctx);
            if (ctx.IsCommitted)
                targets = targets.Where(t => ctx.CommitAllowedSections.Contains(t.SectionNodeId)).ToList();
            state.JumpTargets = targets;
        }
    }

    private static bool HasAnySections(FlowExecutionContext ctx)
    {
        if (HasSectionsInDefinition(ctx.FlowDefinition)) return true;
        return ctx.CallStack.Any(frame =>
        {
            var def = JsonNode.Parse(frame.DefinitionJson)?.AsObject();
            return def is not null && HasSectionsInDefinition(def);
        });
    }

    private static bool HasSectionsInDefinition(JsonObject definition)
    {
        var nodes = definition["nodes"]?.AsObject();
        return nodes?.Any(kvp =>
            kvp.Value?.AsObject()?["type"]?.GetValue<string>() == "section") == true;
    }

    private static List<JumpTarget> BuildJumpTargets(FlowExecutionContext ctx)
    {
        var targets = new List<JumpTarget>();

        // Current (sub-)flow sections first, then parent frames newest-to-oldest
        CollectSectionTargets(ctx.FlowDefinition, ctx, targets);
        for (int i = ctx.CallStack.Count - 1; i >= 0; i--)
        {
            var parentDef = JsonNode.Parse(ctx.CallStack[i].DefinitionJson)?.AsObject();
            if (parentDef is not null)
                CollectSectionTargets(parentDef, ctx, targets);
        }

        return targets;
    }

    private static void CollectSectionTargets(
        JsonObject definition, FlowExecutionContext ctx, List<JumpTarget> targets)
    {
        var nodes = definition["nodes"]?.AsObject();
        if (nodes is null) return;

        foreach (var kvp in nodes)
        {
            var node = kvp.Value?.AsObject();
            if (node?["type"]?.GetValue<string>() != "section") continue;

            var nodeId                = kvp.Key;
            var name                  = node["name"]?.GetValue<string>()
                                        ?? node["label"]?.GetValue<string>()
                                        ?? nodeId;
            var clearPrev             = node["clearPreviousValues"]?.GetValue<bool>() ?? false;
            var allowJumpFromAnywhere = node["allowJumpFromAnywhere"]?.GetValue<bool>() ?? false;
            var isCurrentSection      = nodeId == ctx.CurrentSectionNodeId;
            var isCompleted           = ctx.CompletedSectionNodeIds.Contains(nodeId);
            var isEncountered         = ctx.EncounteredSectionNodeIds.Contains(nodeId);

            // Show if: current, completed, encountered (started but possibly abandoned),
            // or explicitly surfaced everywhere — hides sections the flow hasn't reached yet.
            if (!isCurrentSection && !isCompleted && !isEncountered && !allowJumpFromAnywhere)
                continue;

            var outputVar = node["outputVariable"]?.GetValue<string>()?.Trim();
            var isLocked  = false;
            if (!string.IsNullOrEmpty(outputVar) && ctx.FlowVars.TryGetValue(outputVar, out var varJson))
            {
                try
                {
                    var obj = JsonNode.Parse(varJson)?.AsObject();
                    isLocked = obj?["locked"]?.GetValue<string>() == "true";
                }
                catch { }
            }

            targets.Add(new JumpTarget
            {
                SectionNodeId       = nodeId,
                Name                = name,
                IsCurrentSection    = isCurrentSection,
                ClearPreviousValues = clearPrev,
                IsLocked            = isLocked,
            });
        }
    }

    /// <summary>
    /// BFS from the section node's transition targets, collecting all node IDs that belong
    /// to this section — stops when it hits another section node or the end of the graph.
    /// </summary>
    private static IEnumerable<string> NodesInSection(JsonObject definition, string sectionNodeId)
    {
        var sectionNode = GetNode(definition, sectionNodeId);
        if (sectionNode is null) yield break;

        var transitions = sectionNode["transitions"]?.AsObject();
        if (transitions is null) yield break;

        var visited = new HashSet<string>();
        var queue   = new Queue<string>();

        foreach (var kvp in transitions)
        {
            var t = kvp.Value?.GetValue<string>();
            if (t != null) queue.Enqueue(t);
        }

        while (queue.Count > 0)
        {
            var nodeId = queue.Dequeue();
            if (!visited.Add(nodeId)) continue;

            var node = GetNode(definition, nodeId);
            if (node is null) continue;
            if (node["type"]?.GetValue<string>() == "section") continue; // section boundary

            yield return nodeId;

            var nextTransitions = node["transitions"]?.AsObject();
            if (nextTransitions is null) continue;
            foreach (var kvp in nextTransitions)
            {
                var t = kvp.Value?.GetValue<string>();
                if (t != null) queue.Enqueue(t);
            }
        }
    }

    private static void ClearSectionVars(JsonObject definition, string sectionNodeId, FlowExecutionContext ctx)
    {
        foreach (var nodeId in NodesInSection(definition, sectionNodeId))
        {
            var node      = GetNode(definition, nodeId);
            var outputVar = node?["outputVariable"]?.GetValue<string>()?.Trim();
            if (!string.IsNullOrEmpty(outputVar))
                ctx.FlowVars.Remove(outputVar);
        }
    }

    // Compact Redis cache entry — avoids re-querying PostgreSQL on every advance
    private record RedisCacheEntry(
        Guid FlowId, int FlowVersion,
        Guid CallRecordId, Guid InteractionId, Guid AgentId, Guid TenantId,
        string CurrentNodeId, string DefinitionJson,
        string VariableStoreJson, string ExecutionHistoryJson,
        Dictionary<string, string> CallRecord,
        Dictionary<string, string> Caller,
        Dictionary<string, string> Agent,
        Dictionary<string, string> Tenant)
    {
        // Parameterless ctor for deserialization
        public RedisCacheEntry() : this(Guid.Empty, 0, Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty,
            string.Empty, "{}", "{}", "[]", [], [], [], []) { }

        public RedisCacheEntry(FlowExecutionContext ctx) : this(
            ctx.FlowId, ctx.FlowVersion,
            ctx.CallRecordId, ctx.InteractionId, ctx.AgentId, ctx.TenantId,
            ctx.CurrentNodeId,
            ctx.FlowDefinition.ToJsonString(),
            ctx.SerializeVariableStore(),
            ctx.SerializeExecutionHistory(),
            ctx.CallRecord, ctx.Caller, ctx.Agent, ctx.Tenant) { }
    }
}
