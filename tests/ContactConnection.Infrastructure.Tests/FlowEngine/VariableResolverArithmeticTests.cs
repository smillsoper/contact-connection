using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.FlowEngine;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.FlowEngine;

/// <summary>
/// A single arithmetic operator applied to a variable reference inside one {{...}} tag — e.g.
/// {{flow.auth_attempts + 1}} — added to support counter-style set_variable assignments (found live,
/// Session 161: a script author tried {{flow.auth_attempts}} + 1 as a plain string template, which
/// just concatenated the literal " + 1" instead of adding). Deliberately requires the operator+operand
/// to be written inside the same tag as the variable reference, not inferred from a resolved value —
/// otherwise ordinary hyphenated data (a phone number, a date) would misfire as subtraction.
/// </summary>
public class VariableResolverArithmeticTests
{
    private readonly VariableResolver _resolver = new();

    [Fact]
    public void AddsALiteralToAFlowVar()
    {
        var ctx = new VariableContext { FlowVars = { ["auth_attempts"] = "2" } };

        Assert.Equal("3", _resolver.Resolve("{{flow.auth_attempts + 1}}", ctx));
    }

    [Fact]
    public void MissingBaseVar_TreatedAsZero_SoACounterNeedsNoInitializer()
    {
        var ctx = new VariableContext();

        Assert.Equal("1", _resolver.Resolve("{{flow.never_set + 1}}", ctx));
    }

    [Fact]
    public void SupportsSubtractMultiplyDivide()
    {
        var ctx = new VariableContext { FlowVars = { ["x"] = "10" } };

        Assert.Equal("7",   _resolver.Resolve("{{flow.x - 3}}", ctx));
        Assert.Equal("20",  _resolver.Resolve("{{flow.x * 2}}", ctx));
        Assert.Equal("5",   _resolver.Resolve("{{flow.x / 2}}", ctx));
    }

    [Fact]
    public void WholeNumberResult_HasNoTrailingDecimalPoint()
    {
        // Both for clean display and so a numeric branch comparison (>= 3) works against the
        // formatted string the same way a plain literal "3" would.
        var ctx = new VariableContext { FlowVars = { ["x"] = "4" } };

        Assert.Equal("2", _resolver.Resolve("{{flow.x / 2}}", ctx));
    }

    [Fact]
    public void FractionalResult_KeepsDecimal()
    {
        var ctx = new VariableContext { FlowVars = { ["x"] = "5" } };

        Assert.Equal("2.5", _resolver.Resolve("{{flow.x / 2}}", ctx));
    }

    [Fact]
    public void IncrementedCounter_SatisfiesANumericBranchCondition()
    {
        var ctx = new VariableContext { FlowVars = { ["auth_attempts"] = "2" } };
        var incremented = _resolver.Resolve("{{flow.auth_attempts + 1}}", ctx);
        ctx.FlowVars["auth_attempts"] = incremented;

        Assert.True(_resolver.EvaluateCondition("{{flow.auth_attempts}} >= 3", ctx));
    }

    [Fact]
    public void NoWhitespaceAroundHyphen_NotTreatedAsArithmetic()
    {
        // A tag written without spaces around a hyphen (e.g. a variable/key name that happens to
        // contain one) must not be misparsed as subtraction — the operator requires surrounding
        // whitespace precisely to avoid this. (This tag has no matching flow var, so it resolves to
        // empty, same as any other unresolved reference — the point is it does NOT throw or attempt
        // "order" minus "2023".)
        var ctx = new VariableContext();

        Assert.Equal("", _resolver.Resolve("{{flow.order-2023}}", ctx));
    }

    [Fact]
    public void ResolvedValueLookingLikeMath_IsNotEvaluated_OnlyTheTemplateItselfTriggersIt()
    {
        // The arithmetic marker must come from what the script author typed in the template, never
        // from data that happens to resolve to something math-shaped (e.g. a hyphenated phone
        // number). A plain {{flow.phone}} reference must pass the value through untouched.
        var ctx = new VariableContext { FlowVars = { ["phone"] = "555-1234" } };

        Assert.Equal("555-1234", _resolver.Resolve("{{flow.phone}}", ctx));
    }

    [Fact]
    public void DivideByZero_ReturnsLeftValueUnchanged_DoesNotThrow()
    {
        var ctx = new VariableContext { FlowVars = { ["x"] = "10" } };

        Assert.Equal("10", _resolver.Resolve("{{flow.x / 0}}", ctx));
    }
}
