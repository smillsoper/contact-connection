using System.Text.RegularExpressions;

namespace ContactConnection.Infrastructure.Ai;

/// <summary>
/// Scrubs sensitive data from text before it is sent to an AI model (AI step 1, S171). Anything sent to
/// an outside API leaves the platform, so the rule is: send the minimum, and replace anything sensitive
/// with a labelled placeholder ("[card number]") — the model still knows something was there, which keeps
/// the summary coherent, but never sees the value. Counts what it replaced so the preview can show it.
///
/// Patterns are deliberately broad: a false positive (an order number masked as a phone number) costs a
/// little summary detail; a false negative leaks data.
/// </summary>
public sealed partial class AiRedactor
{
    public Dictionary<string, int> Counts { get; } = new();

    public string Scrub(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        text = Replace(CardNumber(), text, "[card number]", "card numbers");   // 13–19 digits, spaces/dashes allowed
        text = Replace(Ssn(), text, "[SSN]", "SSNs");
        text = Replace(Email(), text, "[email]", "emails");
        text = Replace(Phone(), text, "[phone]", "phone numbers");
        return text;
    }

    /// <summary>Replaces a whole value (e.g. an address the caller gave) and counts it under <paramref name="kind"/>.</summary>
    public string Withhold(string kind, string placeholder)
    {
        Counts[kind] = Counts.GetValueOrDefault(kind) + 1;
        return placeholder;
    }

    private string Replace(Regex pattern, string text, string placeholder, string kind) =>
        pattern.Replace(text, _ =>
        {
            Counts[kind] = Counts.GetValueOrDefault(kind) + 1;
            return placeholder;
        });

    [GeneratedRegex(@"\b(?:\d[ -]?){12,18}\d\b")]
    private static partial Regex CardNumber();

    [GeneratedRegex(@"\b\d{3}-\d{2}-\d{4}\b")]
    private static partial Regex Ssn();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")]
    private static partial Regex Email();

    // US numbers: +1 (541) 670-4541, 541.670.4541, 5416704541 …
    [GeneratedRegex(@"(?<!\d)(?:\+?1[\s.-]?)?\(?\d{3}\)?[\s.-]?\d{3}[\s.-]?\d{4}(?!\d)")]
    private static partial Regex Phone();
}
