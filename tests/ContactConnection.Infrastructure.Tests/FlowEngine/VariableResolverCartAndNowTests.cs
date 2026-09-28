using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.FlowEngine;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.FlowEngine;

/// <summary>{{cart.*}} (FlowEngine refreshes VariableContext.Cart from the call's cart before any
/// node that references it) and {{now.*}} (UTC) on the CRM resolver — added S164 for the NeuroQ V1
/// closing read-back and the SMS-consent timestamp the order API carries.</summary>
public class VariableResolverCartAndNowTests
{
    private readonly VariableResolver _resolver = new();

    [Fact]
    public void ResolvesCartValues()
    {
        var ctx = new VariableContext
        {
            Cart = { ["total"] = "149.73", ["items_summary"] = "1 x NeuroQ Memory & Focus Buy 2 Get 1 Free Every 3 Months" },
        };

        Assert.Equal("Your total today is $149.73 for 1 x NeuroQ Memory & Focus Buy 2 Get 1 Free Every 3 Months",
            _resolver.Resolve("Your total today is ${{cart.total}} for {{cart.items_summary}}", ctx));
    }

    [Fact]
    public void MissingCart_ResolvesEmpty_AndShowsNotCapturedForDisplay()
    {
        var ctx = new VariableContext();
        Assert.Equal("", _resolver.Resolve("{{cart.total}}", ctx));
        Assert.Equal("[not captured]", _resolver.ResolveForDisplay("{{cart.total}}", ctx));
    }

    [Fact]
    public void NowIso_IsCurrentUtcInstant()
    {
        var before = DateTimeOffset.UtcNow;
        var iso = _resolver.Resolve("{{now.iso}}", new VariableContext());
        var parsed = DateTimeOffset.Parse(iso);

        Assert.InRange(parsed, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal(TimeSpan.Zero, parsed.Offset);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", _resolver.Resolve("{{now.date}}", new VariableContext()));
        Assert.Equal("", _resolver.Resolve("{{now.unknown}}", new VariableContext()));
    }
}
