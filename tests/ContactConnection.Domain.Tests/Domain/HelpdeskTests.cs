using ContactConnection.Domain.Entities;
using Xunit;

namespace ContactConnection.Domain.Tests.Domain;

/// <summary>Agent help desks (S184): names, campaigns, topic limits and where files are stored.</summary>
public class HelpdeskTests
{
    [Fact]
    public void Create_trims_and_dedupes_campaigns()
    {
        var c = Guid.NewGuid();
        var h = Helpdesk.Create(Guid.NewGuid(), "  Product help ", "  ", [c, c]);
        Assert.Equal("Product help", h.Name);
        Assert.Null(h.Description);
        Assert.Equal([c], h.CampaignIds);
        Assert.True(h.IsActive);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_name_is_required(string name) =>
        Assert.Throws<ArgumentException>(() => Helpdesk.Create(Guid.NewGuid(), name, null, []));

    [Fact]
    public void Topic_needs_a_title_and_caps_attachments()
    {
        var t = HelpdeskTopic.Create(Guid.NewGuid(), 0);
        Assert.Throws<ArgumentException>(() => t.Update(" ", "<p>x</p>", "x", [], "Admin"));
        Assert.Throws<ArgumentException>(() => t.Update("Too many", "", "", Enumerable.Range(0, 21).Select(_ => Guid.NewGuid()), "Admin"));
        t.Update(" Returns ", "<p>x</p>", "x", [], "Admin");
        Assert.Equal("Returns", t.Title);
        Assert.Equal("Admin", t.UpdatedByName);
    }

    [Fact]
    public void Files_are_stored_under_their_help_desk()
    {
        var tenant = Guid.NewGuid(); var desk = Guid.NewGuid();
        var f = HelpdeskFile.Create(tenant, desk, true, "x.png", "image/png", 10, Guid.NewGuid());
        Assert.Equal($"helpdesk/{tenant}/{desk}/{f.Id}", f.StorageKey);
    }
}
