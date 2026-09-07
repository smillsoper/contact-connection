using ContactConnection.Infrastructure.Telephony;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony;

/// <summary>
/// Covers AgentRegistrationStore — the in-memory SIP-registration presence map behind the
/// supervisor dashboard's "Phone" column (S118). Seeded + kept live from FreeSWITCH by
/// EslBackgroundService; this suite pins the store's own semantics.
/// </summary>
public class AgentRegistrationStoreTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    [Fact]
    public void Set_then_Get_returns_registration()
    {
        var store = new AgentRegistrationStore();
        var since = DateTimeOffset.UtcNow;

        store.Set(TenantA, "1001", since);

        var reg = store.Get(TenantA, "1001");
        Assert.NotNull(reg);
        Assert.Equal(since, reg!.Since);
    }

    [Fact]
    public void Get_is_null_when_not_registered()
    {
        var store = new AgentRegistrationStore();
        Assert.Null(store.Get(TenantA, "1001"));
    }

    [Fact]
    public void Set_keeps_earliest_since_on_re_register()
    {
        var store = new AgentRegistrationStore();
        var first = DateTimeOffset.UtcNow;
        var later = first.AddMinutes(5);

        store.Set(TenantA, "1001", first);
        store.Set(TenantA, "1001", later);   // a re-REGISTER ~one expiry interval later

        Assert.Equal(first, store.Get(TenantA, "1001")!.Since);
    }

    [Fact]
    public void Remove_clears_the_registration()
    {
        var store = new AgentRegistrationStore();
        store.Set(TenantA, "1001", DateTimeOffset.UtcNow);

        store.Remove(TenantA, "1001");

        Assert.Null(store.Get(TenantA, "1001"));
    }

    [Fact]
    public void Registrations_are_scoped_per_tenant()
    {
        var store = new AgentRegistrationStore();
        store.Set(TenantA, "1001", DateTimeOffset.UtcNow);

        Assert.NotNull(store.Get(TenantA, "1001"));
        Assert.Null(store.Get(TenantB, "1001"));
    }

    [Fact]
    public void ReplaceAll_adds_new_and_drops_absent()
    {
        var store = new AgentRegistrationStore();
        var t0 = DateTimeOffset.UtcNow;
        store.Set(TenantA, "1001", t0);
        store.Set(TenantA, "1002", t0);

        store.ReplaceAll(new[]
        {
            (TenantA, "1002", t0.AddMinutes(1)),   // stays (kept-earliest → t0)
            (TenantA, "1003", t0.AddMinutes(1)),   // added
        });

        Assert.Null(store.Get(TenantA, "1001"));                 // absent from snapshot → dropped
        Assert.Equal(t0, store.Get(TenantA, "1002")!.Since);     // retained, earliest since kept
        Assert.NotNull(store.Get(TenantA, "1003"));              // newly seeded
    }

    [Fact]
    public void Blank_extension_is_ignored()
    {
        var store = new AgentRegistrationStore();
        store.Set(TenantA, "  ", DateTimeOffset.UtcNow);
        Assert.Null(store.Get(TenantA, "  "));
    }
}
