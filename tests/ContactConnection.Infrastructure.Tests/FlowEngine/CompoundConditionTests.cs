using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.FlowEngine;
using ContactConnection.Infrastructure.Telephony.NodeHandlers;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.FlowEngine;

/// <summary>Branch conditions joined with && / || (S179) — CRM branch and telephony tf_branch.</summary>
public class CompoundConditionTests
{
    private readonly VariableResolver _resolver = new();

    private static VariableContext Ctx(string captured, string practiceRun) => new()
    {
        SharedVars = { ["CC_Capture_Success"] = captured },
        CallRecord = { ["practice_run"] = practiceRun },
    };

    private const string CaptureOrPractice = "{{shared.CC_Capture_Success}} == true || {{call_record.practice_run}} == true";

    [Theory]
    [InlineData("true", "false", true)]
    [InlineData("false", "true", true)]
    [InlineData("false", "false", false)]
    public void Or_IsTrueWhenEitherSideIs(string captured, string practice, bool expected) =>
        Assert.Equal(expected, _resolver.EvaluateCondition(CaptureOrPractice, Ctx(captured, practice)));

    [Theory]
    [InlineData("true", "true", true)]
    [InlineData("true", "false", false)]
    [InlineData("false", "true", false)]
    public void And_NeedsBothSides(string captured, string practice, bool expected) =>
        Assert.Equal(expected, _resolver.EvaluateCondition(
            "{{shared.CC_Capture_Success}} == true && {{call_record.practice_run}} == true", Ctx(captured, practice)));

    [Fact]
    public void And_BindsTighterThanOr()
    {
        var ctx = new VariableContext { FlowVars = { ["a"] = "1", ["b"] = "0", ["c"] = "0" } };
        // a || (b && c) = true; (a || b) && c would be false
        Assert.True(_resolver.EvaluateCondition("{{flow.a}} == 1 || {{flow.b}} == 1 && {{flow.c}} == 1", ctx));
    }

    [Fact]
    public void OperatorsInsideAResolvedValue_NeverSplitTheCondition()
    {
        // The customer's note contains "||" — it's data, not an operator, because the split happens before resolving.
        var ctx = new VariableContext { FlowVars = { ["note"] = "x || true" } };
        Assert.False(_resolver.EvaluateCondition("{{flow.note}} == y", ctx));
    }

    [Fact]
    public void OperatorsInsideAQuotedLiteral_AreText()
    {
        var ctx = new VariableContext { FlowVars = { ["s"] = "a && b" } };
        Assert.True(_resolver.EvaluateCondition("{{flow.s}} == \"a && b\"", ctx));
    }

    [Fact]
    public void DanglingOperator_IsFalse_NotAnError()
    {
        var ctx = new VariableContext { FlowVars = { ["a"] = "1" } };
        Assert.False(_resolver.EvaluateCondition("{{flow.a}} == 1 &&", ctx));
        Assert.True(_resolver.EvaluateCondition("{{flow.a}} == 1 ||", ctx));
    }

    [Theory]
    [InlineData("1", "0", "true")]
    [InlineData("0", "1", "true")]
    [InlineData("0", "0", "false")]
    public async Task TelephonyBranch_SupportsOr(string a, string b, string expected)
    {
        var ctx = new TelephonyFlowContext
        {
            ChannelUuid = "u", CallerNumber = "+15551110000", DestinationNumber = "+15552220000",
            TenantId = Guid.NewGuid(), CampaignId = Guid.NewGuid(), CallRecordId = Guid.NewGuid(),
            TenantSubdomain = "test-tenant", TenantSchemaName = "tenant_test_tenant", TenantTimezone = "America/Chicago",
        };
        ctx.Vars["a"] = a;
        ctx.Vars["b"] = b;
        var node = new JsonObject
        {
            ["condition"] = "{{flow.a}} == 1 || {{flow.b}} == 1",
            ["transitions"] = new JsonObject { ["true"] = "T", ["false"] = "F" },
        };

        var result = await new TelBranchNodeHandler().ExecuteAsync(node, ctx);

        Assert.Equal(expected, result.TransitionTaken);
    }
}
