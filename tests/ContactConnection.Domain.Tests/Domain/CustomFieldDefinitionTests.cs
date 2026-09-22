using ContactConnection.Domain.Entities;
using Xunit;

namespace ContactConnection.Domain.Tests.Domain;

public class CustomFieldDefinitionTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Create_TenantWide_Succeeds()
    {
        var def = CustomFieldDefinition.Create(TenantId, "original_ani", "Original ANI", "string");

        Assert.Null(def.ClientId);
        Assert.Null(def.CampaignId);
        Assert.Equal(2, def.ScopeRank);
        Assert.True(def.IsActive);
    }

    [Fact]
    public void Create_ClientScoped_Succeeds()
    {
        var clientId = Guid.NewGuid();
        var def = CustomFieldDefinition.Create(TenantId, "priority", "Priority", "integer", clientId: clientId);

        Assert.Equal(clientId, def.ClientId);
        Assert.Null(def.CampaignId);
        Assert.Equal(1, def.ScopeRank);
    }

    [Fact]
    public void Create_CampaignScoped_WithClient_Succeeds()
    {
        var clientId = Guid.NewGuid();
        var campaignId = Guid.NewGuid();
        var def = CustomFieldDefinition.Create(TenantId, "upsell_offered", "Upsell Offered", "boolean",
            clientId: clientId, campaignId: campaignId);

        Assert.Equal(clientId, def.ClientId);
        Assert.Equal(campaignId, def.CampaignId);
        Assert.Equal(0, def.ScopeRank);
    }

    [Fact]
    public void Create_CampaignScoped_WithoutClient_Throws()
    {
        // A campaign implies a client — this scope combination is meaningless and was previously
        // allowed by the domain (only guarded, inconsistently, in the admin UI).
        var ex = Assert.Throws<ArgumentException>(() =>
            CustomFieldDefinition.Create(TenantId, "upsell_offered", "Upsell Offered", "boolean",
                clientId: null, campaignId: Guid.NewGuid()));

        Assert.Equal("clientId", ex.ParamName);
    }

    [Fact]
    public void Create_UnknownDataType_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            CustomFieldDefinition.Create(TenantId, "foo", "Foo", "not_a_real_type"));

        Assert.Equal("dataTypeName", ex.ParamName);
    }

    [Fact]
    public void Create_NormalizesFieldName_TrimmedAndLowercased()
    {
        var def = CustomFieldDefinition.Create(TenantId, "  Original_ANI  ", "Original ANI", "string");
        Assert.Equal("original_ani", def.FieldName);
    }
}
