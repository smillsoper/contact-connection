using System.Text.Json.Nodes;
using ContactConnection.Infrastructure.Ai;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Ai;

/// <summary>AI step 2: the output schema, and never trusting the model's answer blindly.</summary>
public class CallSummarizerTests
{
    private static readonly string[] Allowed = ["Order", "Test Call", "Wrong Number"];

    [Fact]
    public void Tool_RestrictsDispositionAndOutcomeToAllowedValues()
    {
        var props = CallSummarizer.Tool(Allowed)["input_schema"]!["properties"]!;
        Assert.Equal(Allowed, props["suggested_disposition"]!["enum"]!.AsArray().Select(v => v!.GetValue<string>()));
        Assert.Equal(CallSummarizer.Outcomes, props["outcome"]!["enum"]!.AsArray().Select(v => v!.GetValue<string>()));
    }

    private static JsonObject Answer(string disposition = "order", string outcome = "order_failed", double confidence = 0.8) => new()
    {
        ["summary"] = " Caller ordered NeuroQ; the order post failed. ",
        ["reason_for_call"] = "Memory support",
        ["outcome"] = outcome,
        ["suggested_disposition"] = disposition,
        ["confidence"] = confidence,
        ["follow_up"] = null,
        ["is_test_call"] = true,
    };

    [Fact]
    public void Validate_AcceptsAGoodAnswer_NormalizingDispositionCase()
    {
        var s = CallSummarizer.Validate(Answer(), Allowed);
        Assert.Equal("Caller ordered NeuroQ; the order post failed.", s.Text);
        Assert.Equal("Order", s.SuggestedDisposition);
        Assert.True(s.DispositionValid);
        Assert.Equal("order_failed", s.Outcome);
        Assert.Null(s.FollowUp);
        Assert.True(s.IsTestCall);
    }

    [Fact]
    public void Validate_FlagsAnInventedDisposition_UnknownOutcome_AndClampsConfidence()
    {
        var s = CallSummarizer.Validate(Answer(disposition: "Happy Customer", outcome: "great", confidence: 1.7), Allowed);
        Assert.False(s.DispositionValid);
        Assert.Equal("other", s.Outcome);
        Assert.Equal(1, s.Confidence);
    }

    [Fact]
    public void Validate_EmptySummary_IsAFailureNotABlankSuggestion()
    {
        var bad = Answer();
        bad["summary"] = "  ";
        Assert.Throws<AnthropicClient.AiUnavailableException>(() => CallSummarizer.Validate(bad, Allowed));
    }
}
