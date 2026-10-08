using ContactConnection.Domain.Entities;
using Xunit;

namespace ContactConnection.Domain.Tests.Domain;

/// <summary>S184: a tenant's subdomain is a host name (keeps hyphens); only its schema name uses underscores.</summary>
public class TenantSubdomainTests
{
    [Fact]
    public void Subdomain_keeps_hyphens_schema_uses_underscores()
    {
        var t = Tenant.Create("Acme", " Acme-Health ", "America/Chicago");
        Assert.Equal("acme-health", t.Subdomain);
        Assert.Equal("tenant_acme_health", t.SchemaName);
    }

    [Fact]
    public void Hyphen_and_underscore_spellings_share_a_schema_name() =>
        Assert.Equal(Tenant.SchemaNameFor("a-b"), Tenant.SchemaNameFor("a_b"));
}
