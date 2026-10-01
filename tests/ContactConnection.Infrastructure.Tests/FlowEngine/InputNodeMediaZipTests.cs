using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects;
using ContactConnection.Infrastructure.FlowEngine;
using ContactConnection.Infrastructure.FlowEngine.NodeHandlers;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.FlowEngine;

/// <summary>S171 Media Phase B: an input node with a ZIP mask + "perform local media station
/// attribution" re-attributes the call by the captured zip and refreshes {{call_record.media.*}}.</summary>
public class InputNodeMediaZipTests
{
    private static FlowExecutionContext Ctx() => new()
    {
        SessionId = Guid.NewGuid(), FlowId = Guid.NewGuid(), FlowVersion = 1, CallRecordId = Guid.NewGuid(),
        InteractionId = Guid.NewGuid(), AgentId = Guid.NewGuid(), TenantId = Guid.NewGuid(), CurrentNodeId = "n_zip",
    };

    private static JsonObject ZipNode(string mask = "00000", bool attribute = true) => new()
    {
        ["type"] = "input", ["fieldType"] = "text", ["inputMask"] = mask, ["mediaZipAttribution"] = attribute,
        ["transitions"] = new JsonObject { ["default"] = "n_next" },
    };

    private static readonly MediaAttribution Placed = new(
        Guid.NewGuid(), MediaMarketType.Local, "Cannella", "KNAZ-TV", "TV", "LF", new DateOnly(2026, 10, 1), "+18005551234",
        [], CallerLocation.FromZip, "86001", 2.1);

    [Fact]
    public async Task ZipMask_WithAttribution_ReattributesAndRefreshesMediaVars()
    {
        var media = new Mock<IMediaReattributionService>();
        var ctx = Ctx();
        media.Setup(m => m.ApplyZipAsync(ctx.CallRecordId, "86001", It.IsAny<CancellationToken>())).ReturnsAsync(Placed);

        var result = await new InputNodeHandler(new VariableResolver(), media.Object).ExecuteAsync(ZipNode(), ctx, "86001", "default");

        Assert.Equal("n_next", result.NextNodeId);
        Assert.Equal("KNAZ-TV", new VariableResolver().Resolve("{{call_record.media.station}}", ctx.ToVariableContext()));
        Assert.Equal("86001", new VariableResolver().Resolve("{{call_record.media.location_key}}", ctx.ToVariableContext()));
    }

    [Theory]
    [InlineData("00000", false)]            // box not ticked
    [InlineData("(000) 000-0000", true)]    // not a ZIP mask — the flag is ignored
    public async Task NoAttribution_UnlessZipMaskAndTicked(string mask, bool attribute)
    {
        var media = new Mock<IMediaReattributionService>();
        await new InputNodeHandler(new VariableResolver(), media.Object).ExecuteAsync(ZipNode(mask, attribute), Ctx(), "86001", "default");
        media.Verify(m => m.ApplyZipAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LookupFailure_StillAdvances()
    {
        var media = new Mock<IMediaReattributionService>();
        media.Setup(m => m.ApplyZipAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
             .ThrowsAsync(new InvalidOperationException("db down"));

        var result = await new InputNodeHandler(new VariableResolver(), media.Object).ExecuteAsync(ZipNode(), Ctx(), "86001", "default");
        Assert.Equal("n_next", result.NextNodeId);
    }

    [Fact]
    public async Task Display_DoesNotAttribute()
    {
        var media = new Mock<IMediaReattributionService>();
        await new InputNodeHandler(new VariableResolver(), media.Object).ExecuteAsync(ZipNode(), Ctx(), agentInput: null, "default");
        media.Verify(m => m.ApplyZipAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
