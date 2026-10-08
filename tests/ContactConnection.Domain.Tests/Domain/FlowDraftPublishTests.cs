using ContactConnection.Domain.Entities;
using Xunit;

namespace ContactConnection.Domain.Tests.Domain;

/// <summary>Flow draft / published (S183): saving changes the draft only; publishing makes it live.</summary>
public class FlowDraftPublishTests
{
    private static Flow NewFlow() => Flow.Create(Guid.NewGuid(), Guid.NewGuid(), "Script", FlowType.Crm, """{"v":1}""");

    [Fact]
    public void NewFlow_IsADraft_WithNothingLive()
    {
        var f = NewFlow();
        Assert.False(f.IsActive);
        Assert.Null(f.PublishedDefinition);
        Assert.True(f.HasUnpublishedChanges);
        Assert.Equal("{}", f.DefinitionFor(draft: false));
        Assert.Equal("""{"v":1}""", f.DefinitionFor(draft: true));
    }

    [Fact]
    public void Saving_AfterPublish_LeavesTheLiveScriptAlone()
    {
        var f = NewFlow();
        f.Publish();
        Assert.False(f.HasUnpublishedChanges);
        Assert.Equal(1, f.PublishedVersion);

        f.UpdateDefinition("""{"v":2}""");

        Assert.True(f.HasUnpublishedChanges);
        Assert.Equal("""{"v":1}""", f.DefinitionFor(draft: false));
        Assert.Equal(1, f.VersionFor(draft: false));
        Assert.Equal(2, f.VersionFor(draft: true));

        f.Publish();
        Assert.Equal("""{"v":2}""", f.PublishedDefinition);
        Assert.False(f.HasUnpublishedChanges);
    }
}
