using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.ApiExecution;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.ApiExecution;

public class ResponseSuccessEvaluatorTests
{
    private const string LifeSeasonsRule = """
        {"rules":[{"path":"success","operator":"equals","value":"true"}],"errorMessagePath":"message"}
        """;

    private static ApiDefinitionExecutionResult Ok(string body, int status = 200)
        => new(true, status, "OK", new(), body, false, null);

    [Fact]
    public void Http200WithSuccessFalse_BecomesFailure_WithVendorMessage()
    {
        var r = ResponseSuccessEvaluator.Apply(Ok("""{"success":false,"message":"Duplicate order number"}"""), LifeSeasonsRule);
        Assert.False(r.Success);
        Assert.Equal("Duplicate order number", r.Error);
        Assert.Equal(200, r.StatusCode);
        Assert.NotNull(r.ResponseBody); // kept for the flow to inspect
    }

    [Fact]
    public void SuccessTrue_StaysSuccess()
        => Assert.True(ResponseSuccessEvaluator.Apply(Ok("""{"success":true,"receipt":{"messageId":"m1"}}"""), LifeSeasonsRule).Success);

    [Fact]
    public void NoMessage_FallsBackToRuleDescription()
    {
        var r = ResponseSuccessEvaluator.Apply(Ok("""{"success":false}"""), LifeSeasonsRule);
        Assert.Contains("success equals true", r.Error);
        Assert.Contains("was: false", r.Error);
    }

    [Fact]
    public void NoCriteria_OrHttpFailure_Untouched()
    {
        var ok = Ok("""{"success":false}""");
        Assert.Same(ok, ResponseSuccessEvaluator.Apply(ok, "{}"));
        Assert.Same(ok, ResponseSuccessEvaluator.Apply(ok, null));

        var http500 = new ApiDefinitionExecutionResult(false, 500, "err", new(), "{}", false, "boom");
        Assert.Same(http500, ResponseSuccessEvaluator.Apply(http500, LifeSeasonsRule));
    }

    [Fact]
    public void NonJsonBody_IsFailure()
        => Assert.False(ResponseSuccessEvaluator.Apply(Ok("<html>oops</html>"), LifeSeasonsRule).Success);

    [Theory]
    [InlineData("""{"rules":[{"path":"receipt.messageId","operator":"exists"}]}""", true)]
    [InlineData("""{"rules":[{"path":"errors.0","operator":"not_exists"}]}""", true)]
    [InlineData("""{"rules":[{"path":"errors","operator":"falsy"}]}""", true)]
    [InlineData("""{"rules":[{"path":"status","operator":"contains","value":"ACCEPT"}]}""", true)]
    [InlineData("""{"rules":[{"path":"status","operator":"not_equals","value":"accepted"}]}""", false)]
    [InlineData("""{"rules":[{"path":"receipt.missing","operator":"exists"}]}""", false)]
    public void Operators(string criteria, bool expected)
    {
        var body = """{"success":true,"status":"Accepted","errors":[],"receipt":{"messageId":"m1"}}""";
        Assert.Equal(expected, ResponseSuccessEvaluator.Apply(Ok(body), criteria).Success);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("{}", true)]
    [InlineData("""{"rules":[{"path":"success","value":"true"}]}""", true)]
    [InlineData("[1]", false)]
    [InlineData("""{"rules":"x"}""", false)]
    [InlineData("""{"rules":[{"operator":"exists"}]}""", false)]
    [InlineData("""{"rules":[{"path":"a","operator":"bogus"}]}""", false)]
    [InlineData("not json", false)]
    public void Validate(string? criteria, bool valid)
        => Assert.Equal(valid, ResponseSuccessEvaluator.Validate(criteria) is null);
}
