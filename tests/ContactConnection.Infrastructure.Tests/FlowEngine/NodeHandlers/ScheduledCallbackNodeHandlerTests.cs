using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.FlowEngine;
using ContactConnection.Infrastructure.FlowEngine.NodeHandlers;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.FlowEngine.NodeHandlers;

/// <summary>
/// CRM-script scheduled_callback node — an agent books a customer callback for a future time.
/// Shares ScheduledCallbackTimeParser + ScheduledCallback.Create with the telephony node; these
/// tests cover the CRM-specific bits (number template / phone-object extraction, campaign+DNIS
/// from the call record, outcome-driven transitions, output variable).
/// </summary>
public class ScheduledCallbackNodeHandlerTests
{
    private static readonly Guid TenantId  = Guid.Parse("eeeeeeee-0000-0000-0000-0000000000aa");
    private static readonly Guid CallId    = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001");
    private static readonly Guid Campaign  = Guid.Parse("eeeeeeee-0000-0000-0000-0000000000bb");
    private static readonly Guid TargetFlow = Guid.Parse("eeeeeeee-0000-0000-0000-0000000000ff");

    private static string FutureDate => DateTime.UtcNow.AddDays(3).ToString("yyyy-MM-dd");

    private readonly Mock<IScheduledCallbackRepository> _repo = new();
    private ScheduledCallback? _saved;

    private ScheduledCallbackNodeHandler NewHandler()
    {
        _repo.Setup(r => r.AddAsync(It.IsAny<ScheduledCallback>(), It.IsAny<CancellationToken>()))
            .Callback<ScheduledCallback, CancellationToken>((c, _) => _saved = c)
            .Returns(Task.CompletedTask);
        _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return new ScheduledCallbackNodeHandler(new VariableResolver(), _repo.Object);
    }

    private static FlowExecutionContext Ctx()
    {
        var ctx = new FlowExecutionContext
        {
            SessionId = Guid.NewGuid(), FlowId = Guid.NewGuid(), FlowVersion = 1,
            CallRecordId = CallId, InteractionId = Guid.NewGuid(), AgentId = Guid.NewGuid(),
            TenantId = TenantId, CurrentNodeId = "n_sc",
        };
        ctx.Tenant["timezone"] = "America/Chicago";
        ctx.CallRecord["campaign_id"] = Campaign.ToString();
        ctx.CallRecord["dnis"] = "+15419196582";
        return ctx;
    }

    private static JsonObject Node(JsonObject? over = null)
    {
        var n = new JsonObject
        {
            ["type"]  = "scheduled_callback",
            ["label"] = "Book a callback",
            ["callbackNumber"]     = "{{flow.customer_phone}}",
            ["scheduledDateValue"] = FutureDate,
            ["scheduledTimeValue"] = "14:00",
            ["targetFlowId"]       = TargetFlow.ToString(),
            ["outputVariable"]     = "scb",
            ["windowMinutes"]      = 90,
            ["maxAttempts"]        = 2,
            ["transitions"] = new JsonObject
            {
                ["scheduled"] = "n_ok", ["invalid_time"] = "n_bad", ["failed"] = "n_fail",
            },
        };
        if (over is not null) foreach (var kv in over) n[kv.Key] = kv.Value?.DeepClone();
        return n;
    }

    [Fact]
    public async Task ValidFuture_CreatesRow_FollowsScheduled_SetsOutputVar()
    {
        var ctx = Ctx();
        ctx.FlowVars["customer_phone"] = "+15551230000";

        var result = await NewHandler().ExecuteAsync(Node(), ctx, agentInput: null, agentTransition: "");

        Assert.Equal("n_ok", result.NextNodeId);
        Assert.NotNull(_saved);
        Assert.Equal("+15551230000", _saved!.CallbackNumber);
        Assert.Equal(Campaign, _saved.CampaignId);
        Assert.Equal("+15419196582", _saved.Dnis);
        Assert.Equal(TargetFlow, _saved.TargetFlowId);
        Assert.Equal(2, _saved.MaxAttempts);
        Assert.True(_saved.ScheduledFor > DateTimeOffset.UtcNow);

        var outVar = JsonNode.Parse(ctx.FlowVars["scb"])!.AsObject();
        Assert.Equal("scheduled", (string?)outVar["outcome"]);
        Assert.Equal(_saved.Id.ToString(), (string?)outVar["id"]);
    }

    [Fact]
    public async Task PhoneObjectNumber_ExtractsValue()
    {
        var ctx = Ctx();
        ctx.FlowVars["customer_phone"] =
            """{"value":"+15551239999","display_value":"(555) 123-9999","isTollFree":false}""";

        await NewHandler().ExecuteAsync(Node(), ctx, null, "");

        Assert.Equal("+15551239999", _saved!.CallbackNumber);
    }

    [Fact]
    public async Task NoNumber_FollowsFailed_NoRow()
    {
        var ctx = Ctx();   // customer_phone unset → template resolves to "[not captured]"

        var result = await NewHandler().ExecuteAsync(Node(), ctx, null, "");

        Assert.Equal("n_fail", result.NextNodeId);
        Assert.Null(_saved);
        var outVar = JsonNode.Parse(ctx.FlowVars["scb"])!.AsObject();
        Assert.Equal("failed", (string?)outVar["outcome"]);
    }

    [Fact]
    public async Task PastDate_FollowsInvalidTime_NoRow()
    {
        var ctx = Ctx();
        ctx.FlowVars["customer_phone"] = "+15551230000";
        var node = Node(new JsonObject { ["scheduledDateValue"] = DateTime.UtcNow.AddDays(-2).ToString("yyyy-MM-dd") });

        var result = await NewHandler().ExecuteAsync(node, ctx, null, "");

        Assert.Equal("n_bad", result.NextNodeId);
        Assert.Null(_saved);
    }

    [Fact]
    public async Task OutsideAllowedHours_FollowsInvalidTime()
    {
        var ctx = Ctx();
        ctx.FlowVars["customer_phone"] = "+15551230000";
        var node = Node(new JsonObject { ["allowedStartTime"] = "08:00", ["allowedEndTime"] = "12:00" }); // node time is 14:00

        var result = await NewHandler().ExecuteAsync(node, ctx, null, "");

        Assert.Equal("n_bad", result.NextNodeId);
        Assert.Null(_saved);
    }
}
