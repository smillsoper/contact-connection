using System.Text.Json.Nodes;
using ContactConnection.Infrastructure.FlowEngine;
using ContactConnection.Infrastructure.FlowEngine.NodeHandlers;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.FlowEngine.NodeHandlers;

/// <summary>
/// CRM "address" node — agent captures/validates a mailing address. Focused on the isVerified
/// wiring: true only when the stored address is exactly what the vendor validation API returned
/// (exact match, an accepted correction, or a selected multiple-match candidate); false whenever
/// the agent overrides with an unconfirmed value, or validation never ran at all.
/// </summary>
public class AddressNodeHandlerTests
{
    private static AddressNodeHandler NewHandler() => new(new VariableResolver());

    private static FlowExecutionContext Ctx() => new()
    {
        SessionId = Guid.NewGuid(), FlowId = Guid.NewGuid(), FlowVersion = 1,
        CallRecordId = Guid.NewGuid(), InteractionId = Guid.NewGuid(), AgentId = Guid.NewGuid(),
        TenantId = Guid.NewGuid(), CurrentNodeId = "n_address",
    };

    private static JsonObject Node() => new()
    {
        ["type"]           = "address",
        ["label"]          = "Billing Address",
        ["outputVariable"] = "billing_address",
        ["requiredFields"] = new JsonArray("firstName", "lastName", "address1", "zip", "city", "state"),
        ["transitions"]    = new JsonObject { ["default"] = "n_next" },
    };

    private static string Submission(string? isVerified = null) => new JsonObject
    {
        ["firstName"] = "John",
        ["lastName"]  = "Doe",
        ["address1"]  = "123 Main St",
        ["city"]      = "Springfield",
        ["state"]     = "or",
        ["zip"]       = "97477",
        ["country"]   = "US",
        ["isVerified"] = isVerified,
    }.ToJsonString();

    private static JsonObject StoredAddress(FlowExecutionContext ctx) =>
        JsonNode.Parse(ctx.FlowVars["billing_address"])!.AsObject();

    [Fact]
    public async Task FirstDisplay_ReturnsFormState_DoesNotAdvance()
    {
        var ctx = Ctx();
        var result = await NewHandler().ExecuteAsync(Node(), ctx, agentInput: null, agentTransition: "");

        Assert.Null(result.NextNodeId);
        Assert.False(ctx.FlowVars.ContainsKey("billing_address"));
    }

    [Fact]
    public async Task NoValidationOutcomeSupplied_IsVerifiedDefaultsFalse()
    {
        var ctx = Ctx();
        var result = await NewHandler().ExecuteAsync(Node(), ctx, agentInput: Submission(isVerified: null), agentTransition: "");

        Assert.Equal("n_next", result.NextNodeId);
        Assert.False(StoredAddress(ctx)["isVerified"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ExplicitIsVerifiedTrue_ExactMatchOrAcceptedCorrection_StoredAsTrue()
    {
        var ctx = Ctx();
        var result = await NewHandler().ExecuteAsync(Node(), ctx, agentInput: Submission(isVerified: "true"), agentTransition: "");

        Assert.Equal("n_next", result.NextNodeId);
        Assert.True(StoredAddress(ctx)["isVerified"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ExplicitIsVerifiedFalse_AgentKeptUnconfirmedValue_StoredAsFalse()
    {
        var ctx = Ctx();
        var result = await NewHandler().ExecuteAsync(Node(), ctx, agentInput: Submission(isVerified: "false"), agentTransition: "");

        Assert.Equal("n_next", result.NextNodeId);
        Assert.False(StoredAddress(ctx)["isVerified"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData("True")]
    [InlineData("TRUE")]
    public async Task IsVerifiedComparison_IsCaseInsensitive(string value)
    {
        var ctx = Ctx();
        await NewHandler().ExecuteAsync(Node(), ctx, agentInput: Submission(isVerified: value), agentTransition: "");

        Assert.True(StoredAddress(ctx)["isVerified"]!.GetValue<bool>());
    }

    [Fact]
    public async Task GarbageIsVerifiedValue_TreatedAsFalse()
    {
        var ctx = Ctx();
        await NewHandler().ExecuteAsync(Node(), ctx, agentInput: Submission(isVerified: "yes"), agentTransition: "");

        Assert.False(StoredAddress(ctx)["isVerified"]!.GetValue<bool>());
    }

    [Fact]
    public async Task JumpBack_PrefillsFormFromFlowVars_IncludingAfterVerifiedSubmission()
    {
        var ctx = Ctx();
        var handler = NewHandler();

        // Agent submits a validated address, advancing past the node...
        await handler.ExecuteAsync(Node(), ctx, agentInput: Submission(isVerified: "true"), agentTransition: "");

        // ...then jumps back to the same node. FlowVars still holds the stored (normalized)
        // address, so the re-displayed form must prefill from it rather than showing blank/stale
        // data — this is the mechanism the frontend's stable prefilledAddress key depends on.
        ctx.CurrentNodeId = "n_address";
        var result = await handler.ExecuteAsync(Node(), ctx, agentInput: null, agentTransition: "");

        Assert.Null(result.NextNodeId);
        Assert.NotNull(result.State.PrefilledAddress);
        Assert.Equal("John", result.State.PrefilledAddress!["firstName"]);
        Assert.Equal("123 Main St", result.State.PrefilledAddress!["address1"]);
    }

    [Fact]
    public async Task MissingRequiredField_ReturnsValidationError_DoesNotAdvanceOrStore()
    {
        var ctx = Ctx();
        var badSubmission = new JsonObject { ["firstName"] = "John" }.ToJsonString();

        var result = await NewHandler().ExecuteAsync(Node(), ctx, agentInput: badSubmission, agentTransition: "");

        Assert.Null(result.NextNodeId);
        Assert.NotNull(result.State.ValidationError);
        Assert.False(ctx.FlowVars.ContainsKey("billing_address"));
    }
}
