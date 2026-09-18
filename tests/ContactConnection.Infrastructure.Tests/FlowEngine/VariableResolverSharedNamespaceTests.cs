using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.FlowEngine;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.FlowEngine;

/// <summary>
/// The {{shared.*}} namespace on the CRM resolver — reads VariableContext.SharedVars, which
/// FlowEngine populates fresh from ISharedCallVariableStore on every StartAsync/AdvanceAsync/
/// GetCurrentStateAsync call (not cached across requests). Only the resolver's own dispatch is
/// exercised here (this class had no prior tests); FlowEngine's own population of SharedVars has
/// no automated coverage (FlowEngine has none at all, a known pre-existing gap).
/// </summary>
public class VariableResolverSharedNamespaceTests
{
    private readonly VariableResolver _resolver = new();

    [Fact]
    public void ResolvesASharedVarByPlainKey()
    {
        var ctx = new VariableContext { SharedVars = { ["CC_Capture_Success"] = "true" } };

        var result = _resolver.Resolve("{{shared.CC_Capture_Success}}", ctx);

        Assert.Equal("true", result);
    }

    [Fact]
    public void MissingSharedVar_ResolvesToNotCaptured()
    {
        var ctx = new VariableContext();

        var result = _resolver.Resolve("{{shared.nope}}", ctx);

        Assert.Equal("[not captured]", result);
    }

    [Fact]
    public void SharedVarsAreIndependentOfFlowVars()
    {
        var ctx = new VariableContext
        {
            FlowVars   = { ["x"] = "flow-value" },
            SharedVars = { ["x"] = "shared-value" },
        };

        Assert.Equal("flow-value",   _resolver.Resolve("{{flow.x}}", ctx));
        Assert.Equal("shared-value", _resolver.Resolve("{{shared.x}}", ctx));
    }

    [Fact]
    public void ConditionEvaluation_WorksAgainstASharedVar()
    {
        var ctx = new VariableContext { SharedVars = { ["CC_Capture_Success"] = "true" } };

        Assert.True(_resolver.EvaluateCondition("{{shared.CC_Capture_Success}} == true", ctx));
        Assert.False(_resolver.EvaluateCondition("{{shared.CC_Capture_Success}} == false", ctx));
    }

    [Fact]
    public void SupportsDotIntoJsonObjectStoredInASharedVar_SameConventionAsFlowVars()
    {
        var ctx = new VariableContext { SharedVars = { ["billing"] = "{\"city\":\"Springfield\"}" } };

        Assert.Equal("Springfield", _resolver.Resolve("{{shared.billing.city}}", ctx));
    }
}
