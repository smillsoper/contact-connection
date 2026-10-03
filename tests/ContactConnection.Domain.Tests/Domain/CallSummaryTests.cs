using ContactConnection.Domain.Entities;
using Xunit;

namespace ContactConnection.Domain.Tests.Domain;

/// <summary>AI summary review: provenance, edit detection, one confirmation per suggestion.</summary>
public class CallSummaryTests
{
    private static CallSummary Suggested() => CallSummary.Suggest(Guid.NewGuid(), Guid.NewGuid(),
        "Caller ordered NeuroQ.", "Memory support", "order_failed", "Order", true, 0.8, null, false, true,
        "claude-haiku-4-5-20251001", 1800, 200, 0.0028m, 2500, "Stephen Soper");

    [Fact]
    public void ConfirmAsSuggested_IsNotEdited_AndRecordsWhoConfirmed()
    {
        var s = Suggested();
        var reviewer = Guid.NewGuid();
        s.Confirm("Caller ordered NeuroQ.", "Memory support", "order_failed", "Order", null, reviewer, "Stephen Soper");

        Assert.Equal(CallSummaryStatus.Confirmed, s.Status);
        Assert.False(s.Edited);
        Assert.Equal(reviewer, s.ReviewedById);
        Assert.Equal("Caller ordered NeuroQ.", s.AiSummary);   // the AI's original is kept
    }

    [Theory]
    [InlineData("Caller ordered NeuroQ — test call.", "Order")]
    [InlineData("Caller ordered NeuroQ.", "Test Call")]
    public void ChangingTheTextOrDisposition_MarksItEdited(string text, string disposition)
    {
        var s = Suggested();
        s.Confirm(text, "Memory support", "order_failed", disposition, null, Guid.NewGuid(), "x");
        Assert.True(s.Edited);
    }

    [Fact]
    public void ASuggestionIsReviewedOnce_AndCantBeConfirmedEmpty()
    {
        var empty = Suggested();
        Assert.Throws<ArgumentException>(() => empty.Confirm(" ", "", "other", null, null, Guid.NewGuid(), "x"));

        var s = Suggested();
        s.Discard(Guid.NewGuid(), "x");
        Assert.Equal(CallSummaryStatus.Discarded, s.Status);
        Assert.Throws<InvalidOperationException>(() => s.Confirm("text", "", "other", null, null, Guid.NewGuid(), "x"));
    }

    [Fact]
    public void Supersede_OnlyAffectsConfirmed()
    {
        var s = Suggested();
        s.Supersede();
        Assert.Equal(CallSummaryStatus.Suggested, s.Status);
        s.Confirm("text", "", "other", null, null, Guid.NewGuid(), "x");
        s.Supersede();
        Assert.Equal(CallSummaryStatus.Superseded, s.Status);
    }
}
