using System.Text.Json;
using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.FlowEngine;
using ContactConnection.Infrastructure.FlowEngine.NodeHandlers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;
using Xunit;
using CrmFlowEngine = ContactConnection.Infrastructure.FlowEngine.FlowEngine;

namespace ContactConnection.Infrastructure.Tests.FlowEngine;

/// <summary>Post-call review (S165, Call Records admin): editing a finished session's variables
/// and re-running one of its api_call nodes — the Life Seasons "order failed to post → fix → resubmit"
/// loop. The re-run goes through the flow's own node handler, against fresh call data, and leaves
/// the session where it was.</summary>
public class FlowSessionReviewTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Mock<IFlowSessionRepository> _sessions = new();
    private readonly Mock<IFlowRepository> _flows = new();
    private readonly Mock<ICallRecordRepository> _callRecords = new();
    private readonly Mock<IDatabase> _redis = new();
    private readonly RecordingApiHandler _apiHandler = new();
    private readonly CallRecord _record;

    public FlowSessionReviewTests()
    {
        _record = CallRecord.Create(_tenantId, Guid.NewGuid(), Guid.NewGuid());
        _record.SetCallerIdentity("Bob", "Corrected", "bob@example.com", null, null);
        _callRecords.Setup(r => r.GetByIdWithInteractionsAsync(_record.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_record);
        _callRecords.Setup(r => r.GetByIdAsync(_record.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_record);
        // Not live in Redis — the agent finished the script.
        _redis.Setup(r => r.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).ReturnsAsync(RedisValue.Null);
    }

    private CrmFlowEngine Engine() => Engine(new Mock<IFlowNotifier>().Object);

    private CrmFlowEngine Engine(IFlowNotifier notifier, params INodeHandler[] extraHandlers)
    {
        var mux = new Mock<IConnectionMultiplexer>();
        mux.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_redis.Object);
        var shared = new Mock<ISharedCallVariableStore>();
        shared.Setup(s => s.GetAllAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        return new CrmFlowEngine(
            _flows.Object, _sessions.Object, new Mock<IAgentRepository>().Object, _callRecords.Object,
            mux.Object, new TenantContext(), notifier, new Mock<ICallTraceRecorder>().Object,
            shared.Object, [_apiHandler, .. extraHandlers], NullLogger<CrmFlowEngine>.Instance,
            Mock.Of<ICardDataRetentionService>());
    }

    private static JsonObject Definition(params (string Id, JsonObject Node)[] nodes)
    {
        var map = new JsonObject();
        foreach (var (id, node) in nodes) map[id] = node;
        return new JsonObject { ["entry_node"] = nodes[0].Id, ["nodes"] = map };
    }

    private static JsonObject ApiNode(bool oncePerCall = true) => new()
    {
        ["type"] = "api_call", ["label"] = "Submit Life Seasons Order", ["outputVariable"] = "order_response",
        ["oncePerCall"] = oncePerCall, ["transitions"] = new JsonObject { ["success"] = "ok", ["error"] = "email_reviewer" },
    };

    private static JsonObject EndNode() => new() { ["type"] = "end", ["label"] = "End" };

    private Flow AddFlow(JsonObject definition)
    {
        var flow = Flow.Create(_tenantId, Guid.NewGuid(), "NeuroQ", FlowType.Crm, definition.ToJsonString());
        _flows.Setup(f => f.GetByIdAsync(flow.Id, It.IsAny<CancellationToken>())).ReturnsAsync(flow);
        return flow;
    }

    private FlowSession AddCompletedSession(Flow flow, Dictionary<string, string> flowVars)
    {
        var session = FlowSession.Create(_tenantId, flow.Id, 1, _record.Id, Guid.NewGuid(), Guid.NewGuid(), "end_1");
        var store = JsonSerializer.Serialize(new { FlowVars = flowVars, Inputs = new Dictionary<string, string>(), ApiResults = new Dictionary<string, string>() });
        session.Complete(store, "[]");
        _sessions.Setup(s => s.GetByIdAsync(session.Id, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        return session;
    }

    private static Dictionary<string, string> FlowVarsOf(FlowSession s) =>
        JsonNode.Parse(s.VariableStore)!["FlowVars"]!.Deserialize<Dictionary<string, string>>()!;

    [Fact]
    public async Task Rerun_FailedOrder_RunsTheNodeWithFreshCallData_AndSavesTheNewResult()
    {
        var flow = AddFlow(Definition(("start", EndNode()), ("n_order", ApiNode()), ("end_1", EndNode())));
        var session = AddCompletedSession(flow, new() { ["order_response.success"] = "false", ["order_response.error"] = "Invalid ZIP" });
        _apiHandler.Succeed = true;

        var result = await Engine().RerunNodeAsync(session.Id, "n_order");

        Assert.True(result.Success);
        Assert.Equal("success", result.Transition);
        Assert.False(result.Replayed);
        Assert.Equal("200", result.StatusCode);
        // The handler saw the corrected call record, not the values captured during the call.
        Assert.Equal("Corrected", _apiHandler.SeenCallerLastName);
        Assert.Equal("n_order", _apiHandler.SeenCurrentNodeId);
        // Persisted — and the session didn't move.
        Assert.Equal("true", FlowVarsOf(session)["order_response.success"]);
        Assert.Equal("end_1", session.CurrentNodeId);
        Assert.Equal(FlowSessionStatus.Complete, session.Status);
        _sessions.Verify(s => s.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Rerun_StillFailing_ReportsTheError()
    {
        var flow = AddFlow(Definition(("n_order", ApiNode()), ("end_1", EndNode())));
        var session = AddCompletedSession(flow, new() { ["order_response.success"] = "false" });
        _apiHandler.Succeed = false;

        var result = await Engine().RerunNodeAsync(session.Id, "n_order");

        Assert.False(result.Success);
        Assert.Equal("error", result.Transition);
        Assert.Equal("Order rejected: bad SKU", result.Error);
    }

    [Fact]
    public async Task Rerun_AlreadySucceededOncePerCall_IsFlaggedAsReplay()
    {
        var flow = AddFlow(Definition(("n_order", ApiNode(oncePerCall: true)), ("end_1", EndNode())));
        var session = AddCompletedSession(flow, new() { ["order_response.success"] = "true" });
        _apiHandler.Succeed = true;

        var result = await Engine().RerunNodeAsync(session.Id, "n_order");

        Assert.True(result.Replayed);
    }

    [Fact]
    public async Task Rerun_FindsTheNodeInACalledSubFlow()
    {
        var sub = AddFlow(Definition(("sub_order", ApiNode()), ("sub_end", EndNode())));
        var main = AddFlow(Definition(
            ("call_sub", new JsonObject { ["type"] = "execute_flow", ["targetFlowId"] = sub.Id.ToString() }),
            ("end_1", EndNode())));
        var session = AddCompletedSession(main, new() { ["order_response.success"] = "false" });
        _apiHandler.Succeed = true;

        var result = await Engine().RerunNodeAsync(session.Id, "sub_order");

        Assert.True(result.Success);
        var snapshot = await Engine().GetSessionSnapshotAsync(session.Id);
        Assert.Contains(snapshot!.ApiCalls, a => a.NodeId == "sub_order" && a.Success == "true");
    }

    [Fact]
    public async Task Rerun_RefusesANodeThatIsNotAnApiCall()
    {
        var flow = AddFlow(Definition(("n_order", ApiNode()), ("end_1", EndNode())));
        var session = AddCompletedSession(flow, []);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Engine().RerunNodeAsync(session.Id, "end_1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Engine().RerunNodeAsync(session.Id, "nope"));
    }

    [Fact]
    public async Task UpdateVariables_SetsAndRemoves_ReturningPreviousValues()
    {
        var flow = AddFlow(Definition(("n_order", ApiNode()), ("end_1", EndNode())));
        var session = AddCompletedSession(flow, new() { ["vendor_number"] = "V1", ["coupon_code"] = "OLD" });

        var previous = await Engine().UpdateSessionVariablesAsync(session.Id,
            new Dictionary<string, string?> { ["vendor_number"] = "V2", ["coupon_code"] = null, ["keycode"] = "K9" });

        Assert.Equal("V1", previous["vendor_number"]);
        Assert.Equal("OLD", previous["coupon_code"]);
        Assert.Null(previous["keycode"]);
        var vars = FlowVarsOf(session);
        Assert.Equal("V2", vars["vendor_number"]);
        Assert.False(vars.ContainsKey("coupon_code"));
        Assert.Equal("K9", vars["keycode"]);
    }

    [Fact]
    public async Task Snapshot_ListsApiCallNodesWithTheirLastResult()
    {
        var flow = AddFlow(Definition(("n_order", ApiNode()), ("end_1", EndNode())));
        var session = AddCompletedSession(flow, new()
        {
            ["order_response.success"] = "false", ["order_response.status_code"] = "400", ["order_response.error"] = "Invalid ZIP",
        });

        var snapshot = await Engine().GetSessionSnapshotAsync(session.Id);

        var api = Assert.Single(snapshot!.ApiCalls);
        Assert.Equal(("n_order", "Submit Life Seasons Order", "false", "400", "Invalid ZIP"),
            (api.NodeId, api.Label, api.Success, api.StatusCode, api.Error));
        Assert.True(api.OncePerCall);
        Assert.False(snapshot.IsLive);
    }

    [Fact]
    public async Task PushLiveUpdate_LiveSession_ReloadsCallData_AndPushesTheCurrentNode()
    {
        var flow = AddFlow(Definition(("n_name", new JsonObject { ["type"] = "input", ["label"] = "Confirm name" }), ("end_1", EndNode())));
        var sessionId = Guid.NewGuid();
        // Live in Redis, holding the name as it was when the script started.
        var entry = new JsonObject
        {
            ["FlowId"] = flow.Id.ToString(), ["FlowVersion"] = 1, ["CallRecordId"] = _record.Id.ToString(),
            ["InteractionId"] = Guid.NewGuid().ToString(), ["AgentId"] = Guid.NewGuid().ToString(), ["TenantId"] = _tenantId.ToString(),
            ["CurrentNodeId"] = "n_name", ["DefinitionJson"] = flow.Definition,
            ["VariableStoreJson"] = "{}", ["ExecutionHistoryJson"] = "[]",
            ["CallRecord"] = new JsonObject(), ["Caller"] = new JsonObject { ["last_name"] = "Typo", ["loyalty_tier"] = "gold" },
            ["Agent"] = new JsonObject(), ["Tenant"] = new JsonObject(),
        };
        _redis.Setup(r => r.StringGetAsync(It.Is<RedisKey>(k => k.ToString().Contains(sessionId.ToString())), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)entry.ToJsonString());
        var input = new RecordingInputHandler();
        var notifier = new Mock<IFlowNotifier>();

        var pushed = await Engine(notifier.Object, input).PushLiveUpdateAsync(sessionId, "Updated by Sue: Customer contact details edited");

        Assert.True(pushed);
        Assert.Equal("Corrected", input.SeenCallerLastName);      // reloaded from the call record
        Assert.Equal("gold", input.SeenLoyaltyTier);               // flow-set caller keys kept
        notifier.Verify(n => n.PushSessionUpdatedAsync(sessionId,
            It.Is<FlowNodeState>(st => st.NodeId == "n_name" && st.FlowName == "NeuroQ"),
            "Updated by Sue: Customer contact details edited", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains(_redis.Invocations, i => i.Method.Name == "StringSetAsync");
    }

    [Fact]
    public async Task PushLiveUpdate_FinishedSession_DoesNothing()
    {
        var notifier = new Mock<IFlowNotifier>();
        Assert.False(await Engine(notifier.Object).PushLiveUpdateAsync(Guid.NewGuid(), "x"));
        notifier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LiveSessionsForAgents_OnlyThoseStillOpenInRedis()
    {
        var flow = AddFlow(Definition(("n_order", ApiNode()), ("end_1", EndNode())));
        var agent = Guid.NewGuid();
        var open = FlowSession.Create(_tenantId, flow.Id, 1, _record.Id, Guid.NewGuid(), agent, "n_order");
        var closedTab = FlowSession.Create(_tenantId, flow.Id, 1, Guid.NewGuid(), Guid.NewGuid(), agent, "n_order");
        _sessions.Setup(s => s.GetActiveForAgentsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([open, closedTab]);
        _redis.Setup(r => r.KeyExistsAsync(It.Is<RedisKey>(k => k.ToString().Contains(open.Id.ToString())), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var live = await Engine().GetLiveSessionsForAgentsAsync([agent]);

        var only = Assert.Single(live);
        Assert.Equal((agent, open.Id, _record.Id, "NeuroQ"), (only.AgentId, only.SessionId, only.CallRecordId, only.FlowName));
    }

    [Fact]
    public async Task Rerun_AuthorizePaymentNode_ReportsTheReauthorization()
    {
        var payNode = new JsonObject
        {
            ["type"] = "authorize_payment", ["label"] = "Authorize card", ["outputVariable"] = "payment",
            ["transitions"] = new JsonObject { ["approved"] = "n_order", ["declined"] = "end_1", ["error"] = "end_1" },
        };
        var flow = AddFlow(Definition(("n_pay", payNode), ("n_order", ApiNode()), ("end_1", EndNode())));
        var session = AddCompletedSession(flow, new() { ["payment.succeeded"] = "True", ["payment.amount"] = "139.90" });

        var result = await Engine(new Mock<IFlowNotifier>().Object, new RecordingPaymentHandler()).RerunNodeAsync(session.Id, "n_pay");

        Assert.True(result.Success);
        Assert.Equal(("approved", "authorize_payment", false), (result.Transition, result.NodeType, result.Replayed));
        Assert.Contains("$142.15", result.Response);
        Assert.Contains("reauthorized", result.Response);
        var snapshot = await Engine(new Mock<IFlowNotifier>().Object, new RecordingPaymentHandler()).GetSessionSnapshotAsync(session.Id);
        var pay = Assert.Single(snapshot!.ApiCalls, a => a.NodeType == "authorize_payment");
        Assert.Equal(("true", "approved"), (pay.Success, pay.StatusCode));
    }

    /// <summary>Stands in for AuthorizePaymentNodeHandler: a void + re-authorization of a corrected total.</summary>
    private sealed class RecordingPaymentHandler : INodeHandler
    {
        public string NodeType => "authorize_payment";

        public Task<NodeResult> ExecuteAsync(JsonObject node, FlowExecutionContext ctx, string? agentInput, string agentTransition, CancellationToken ct = default)
        {
            ctx.FlowVars["payment.succeeded"] = "True";
            ctx.FlowVars["payment.status"] = "approved";
            ctx.FlowVars["payment.amount"] = "142.15";
            ctx.FlowVars["payment.cardLast4"] = "1111";
            ctx.FlowVars["payment.gatewayTransactionId"] = "60012345";
            ctx.FlowVars["payment.action"] = "reauthorized";
            ctx.ExecutionHistory.Add(new NodeExecutionRecord(ctx.CurrentNodeId, "authorize_payment", "Authorize card", DateTimeOffset.UtcNow, null, "approved"));
            var state = new FlowNodeState
            {
                SessionId = ctx.SessionId, CallRecordId = ctx.CallRecordId, NodeId = ctx.CurrentNodeId, NodeType = "authorize_payment", Label = "x",
            };
            return Task.FromResult(new NodeResult(state, "n_order"));
        }
    }

    [Fact]
    public async Task Finalize_LiveSession_ShowsTheAgentTheEnd_AndCompletesIt()
    {
        var flow = AddFlow(Definition(("n_name", new JsonObject { ["type"] = "input", ["label"] = "Name" }), ("end_1", EndNode())));
        var session = FlowSession.Create(_tenantId, flow.Id, 1, _record.Id, Guid.NewGuid(), Guid.NewGuid(), "n_name");
        _sessions.Setup(s => s.GetByIdAsync(session.Id, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        var entry = new JsonObject
        {
            ["FlowId"] = flow.Id.ToString(), ["FlowVersion"] = 1, ["CallRecordId"] = _record.Id.ToString(),
            ["InteractionId"] = Guid.NewGuid().ToString(), ["AgentId"] = session.AgentId.ToString(), ["TenantId"] = _tenantId.ToString(),
            ["CurrentNodeId"] = "n_name", ["DefinitionJson"] = flow.Definition, ["VariableStoreJson"] = "{}", ["ExecutionHistoryJson"] = "[]",
            ["CallRecord"] = new JsonObject(), ["Caller"] = new JsonObject(), ["Agent"] = new JsonObject(), ["Tenant"] = new JsonObject(),
        };
        _redis.Setup(r => r.StringGetAsync(It.Is<RedisKey>(k => k.ToString().Contains(session.Id.ToString())), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)entry.ToJsonString());
        var notifier = new Mock<IFlowNotifier>();

        Assert.True(await Engine(notifier.Object).FinalizeSessionAsync(session.Id, "Call finalized by Sue: relieved"));

        notifier.Verify(n => n.PushSessionUpdatedAsync(session.Id,
            It.Is<FlowNodeState>(st => st.NodeType == "end" && st.IsTerminal), "Call finalized by Sue: relieved", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(FlowSessionStatus.Complete, session.Status);
        _redis.Verify(r => r.KeyDeleteAsync(It.Is<RedisKey>(k => k.ToString().Contains(session.Id.ToString())), It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task Finalize_OrphanedSession_JustCompletes_AndAFinishedOneIsANoOp()
    {
        var flow = AddFlow(Definition(("n_order", ApiNode()), ("end_1", EndNode())));
        var orphan = FlowSession.Create(_tenantId, flow.Id, 1, _record.Id, Guid.NewGuid(), Guid.NewGuid(), "n_order");
        _sessions.Setup(s => s.GetByIdAsync(orphan.Id, It.IsAny<CancellationToken>())).ReturnsAsync(orphan);
        var finished = AddCompletedSession(flow, []);
        var notifier = new Mock<IFlowNotifier>();

        Assert.True(await Engine(notifier.Object).FinalizeSessionAsync(orphan.Id, "x"));
        Assert.Equal(FlowSessionStatus.Complete, orphan.Status);
        notifier.Verify(n => n.PushSessionUpdatedAsync(It.IsAny<Guid>(), It.IsAny<FlowNodeState>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        Assert.False(await Engine(notifier.Object).FinalizeSessionAsync(finished.Id, "x"));
    }

    [Fact]
    public async Task TakeOver_MovesTheOpenScriptToTheSupervisor_WithEverythingCaptured()
    {
        var flow = AddFlow(Definition(("n_name", new JsonObject { ["type"] = "input", ["label"] = "Confirm name" }), ("end_1", EndNode())));
        var agentId = Guid.NewGuid();
        var supervisorId = Guid.NewGuid();
        var session = FlowSession.Create(_tenantId, flow.Id, 1, _record.Id, Guid.NewGuid(), agentId, "n_name");
        _sessions.Setup(s => s.GetByIdAsync(session.Id, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        var entry = new JsonObject
        {
            ["FlowId"] = flow.Id.ToString(), ["FlowVersion"] = 1, ["CallRecordId"] = _record.Id.ToString(),
            ["InteractionId"] = Guid.NewGuid().ToString(), ["AgentId"] = agentId.ToString(), ["TenantId"] = _tenantId.ToString(),
            ["CurrentNodeId"] = "n_name", ["DefinitionJson"] = flow.Definition,
            ["VariableStoreJson"] = """{"FlowVars":{"coupon_code":"SAVE10"},"Inputs":{"v1_first_name":"Bob"},"ApiResults":{}}""",
            ["ExecutionHistoryJson"] = "[]",
            ["CallRecord"] = new JsonObject(), ["Caller"] = new JsonObject(), ["Agent"] = new JsonObject(), ["Tenant"] = new JsonObject(),
        };
        _redis.Setup(r => r.StringGetAsync(It.Is<RedisKey>(k => k.ToString().Contains(session.Id.ToString())), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)entry.ToJsonString());
        var notifier = new Mock<IFlowNotifier>();
        var input = new RecordingInputHandler();

        var state = await Engine(notifier.Object, input).TakeOverSessionAsync(session.Id, supervisorId, "Call taken over by Sue");

        Assert.NotNull(state);
        Assert.Equal("n_name", state!.NodeId);
        Assert.Equal(supervisorId, session.AgentId);
        // The previous agent's tab was told to finish.
        notifier.Verify(n => n.PushSessionUpdatedAsync(session.Id,
            It.Is<FlowNodeState>(st => st.NodeType == "end" && st.IsTerminal), "Call taken over by Sue", It.IsAny<CancellationToken>()), Times.Once);
        // Everything captured so far came along (persisted under the new owner).
        Assert.Contains("SAVE10", session.VariableStore);
        Assert.Contains("Bob", session.VariableStore);
        var saved = _redis.Invocations.Where(i => i.Method.Name == "StringSetAsync").Select(i => i.Arguments[1].ToString()).Last();
        Assert.Contains(supervisorId.ToString(), saved);
        notifier.Verify(n => n.PushAgentSessionsChangedAsync(_tenantId, agentId, It.IsAny<CancellationToken>()), Times.Once);
        notifier.Verify(n => n.PushAgentSessionsChangedAsync(_tenantId, supervisorId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TakeOver_NothingOpen_ReturnsNull()
    {
        var flow = AddFlow(Definition(("n_order", ApiNode()), ("end_1", EndNode())));
        var finished = AddCompletedSession(flow, []);
        Assert.Null(await Engine().TakeOverSessionAsync(finished.Id, Guid.NewGuid(), "x"));
    }

    [Fact]
    public async Task ScriptEnd_CompletesTheInteraction_WithTheFlowsDisposition_AndFixesAnEndedCallsStatus()
    {
        // S169: the caller hung up first (status derived "incomplete" — interaction still active), then
        // the agent finished the script in ACW.
        var interaction = _record.AddInteraction(InteractionType.OrderSale);
        _record.Complete();
        Assert.Equal(CallRecordStatus.Incomplete, _record.OverallStatus);
        _record.UpdateCustomFieldsSnapshot("""{"call_type":"Junk","disposition":"Test Call"}""");

        var flow = AddFlow(Definition(("n_order", ApiNode()), ("end_1", EndNode())));
        var session = FlowSession.Create(_tenantId, flow.Id, 1, _record.Id, interaction.Id, Guid.NewGuid(), "end_1");
        _sessions.Setup(s => s.GetByIdAsync(session.Id, It.IsAny<CancellationToken>())).ReturnsAsync(session);

        Assert.True(await Engine().FinalizeSessionAsync(session.Id, "x"));   // runs the normal completion path

        Assert.Equal((InteractionStatus.Complete, "Test Call"), (interaction.Status, interaction.Disposition));
        Assert.Equal(CallRecordStatus.Complete, _record.OverallStatus);
    }

    [Fact]
    public void Disposition_FallsBackToTheFlowVariable()
    {
        var ctx = new FlowExecutionContext { CallRecordId = _record.Id };
        ctx.FlowVars["disposition"] = "Sale";
        Assert.Equal("Sale", CrmFlowEngine.DispositionOf(_record, ctx));
        ctx.FlowVars.Clear();
        Assert.Null(CrmFlowEngine.DispositionOf(_record, ctx));
    }

    private sealed class RecordingInputHandler : INodeHandler
    {
        public string NodeType => "input";
        public string? SeenCallerLastName { get; private set; }
        public string? SeenLoyaltyTier { get; private set; }

        public Task<NodeResult> ExecuteAsync(JsonObject node, FlowExecutionContext ctx, string? agentInput, string agentTransition, CancellationToken ct = default)
        {
            SeenCallerLastName = ctx.Caller.GetValueOrDefault("last_name");
            SeenLoyaltyTier = ctx.Caller.GetValueOrDefault("loyalty_tier");
            var state = new FlowNodeState
            {
                SessionId = ctx.SessionId, CallRecordId = ctx.CallRecordId, NodeId = ctx.CurrentNodeId, NodeType = "input", Label = "Confirm name",
            };
            return Task.FromResult(new NodeResult(state, null));
        }
    }

    /// <summary>Stands in for ApiCallNodeHandler: records what it saw, writes the wrapper's output vars.</summary>
    private sealed class RecordingApiHandler : INodeHandler
    {
        public string NodeType => "api_call";
        public bool Succeed { get; set; }
        public string? SeenCallerLastName { get; private set; }
        public string? SeenCurrentNodeId { get; private set; }

        public Task<NodeResult> ExecuteAsync(JsonObject node, FlowExecutionContext ctx, string? agentInput, string agentTransition, CancellationToken ct = default)
        {
            SeenCallerLastName = ctx.Caller.GetValueOrDefault("last_name");
            SeenCurrentNodeId = ctx.CurrentNodeId;
            var output = node["outputVariable"]!.GetValue<string>();
            ctx.FlowVars[$"{output}.success"] = Succeed ? "true" : "false";
            ctx.FlowVars[$"{output}.status_code"] = Succeed ? "200" : "400";
            ctx.FlowVars[$"{output}.error"] = Succeed ? "" : "Order rejected: bad SKU";
            ctx.FlowVars[$"{output}.timed_out"] = "false";
            ctx.ExecutionHistory.Add(new NodeExecutionRecord(ctx.CurrentNodeId, "api_call", "x", DateTimeOffset.UtcNow, null, null));
            var state = new FlowNodeState
            {
                SessionId = ctx.SessionId, CallRecordId = ctx.CallRecordId, NodeId = ctx.CurrentNodeId, NodeType = "api_call", Label = "x",
            };
            return Task.FromResult(new NodeResult(state, Succeed ? "ok" : "email_reviewer"));
        }
    }
}
