using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Telephony.NodeHandlers;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony.NodeHandlers;

/// <summary>
/// tf_branch's condition evaluator gates every conditional path in every telephony flow, but had
/// zero coverage of its own — including of the exact regression called out in its source comment
/// (a {{flow.*}} condition used to silently resolve to false because raw ctx.Vars was keyed
/// without the resolver's prefix-stripping). Covers operator selection/precedence, quoting,
/// numeric-parse-failure fallthrough, and the true/false/default transition wiring.
/// </summary>
public class TelBranchNodeHandlerTests
{
    private static readonly TelBranchNodeHandler Handler = new();

    private static TelephonyFlowContext Ctx() => new()
    {
        ChannelUuid       = "uuid-1",
        CallerNumber      = "+15551234567",
        DestinationNumber = "+15557654321",
        TenantId          = Guid.NewGuid(),
        CampaignId        = Guid.NewGuid(),
        CallRecordId      = Guid.NewGuid(),
        TenantSubdomain   = "test-tenant",
        TenantSchemaName  = "tenant_test_tenant",
        TenantTimezone    = "America/Chicago",
    };

    private static JsonObject Node(string condition) => new()
    {
        ["type"]      = "tf_branch",
        ["condition"] = condition,
        ["transitions"] = new JsonObject
        {
            ["true"]  = "node_true",
            ["false"] = "node_false",
        },
    };

    [Theory]
    [InlineData("5 == 5", true)]
    [InlineData("5 == 6", false)]
    [InlineData("abc == abc", true)]
    [InlineData("abc == def", false)]
    public async Task Equality(string condition, bool expectTrue)
    {
        var result = await Handler.ExecuteAsync(Node(condition), Ctx());
        Assert.Equal(expectTrue ? "node_true" : "node_false", result.NextNodeId);
        Assert.Equal(expectTrue ? "true" : "false", result.TransitionTaken);
    }

    [Theory]
    [InlineData("5 != 6", true)]
    [InlineData("5 != 5", false)]
    public async Task Inequality(string condition, bool expectTrue)
    {
        var result = await Handler.ExecuteAsync(Node(condition), Ctx());
        Assert.Equal(expectTrue ? "node_true" : "node_false", result.NextNodeId);
    }

    [Theory]
    [InlineData("10 > 5", true)]
    [InlineData("5 > 10", false)]
    [InlineData("5 < 10", true)]
    [InlineData("10 < 5", false)]
    [InlineData("5 >= 5", true)]
    [InlineData("4 >= 5", false)]
    [InlineData("5 <= 5", true)]
    [InlineData("6 <= 5", false)]
    public async Task NumericComparisons(string condition, bool expectTrue)
    {
        var result = await Handler.ExecuteAsync(Node(condition), Ctx());
        Assert.Equal(expectTrue ? "node_true" : "node_false", result.NextNodeId);
    }

    [Fact]
    public async Task NumericOperator_NonNumericOperands_FallsThroughToFalse()
    {
        var result = await Handler.ExecuteAsync(Node("abc > def"), Ctx());
        Assert.Equal("node_false", result.NextNodeId);
    }

    [Theory]
    [InlineData("hello world contains world", true)]
    [InlineData("HELLO WORLD contains world", true)]
    [InlineData("hello world contains xyz", false)]
    public async Task Contains_IsCaseInsensitive(string condition, bool expectTrue)
    {
        var result = await Handler.ExecuteAsync(Node(condition), Ctx());
        Assert.Equal(expectTrue ? "node_true" : "node_false", result.NextNodeId);
    }

    [Fact]
    public async Task QuotedOperands_AreUnwrappedBeforeComparison()
    {
        var result = await Handler.ExecuteAsync(Node("\"sale\" == \"sale\""), Ctx());
        Assert.Equal("node_true", result.NextNodeId);
    }

    [Fact]
    public async Task OperatorPrecedence_GreaterOrEqual_NotMisreadAsGreaterThan()
    {
        // ">=" must be matched before the bare ">" check, or "5 >= 5" would split on ">" into
        // left="5 " right="= 5" and fail numeric parsing on the right side.
        var result = await Handler.ExecuteAsync(Node("5 >= 5"), Ctx());
        Assert.Equal("node_true", result.NextNodeId);
    }

    [Fact]
    public async Task FlowPrefixedVariable_ResolvesCorrectly_RegressionForPastBug()
    {
        // Source comment documents a real, fixed bug: {{flow.*}} conditions used to always
        // resolve to false because the old code looked ctx.Vars up keyed on the literal
        // "flow.varname" string, which never matched a bare-keyed Vars dictionary.
        var ctx = Ctx();
        ctx.Vars["is_existing_customer"] = "true";

        var result = await Handler.ExecuteAsync(Node("{{flow.is_existing_customer}} == true"), ctx);

        Assert.Equal("node_true", result.NextNodeId);
    }

    [Fact]
    public async Task WellKnownNamespace_CallerAni_ResolvesBeforeComparison()
    {
        var ctx = Ctx();
        var result = await Handler.ExecuteAsync(Node("{{caller.ani}} == +15551234567"), ctx);
        Assert.Equal("node_true", result.NextNodeId);
    }

    [Fact]
    public async Task EmptyCondition_FollowsFalseTransition()
    {
        var result = await Handler.ExecuteAsync(Node(""), Ctx());
        Assert.Equal("node_false", result.NextNodeId);
        Assert.Equal("false", result.TransitionTaken);
    }

    [Fact]
    public async Task WhitespaceOnlyCondition_FollowsFalseTransition()
    {
        var result = await Handler.ExecuteAsync(Node("   "), Ctx());
        Assert.Equal("node_false", result.NextNodeId);
    }

    [Fact]
    public async Task NoOperatorFound_FollowsFalseTransition()
    {
        var result = await Handler.ExecuteAsync(Node("just a plain string"), Ctx());
        Assert.Equal("node_false", result.NextNodeId);
    }

    [Fact]
    public async Task NoTrueFalseTransitions_FallsBackToDefault()
    {
        var node = new JsonObject
        {
            ["type"]      = "tf_branch",
            ["condition"] = "5 == 5",
            ["transitions"] = new JsonObject { ["default"] = "node_fallback" },
        };

        var result = await Handler.ExecuteAsync(node, Ctx());
        Assert.Equal("node_fallback", result.NextNodeId);
    }

    [Fact]
    public async Task NoTransitionsAtAll_NextNodeIdIsNull()
    {
        var node = new JsonObject { ["type"] = "tf_branch", ["condition"] = "5 == 5" };
        var result = await Handler.ExecuteAsync(node, Ctx());
        Assert.Null(result.NextNodeId);
        Assert.Equal("true", result.TransitionTaken);
    }
}
