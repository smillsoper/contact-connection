using ContactConnection.Infrastructure.Common;
using ContactConnection.Infrastructure.Tests.Credentials;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Common;

/// <summary>
/// {{shared.*}} backing store — the call-wide variable bridge between the CRM script flow and the
/// telephony call flow for the same call (see project memory: neither flow.* namespace has ever
/// been visible to the other side; this is the new dedicated cross-engine channel). Against a real
/// local Redis, same convention as the other Redis-backed stores in this test project.
/// </summary>
[Collection("Redis")]
public class RedisSharedCallVariableStoreTests(RedisFixture fixture)
{
    private RedisSharedCallVariableStore NewStore() => new(fixture.Connection);

    [Fact]
    public async Task GetAllAsync_NoVarsSet_ReturnsEmptyDictionary()
    {
        var result = await NewStore().GetAllAsync(Guid.NewGuid());

        Assert.Empty(result);
    }

    [Fact]
    public async Task SetAsync_ThenGetAllAsync_ReturnsTheValue()
    {
        var callId = Guid.NewGuid();
        var store  = NewStore();

        await store.SetAsync(callId, "CC_Capture_Success", "true");
        var result = await store.GetAllAsync(callId);

        Assert.Equal("true", result["CC_Capture_Success"]);
    }

    [Fact]
    public async Task SetAsync_MultipleKeys_AllVisibleTogether()
    {
        var callId = Guid.NewGuid();
        var store  = NewStore();

        await store.SetAsync(callId, "a", "1");
        await store.SetAsync(callId, "b", "2");
        var result = await store.GetAllAsync(callId);

        Assert.Equal("1", result["a"]);
        Assert.Equal("2", result["b"]);
    }

    [Fact]
    public async Task SetAsync_SameKeyTwice_OverwritesPreviousValue()
    {
        var callId = Guid.NewGuid();
        var store  = NewStore();

        await store.SetAsync(callId, "k", "first");
        await store.SetAsync(callId, "k", "second");
        var result = await store.GetAllAsync(callId);

        Assert.Equal("second", result["k"]);
    }

    [Fact]
    public async Task DifferentCallRecordIds_AreIsolatedFromEachOther()
    {
        var callA = Guid.NewGuid();
        var callB = Guid.NewGuid();
        var store = NewStore();

        await store.SetAsync(callA, "k", "for-a");
        var resultB = await store.GetAllAsync(callB);

        Assert.Empty(resultB);
    }
}
