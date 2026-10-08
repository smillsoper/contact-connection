using ContactConnection.Domain.Entities;
using Xunit;

namespace ContactConnection.Domain.Tests.Domain;

/// <summary>In-call coaching notes (S183): Sent → Seen → Got it, or taken back.</summary>
public class CoachingNoteTests
{
    private static CoachingNote New() => CoachingNote.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Sup", "  Offer the 3-month package  ", null);

    [Fact]
    public void Lifecycle_SentSeenGotIt()
    {
        var n = New();
        Assert.Equal("Offer the 3-month package", n.Text);
        Assert.Equal("sent", n.Status);
        n.Seen();
        Assert.Equal("seen", n.Status);
        n.Acknowledge();
        Assert.Equal("acknowledged", n.Status);
        Assert.False(n.IsOpen);
        n.Retract();   // too late — it was already acknowledged
        Assert.Equal("acknowledged", n.Status);
    }

    [Fact]
    public void GotIt_WithoutSeen_StillRecordsSeen()
    {
        var n = New();
        n.Acknowledge();
        Assert.NotNull(n.SeenAt);
    }

    [Fact]
    public void TakenBack_LeavesThePortal()
    {
        var n = New();
        n.Retract();
        Assert.Equal("retracted", n.Status);
        Assert.False(n.IsOpen);
    }

    [Fact]
    public void Empty_Or_TooLong_IsRefused()
    {
        Assert.Throws<ArgumentException>(() => CoachingNote.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "S", "   ", null));
        Assert.Throws<ArgumentException>(() => CoachingNote.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "S", new string('x', 501), null));
    }
}
