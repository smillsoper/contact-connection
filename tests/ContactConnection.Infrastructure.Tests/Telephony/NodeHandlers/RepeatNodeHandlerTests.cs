using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Telephony.NodeHandlers;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony.NodeHandlers;

/// <summary>
/// tf_repeat — a bounded loop primitive: one entry, "repeat"/"finished" exits. Every arrival at
/// this node's entry (first pass from upstream, or the tenant's own loop-back wire) increments a
/// per-node counter; "repeat" fires for every hit up to and including repeatCount, and the FIRST
/// hit past repeatCount exits "finished" instead — so with repeatCount = 3 the "repeat" branch
/// fires exactly 3 times before the loop ends on hit 4. Pure ctx.Vars bookkeeping, no telephony
/// I/O — see the handler's own doc comment for the full mechanics writeup (locked with the user
/// in S138; count semantics corrected same session after a live test showed the original
/// off-by-one reading backwards from what a tenant would expect).
/// </summary>
public class RepeatNodeHandlerTests
{
    private static TelephonyFlowContext Ctx(params (string k, string v)[] vars)
    {
        var ctx = new TelephonyFlowContext
        {
            ChannelUuid = "call-uuid-repeat", CallerNumber = "+15551110000", DestinationNumber = "+15552220000",
            TenantId = Guid.NewGuid(), CampaignId = Guid.NewGuid(), CallRecordId = Guid.NewGuid(),
            TenantSubdomain = "test-tenant", TenantSchemaName = "tenant_test_tenant", TenantTimezone = "America/Chicago",
        };
        foreach (var (k, v) in vars) ctx.Vars[k] = v;
        return ctx;
    }

    private static JsonObject Node(int repeatCount) => new()
    {
        ["type"] = "tf_repeat",
        ["nodeId"] = "tf_repeat_1",
        ["repeatCount"] = repeatCount,
        ["transitions"] = new JsonObject { ["repeat"] = "n_body", ["finished"] = "n_after" },
    };

    private static readonly RepeatNodeHandler Handler = new();

    [Fact]
    public async Task FirstHit_AtOrBelowCount_TakesRepeat_IncrementsCounter()
    {
        var ctx = Ctx();

        var result = await Handler.ExecuteAsync(Node(repeatCount: 3), ctx);

        Assert.Equal("repeat", result.TransitionTaken);
        Assert.Equal("n_body", result.NextNodeId);
        Assert.Equal("1", ctx.Vars["_repeat_tf_repeat_1_count"]);
    }

    [Fact]
    public async Task ThirdHit_StillAtCount_TakesRepeat_NotFinished()
    {
        // Two prior passes already ran (count=2 persisted). repeatCount=3 means "repeat" should
        // still fire on this, the 3rd hit — finishing only fires past the configured count.
        var ctx = Ctx(("_repeat_tf_repeat_1_count", "2"));

        var result = await Handler.ExecuteAsync(Node(repeatCount: 3), ctx);

        Assert.Equal("repeat", result.TransitionTaken);
        Assert.Equal("n_body", result.NextNodeId);
        Assert.Equal("3", ctx.Vars["_repeat_tf_repeat_1_count"]);
    }

    [Fact]
    public async Task FourthHit_PastCount_TakesFinished_ResetsCounter()
    {
        var ctx = Ctx(("_repeat_tf_repeat_1_count", "3"));

        var result = await Handler.ExecuteAsync(Node(repeatCount: 3), ctx);

        Assert.Equal("finished", result.TransitionTaken);
        Assert.Equal("n_after", result.NextNodeId);
        Assert.False(ctx.Vars.ContainsKey("_repeat_tf_repeat_1_count"));
    }

    [Fact]
    public async Task FullCycle_RepeatCount3_FiresRepeatExactlyThreeTimesThenFinished()
    {
        var ctx = Ctx();

        var first  = await Handler.ExecuteAsync(Node(repeatCount: 3), ctx);
        var second = await Handler.ExecuteAsync(Node(repeatCount: 3), ctx);
        var third  = await Handler.ExecuteAsync(Node(repeatCount: 3), ctx);
        var fourth = await Handler.ExecuteAsync(Node(repeatCount: 3), ctx);

        Assert.Equal("repeat", first.TransitionTaken);
        Assert.Equal("repeat", second.TransitionTaken);
        Assert.Equal("repeat", third.TransitionTaken);
        Assert.Equal("finished", fourth.TransitionTaken);
        Assert.False(ctx.Vars.ContainsKey("_repeat_tf_repeat_1_count"));
    }

    [Fact]
    public async Task RepeatCountOne_FirstHitRepeats_SecondHitFinishes()
    {
        var ctx = Ctx();

        var first  = await Handler.ExecuteAsync(Node(repeatCount: 1), ctx);
        var second = await Handler.ExecuteAsync(Node(repeatCount: 1), ctx);

        Assert.Equal("repeat", first.TransitionTaken);
        Assert.Equal("finished", second.TransitionTaken);
        Assert.Equal("n_after", second.NextNodeId);
    }

    [Fact]
    public async Task RepeatCountZeroOrNegative_ClampedToOne_BehavesLikeRepeatCountOne()
    {
        var ctx = Ctx();

        var first  = await Handler.ExecuteAsync(Node(repeatCount: 0), ctx);
        var second = await Handler.ExecuteAsync(Node(repeatCount: 0), ctx);

        Assert.Equal("repeat", first.TransitionTaken);
        Assert.Equal("finished", second.TransitionTaken);
    }

    [Fact]
    public async Task DistinctNodeIds_CountersDoNotCollide()
    {
        var ctx = Ctx();
        var nodeA = Node(repeatCount: 5);
        nodeA["nodeId"] = "tf_repeat_A";
        var nodeB = Node(repeatCount: 5);
        nodeB["nodeId"] = "tf_repeat_B";

        await Handler.ExecuteAsync(nodeA, ctx);
        await Handler.ExecuteAsync(nodeA, ctx);
        await Handler.ExecuteAsync(nodeB, ctx);

        Assert.Equal("2", ctx.Vars["_repeat_tf_repeat_A_count"]);
        Assert.Equal("1", ctx.Vars["_repeat_tf_repeat_B_count"]);
    }
}
