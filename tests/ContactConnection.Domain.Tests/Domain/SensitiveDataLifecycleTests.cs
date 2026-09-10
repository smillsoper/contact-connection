using ContactConnection.Domain.Entities;
using Xunit;

namespace ContactConnection.Domain.Tests.Domain;

/// <summary>CallRecord.StoreSensitiveData / WipeSensitiveData — the PCI time-bounded blob
/// (ARCHITECTURE.md §24), first used by the tf_secure_collect node.</summary>
public class SensitiveDataLifecycleTests
{
    private static CallRecord NewRecord() =>
        CallRecord.CreateInbound(Guid.NewGuid(), "+15551110000");

    [Fact]
    public void StoreSensitiveData_SetsCiphertext_StampsStoredAt_ClearsWipe()
    {
        var r = NewRecord();
        r.WipeSensitiveData("earlier discard");

        r.StoreSensitiveData("cipher-token-1");

        Assert.Equal("cipher-token-1", r.SensitiveData);
        Assert.NotNull(r.SensitiveDataStoredAt);
        Assert.Null(r.SensitiveDataWipedAt);
        Assert.Null(r.SensitiveWipeReason);
    }

    [Fact]
    public void StoreSensitiveData_Twice_OverwritesAndRefreshesTimestamp()
    {
        var r = NewRecord();
        r.StoreSensitiveData("v1");
        var firstAt = r.SensitiveDataStoredAt;

        r.StoreSensitiveData("v2");

        Assert.Equal("v2", r.SensitiveData);
        Assert.True(r.SensitiveDataStoredAt >= firstAt);
    }

    [Fact]
    public void WipeSensitiveData_DropsCiphertext_RecordsReason()
    {
        var r = NewRecord();
        r.StoreSensitiveData("cipher");

        r.WipeSensitiveData("retention purge");

        Assert.Null(r.SensitiveData);
        Assert.NotNull(r.SensitiveDataWipedAt);
        Assert.Equal("retention purge", r.SensitiveWipeReason);
    }
}
