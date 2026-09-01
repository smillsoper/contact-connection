using ContactConnection.Infrastructure.Telephony;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony;

public class IvrMenuTests
{
    [Fact]
    public void BuildRegexp_FromOptions_IsAnchoredAlternation()
        => Assert.Equal("^(1|2|9)$", IvrMenu.BuildRegexp(new[] { "1", "2", "9" }));

    [Fact]
    public void BuildRegexp_DedupesAndSkipsBlanks()
        => Assert.Equal("^(1|2)$", IvrMenu.BuildRegexp(new[] { "1", "", "2", "1", null }));

    [Fact]
    public void BuildRegexp_NoOptions_MatchesAnyDigits()
        => Assert.Equal(@"\d+", IvrMenu.BuildRegexp(Array.Empty<string>()));

    [Fact]
    public void BuildRegexp_EscapesStarAndHash()
        => Assert.Equal(@"^(\*|\#)$", IvrMenu.BuildRegexp(new[] { "*", "#" }));

    [Fact]
    public void ResolveTarget_Match_ReturnsOptionTarget()
    {
        var opts = new Dictionary<string, string> { ["1"] = "node_sales", ["2"] = "node_support" };
        Assert.Equal("node_sales", IvrMenu.ResolveTarget("1", opts, "node_fallback"));
    }

    [Fact]
    public void ResolveTarget_NoMatch_FallsThroughToNoMatch()
    {
        var opts = new Dictionary<string, string> { ["1"] = "node_sales" };
        Assert.Equal("node_fallback", IvrMenu.ResolveTarget("7", opts, "node_fallback"));
    }

    [Fact]
    public void ResolveTarget_EmptyDigits_FallsThroughToNoMatch()
    {
        var opts = new Dictionary<string, string> { ["1"] = "node_sales" };
        Assert.Equal("node_fallback", IvrMenu.ResolveTarget("", opts, "node_fallback"));
        Assert.Equal("node_fallback", IvrMenu.ResolveTarget(null, opts, "node_fallback"));
    }

    [Fact]
    public void ResolveTarget_NoMatchAndNoFallback_ReturnsNull()
        => Assert.Null(IvrMenu.ResolveTarget("7", new Dictionary<string, string> { ["1"] = "x" }, null));
}
