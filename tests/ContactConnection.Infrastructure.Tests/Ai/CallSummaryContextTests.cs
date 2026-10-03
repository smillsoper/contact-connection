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

    [Fact]
    public void Build_ShowsTheScriptAsWorked_AndWithholdsPersonalData()
    {
        var call = CallRecord.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var history = History(
            Step("section", "Opening"),
            Step("input", "First Name", "Margaret"),
            Step("set_variable", "Billing first name = first name"),
            Step("input", "Probe 1", "Memory issues and forgetfulness"),
            Step("address", "Billing Address", "{\"address1\":\"1 Main St\",\"zip\":\"97470\"}"),
            Step("tf_secure_collect", "Card", "4111111111111111"),
            Step("input", "Callback number", "541-670-4541"),
            Step("api_call", "Submit Order"));

        var r = CallSummaryContextBuilder.Build(call, "Life Seasons", "NeuroQ - LF TV", [history]);

        Assert.Contains("Life Seasons / NeuroQ - LF TV", r.Text);
        Assert.Contains("== Opening ==", r.Text);
        Assert.Contains("Probe 1 → Memory issues and forgetfulness", r.Text);
        Assert.Contains("Submit Order (step reached)", r.Text);
        Assert.DoesNotContain("Margaret", r.Text);
        Assert.DoesNotContain("1 Main St", r.Text);
        Assert.DoesNotContain("4111", r.Text);
        Assert.DoesNotContain("670-4541", r.Text);
        Assert.DoesNotContain("Billing first name", r.Text);   // script plumbing skipped
        Assert.Equal(1, r.Redactions["names"]);
        Assert.Equal(2, r.Redactions["personal details"]);
        Assert.Equal(1, r.Redactions["phone numbers"]);
    }

    [Fact]
    public void Build_NoScript_SaysSo() =>
        Assert.Contains("(no script steps recorded)",
            CallSummaryContextBuilder.Build(CallRecord.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), null, null, []).Text);
}
