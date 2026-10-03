using System.Text.Json;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Ai;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Ai;

/// <summary>AI step 1: what the model would see — and, above all, what it must never see.</summary>
public class CallSummaryContextTests
{
    [Theory]
    [InlineData("card 4111 1111 1111 1111 exp", "card [card number] exp")]
    [InlineData("4111-1111-1111-1111", "[card number]")]
    [InlineData("ssn 123-45-6789", "ssn [SSN]")]
    [InlineData("call me at (541) 670-4541 tomorrow", "call me at [phone] tomorrow")]
    [InlineData("+15416704541", "[phone]")]
    [InlineData("email jane.doe@example.com please", "email [email] please")]
    [InlineData("zip 97470, order LIFSEA-10000003", "zip 97470, order LIFSEA-10000003")]   // not over-eager
    public void Redactor_ScrubsSensitivePatterns(string input, string expected) =>
        Assert.Equal(expected, new AiRedactor().Scrub(input));

    private static string History(params object[] steps) => JsonSerializer.Serialize(steps);
    private static object Step(string type, string label, string? input = null) =>
        new { NodeType = type, Label = label, InputValue = input, TransitionTaken = "next" };

    private static CallRecord Call() => CallRecord.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    [Fact]
    public void Build_ShowsTheScriptAsWorked_AndWithholdsPersonalData()
    {
        var history = History(
            Step("section", "Opening"),
            Step("input", "First Name", "Margaret"),
            Step("input", "Zip Code", "97470"),
            Step("set_variable", "Billing first name = first name"),
            Step("input", "Probe 1", "Memory issues and forgetfulness"),
            Step("address", "Billing Address", "{\"address1\":\"1 Main St\"}"),
            Step("tf_secure_collect", "Card", "4111111111111111"),
            Step("input", "Callback number", "541-670-4541"),
            Step("api_call", "Submit Order"));

        var r = CallSummaryContextBuilder.Build(Call(), "Life Seasons", "NeuroQ - LF TV", [history]);

        Assert.Contains("Life Seasons / NeuroQ - LF TV", r.Text);
        Assert.Contains("== Opening ==", r.Text);
        Assert.Contains("Probe 1 → Memory issues and forgetfulness", r.Text);
        Assert.Contains("Submit Order (step reached)", r.Text);
        Assert.Contains("Captured: name, zip, address, payment card", r.Text);
        foreach (var leaked in new[] { "Margaret", "97470", "1 Main St", "4111", "670-4541", "Billing first name", "Billing Address" })
            Assert.DoesNotContain(leaked, r.Text);
        Assert.Equal(4, r.Redactions["personal details"]);
        Assert.Equal(1, r.Redactions["phone numbers"]);
    }

    [Fact]
    public void Build_DropsNavigationClicksAndReadAloudText()
    {
        var history = History(
            Step("script", "Closing — Order"),
            Step("input", "Press Hot Button", "Continue"),
            Step("input", "Main Offer", "Yes - Place Order"),
            Step("end", "End"));

        var text = CallSummaryContextBuilder.Build(Call(), null, null, [history]).Text;
        Assert.Contains("Main Offer → Yes - Place Order", text);
        Assert.DoesNotContain("Hot Button", text);
        Assert.DoesNotContain("Closing — Order", text);
        Assert.DoesNotContain("- End", text);
    }

    [Fact]
    public void Build_CollapsesBackToBackRepeats_ButKeepsGenuineRepeatsLater()
    {
        var history = History(
            Step("input", "Upsell", "Not Interested"),
            Step("input", "Upsell", "Not Interested"),   // recorded twice — one step
            Step("input", "Main Offer", "Yes"),
            Step("input", "Upsell", "Not Interested"));  // revisited later — keep it

        var text = CallSummaryContextBuilder.Build(Call(), null, null, [history]).Text;
        Assert.Equal(2, text.Split("Upsell → Not Interested").Length - 1);
    }

    [Theory]
    [InlineData("Yes at 2026-10-01T23:53:04.1471780+00:00", "Yes")]
    [InlineData("2026-10-01T23:53:04Z", "")]
    [InlineData("Order", "Order")]
    public void StripTimestamps(string input, string expected) =>
        Assert.Equal(expected, CallSummaryContextBuilder.StripTimestamps(input));

    [Fact]
    public void Build_NoScript_SaysSo() =>
        Assert.Contains("(no script steps recorded)", CallSummaryContextBuilder.Build(Call(), null, null, []).Text);

    [Fact]
    public void Build_FlagsPlaceholderAnswersAsAPossibleTestCall()
    {
        var history = History(
            Step("input", "Probe 1", "Memory issues"),
            Step("input", "Probe 2", "test"),
            Step("input", "Probe 3", "TEST"),
            Step("input", "Probe 4", "asdf"));

        var text = CallSummaryContextBuilder.Build(Call(), null, null, [history]).Text;
        Assert.Contains("Possible test call: placeholder answers (\"test\", \"TEST\", \"asdf\") at Probe 2, Probe 3, Probe 4", text);
    }

    [Fact]
    public void Build_RealAnswers_NoTestCallFlag() =>
        Assert.DoesNotContain("Possible test call",
            CallSummaryContextBuilder.Build(Call(), null, null, [History(Step("input", "Probe 1", "Testimonials helped me decide"))]).Text);
}
