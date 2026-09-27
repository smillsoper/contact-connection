using ContactConnection.Domain.Entities;
using Xunit;

namespace ContactConnection.Domain.Tests.Domain;

public class NumberProviderTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public void Create_ValidatesNameAndType()
    {
        var p = NumberProvider.Create(Tenant, " RingSquared ", NumberProviderType.RoutingPlatform);
        Assert.Equal("RingSquared", p.Name);
        Assert.True(p.IsActive);
        Assert.Throws<ArgumentException>(() => NumberProvider.Create(Tenant, "", NumberProviderType.Carrier));
        Assert.Throws<ArgumentException>(() => NumberProvider.Create(Tenant, "X", "isp"));
    }

    [Fact]
    public void ApiKey_SetAndRevoke()
    {
        var p = NumberProvider.Create(Tenant, "RingSquared", NumberProviderType.RoutingPlatform);
        p.SetApiKey("hash", "ccrk_abc1234");
        Assert.Equal("hash", p.ApiKeyHash);
        Assert.NotNull(p.ApiKeyIssuedAt);
        p.RevokeApiKey();
        Assert.Null(p.ApiKeyHash);
        Assert.Null(p.ApiKeyPrefix);
    }

    [Fact]
    public void PhoneNumber_RoutingDelivery_RequiresClientNumber()
    {
        var pn = PhoneNumber.Create(Tenant, Guid.NewGuid(), "+18005550100");
        Assert.Equal(PhoneNumberRole.Hosted, pn.Role);

        Assert.Throws<ArgumentException>(() => pn.SetProvider(Guid.NewGuid(), PhoneNumberRole.RoutingDelivery, " "));

        var provider = Guid.NewGuid();
        pn.SetProvider(provider, PhoneNumberRole.RoutingDelivery, " 8009399174 ");
        Assert.Equal(provider, pn.ProviderId);
        Assert.Equal("8009399174", pn.ClientNumber);
    }

    [Fact]
    public void PhoneNumber_Hosted_DropsClientNumber_AndRejectsUnknownRole()
    {
        var pn = PhoneNumber.Create(Tenant, Guid.NewGuid(), "+18005550100");
        pn.SetProvider(Guid.NewGuid(), PhoneNumberRole.Hosted, "8009399174");
        Assert.Null(pn.ClientNumber);
        Assert.Throws<ArgumentException>(() => pn.SetProvider(null, "ported", null));
    }

    [Fact]
    public void CallRecord_SetNumberProvider()
    {
        var record = CallRecord.Create(Tenant, Guid.NewGuid(), Guid.NewGuid());
        var provider = Guid.NewGuid();
        record.SetNumberProvider(provider, "8009399174");
        Assert.Equal(provider, record.NumberProviderId);
        Assert.Equal("8009399174", record.ClientNumber);
    }
}
