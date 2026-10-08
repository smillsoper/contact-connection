using ContactConnection.Domain.Entities;
using Xunit;

namespace ContactConnection.Domain.Tests.Domain;

/// <summary>Remote fixes (S184): which fixes change things, and a result is recorded once.</summary>
public class RemoteActionTests
{
    [Theory]
    [InlineData("diagnostics", false)]
    [InlineData("extension", false)]
    [InlineData("reregister", true)]
    [InlineData("clear-call", true)]
    [InlineData("refresh", true)]
    public void OnlyLookingFixes_AreNotDisruptive(string action, bool disruptive) =>
        Assert.Equal(disruptive, RemoteActionType.Disruptive(action));

    [Fact]
    public void Unknown_IsRefused() =>
        Assert.Throws<ArgumentException>(() => RemoteAction.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Sup", "format-drive"));

    [Fact]
    public void Result_IsRecordedOnce()
    {
        var a = RemoteAction.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Sup", RemoteActionType.Reregister);
        a.Complete(true, "The softphone re-registered.");
        a.Complete(false, "late duplicate");
        Assert.True(a.Ok);
        Assert.Equal("The softphone re-registered.", a.Detail);
        Assert.NotNull(a.CompletedAt);
    }
}
