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
    private readonly Dictionary<string, INodeHandler> _handlers;
    private readonly ILogger<FlowEngine> _logger;

    // Transparent node types — engine auto-advances without waiting for agent input
    private static readonly HashSet<string> AutoAdvanceTypes =
        ["branch", "set_variable", "section", "execute_flow", "transition_to_flow"];

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

        // Populate agent context from database so {{agent.*}} tags resolve correctly
        var agent = await _agents.GetByIdAsync(request.AgentId, ct);
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

        // Populate tenant context so {{tenant.*}} tags resolve correctly
        if (_tenantContext.Current is { } tenant)
        {
            ctx.Tenant["id"]        = tenant.Id.ToString();
            ctx.Tenant["name"]      = tenant.Name;
            ctx.Tenant["subdomain"] = tenant.Subdomain;
            ctx.Tenant["timezone"]  = tenant.Timezone;
            ctx.Tenant["plan_tier"] = tenant.PlanTier;
        }

        // Populate call_record/caller context from the call record so {{call_record.*}} and
        // {{caller.*}} tags resolve correctly — previously always empty (never wired up).
        await PopulateCallContextAsync(ctx, request.CallRecordId, request.InteractionId, ct);

        var state = await AdvanceInternalAsync(ctx, entryNodeId, agentInput: null, transition: "default", isStart: true, ct);
        state.FlowName = flow.Name;
        AttachSectionInfo(ctx, state);

        // Save ctx AFTER advance so CurrentNodeId reflects where the engine stopped,
        // not the entry node — same pattern as AdvanceAsync.
        if (!state.IsTerminal)
            await SaveToRedis(ctx, ct);

        await _notifier.PushNodeStateAsync(session.Id, state, ct);
        return state;
    }

    public async Task<FlowNodeState> AdvanceAsync(AdvanceFlowRequest request, CancellationToken ct = default)
    {
        var ctx = await LoadFromRedis(request.SessionId, ct)
            ?? throw new InvalidOperationException($"No active session {request.SessionId}.");

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
            await SaveToRedis(ctx, ct);

        await _notifier.PushNodeStateAsync(request.SessionId, state, ct);
        return state;
    }

    public async Task<FlowNodeState?> GetCurrentStateAsync(Guid sessionId, CancellationToken ct = default)
    {
        var ctx = await LoadFromRedis(sessionId, ct);
        if (ctx is null) return null;

        var node = GetNode(ctx.FlowDefinition, ctx.CurrentNodeId);
        if (node is null) return null;

        var nodeType = node["type"]?.GetValue<string>() ?? "script";
        if (!_handlers.TryGetValue(nodeType, out var handler)) return null;

        var result = await handler.ExecuteAsync(node, ctx, agentInput: null, agentTransition: "default", ct);
        return result.State;
    }

    // ── Internal engine loop ────────────────────────────────────────────────

    private async Task<FlowNodeState> AdvanceInternalAsync(
        FlowExecutionContext ctx, string nodeId,
        string? agentInput, string transition, bool isStart, CancellationToken ct)
    {
        const int MaxAutoAdvance = 50; // safety cap — prevents infinite loops in bad flow definitions
        var steps = 0;
        string? scriptContext = null; // accumulated script content to attach to the next stopping node

        while (true)
        {
            ctx.CurrentNodeId = nodeId;
            var node = GetNode(ctx.FlowDefinition, nodeId)
                ?? throw new InvalidOperationException($"Node '{nodeId}' not found in flow definition.");

            var nodeType = node["type"]?.GetValue<string>()
                ?? throw new InvalidOperationException($"Node '{nodeId}' has no type.");

            if (!_handlers.TryGetValue(nodeType, out var handler))
                throw new InvalidOperationException($"No handler registered for node type '{nodeType}'.");

            var result = await handler.ExecuteAsync(node, ctx, agentInput, transition, ct);

            var detail = !string.IsNullOrWhiteSpace(result.State.Content) ? Truncate(result.State.Content) : result.State.Condition;
            var sensitiveKeys = CallTraceSnapshot.FindSensitiveKeys(ctx.FlowDefinition);
            var snapshot = CallTraceSnapshot.BuildCrmSnapshot(ctx, sensitiveKeys);
            await _traceRecorder.RecordStepAsync(
                ctx.TenantId, _tenantContext.Current!.SchemaName, ctx.CallRecordId, TraceEngine.Crm, nodeId, nodeType,
                result.State.Label, detail, transitionTaken: transition, result.NextNodeId,
                exitReason: result.State.IsTerminal ? "terminal" : null,
                campaignId: null, ctx.FlowId, dnis: null, ani: null, snapshot, ct);

            // Terminal node or node waiting for input — return state, attaching any preceding script content
            if (result.NextNodeId is null || result.State.IsTerminal)
            {
                // Sub-flow ended — pop call stack and resume parent flow transparently
                if (result.State.IsTerminal && ctx.CallStack.Count > 0)
                {
                    var frame = ctx.CallStack[^1];
                    ctx.CallStack.RemoveAt(ctx.CallStack.Count - 1);
                    ctx.FlowDefinition = JsonNode.Parse(frame.DefinitionJson)?.AsObject() ?? [];
                    nodeId        = frame.ReturnNodeId;
                    agentInput    = null;
                    transition    = "default";
                    scriptContext = null; // don't bleed sub-flow scripts into parent
                    continue;
                }

                result.State.ScriptContext = scriptContext;
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

            // Script node — capture its content and advance through it automatically.
            // The content will be attached to the next node that stops (input/end).
            if (nodeType == "script")
            {
                scriptContext = result.State.Content;
                nodeId        = result.NextNodeId;
                agentInput    = null;
                transition    = "default";
                continue;
            }

            // StartAsync / first-display: stop here and show this node to the agent.
            if (isStart)
            {
                result.State.ScriptContext = scriptContext;
                return result.State;
            }

            // AdvanceAsync: agent acted on this node — advance past it to the next node.
            nodeId     = result.NextNodeId;
            agentInput = null;
            transition = "default";
            isStart    = false;
        }
    }

    // ── Context build/persist ───────────────────────────────────────────────

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

        if (HasAnySections(ctx))
            state.JumpTargets = BuildJumpTargets(ctx);
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
