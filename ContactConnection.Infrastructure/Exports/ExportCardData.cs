using System.Text.RegularExpressions;

namespace ContactConnection.Infrastructure.Exports;

/// <summary>
/// The <c>card</c> object a card-data export's templates see (S182), built from the call's captured card fields (the
/// tf_secure_collect blob — its keys are whatever the script named them, so the well-known values are found by key name,
/// and the number also by shape). Masked for previews and test files: every digit but the number's last four becomes X.
///
///   card.number, card.last4, card.brand, card.expiration (as captured), card.exp_month, card.exp_year (4 digits),
///   card.cvv, card.zip, card.fields.&lt;key&gt; (every captured field)
/// </summary>
public static partial class ExportCardData
{
    private static readonly string[] NumberKeys = ["card_number", "cardnumber", "card", "pan", "cc_number", "number", "account_number"];
    private static readonly string[] CvvKeys = ["cvv", "cvc", "cvv2", "cvc2", "cid", "security_code", "csc"];

    public static string? Number(IReadOnlyDictionary<string, string> fields)
    {
        foreach (var k in NumberKeys)
            if (fields.TryGetValue(k, out var v) && Digits(v) is { Length: >= 12 } d) return d;
        return fields.Values.Select(Digits).FirstOrDefault(d => d.Length is >= 13 and <= 19 && Luhn(d));
    }

    public static Dictionary<string, object?> Build(IReadOnlyDictionary<string, string> raw, bool mask)
    {
        var fields = new Dictionary<string, string>(raw, StringComparer.OrdinalIgnoreCase);
        var number = Number(fields) ?? "";
        var exp = fields.FirstOrDefault(kv => kv.Key.Contains("exp", StringComparison.OrdinalIgnoreCase)).Value ?? "";
        var cvv = fields.FirstOrDefault(kv => CvvKeys.Contains(kv.Key, StringComparer.OrdinalIgnoreCase)
                                              || kv.Key.Contains("cvv", StringComparison.OrdinalIgnoreCase)
                                              || kv.Key.Contains("cvc", StringComparison.OrdinalIgnoreCase)).Value ?? "";
        var zip = fields.FirstOrDefault(kv => kv.Key.Contains("zip", StringComparison.OrdinalIgnoreCase)
                                              || kv.Key.Contains("postal", StringComparison.OrdinalIgnoreCase)).Value ?? "";
        var expDigits = Digits(exp);
        var (month, year) = expDigits.Length switch
        {
            4 => (expDigits[..2], "20" + expDigits[2..]),          // MMYY
            6 => (expDigits[..2], expDigits[2..]),                 // MMYYYY
            _ => ("", ""),
        };
        var last4 = number.Length >= 4 ? number[^4..] : "";

        string M(string v) => mask ? MaskDigits(v) : v;
        return new Dictionary<string, object?>
        {
            ["number"] = mask && number.Length > 4 ? new string('X', number.Length - 4) + last4 : number,
            ["last4"] = last4,
            ["brand"] = Brand(number),
            ["expiration"] = M(exp),
            ["exp_month"] = M(month),
            ["exp_year"] = M(year),
            ["cvv"] = M(cvv),
            ["zip"] = zip,
            ["fields"] = fields.ToDictionary(kv => kv.Key, kv => (object?)(mask
                ? Digits(kv.Value) == number && number.Length > 4 ? new string('X', number.Length - 4) + last4 : MaskDigits(kv.Value)
                : kv.Value)),
        };
    }

    public static string Brand(string number) => number switch
    {
        _ when number.StartsWith('4') => "Visa",
        _ when number.Length >= 2 && number[..2] is "34" or "37" => "American Express",
        _ when number.Length >= 2 && int.Parse(number[..2]) is >= 51 and <= 55 => "Mastercard",
        _ when number.Length >= 4 && int.Parse(number[..4]) is >= 2221 and <= 2720 => "Mastercard",
        _ when number.StartsWith("6011") || number.StartsWith("65") || (number.Length >= 3 && int.Parse(number[..3]) is >= 644 and <= 649) => "Discover",
        _ when number.Length >= 2 && number[..2] is "36" or "38" or "30" => "Diners Club",
        _ when number.StartsWith("35") => "JCB",
        "" => "",
        _ => "Card",
    };

    public static bool Luhn(string digits)
    {
        var sum = 0;
        var alt = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var n = digits[i] - '0';
            if (alt) { n *= 2; if (n > 9) n -= 9; }
            sum += n;
            alt = !alt;
        }
        return digits.Length > 0 && sum % 10 == 0;
    }

    /// <summary>Does a template use the card variables? A layout without the card-data switch may not.</summary>
    public static bool UsesCard(string? template) => template is not null && CardReference().IsMatch(template);

    private static string Digits(string? v) => new((v ?? "").Where(char.IsDigit).ToArray());
    private static string MaskDigits(string v) => new(v.Select(ch => char.IsDigit(ch) ? 'X' : ch).ToArray());

    [GeneratedRegex(@"\bcard\s*(\.|\[)", RegexOptions.IgnoreCase)]
    private static partial Regex CardReference();
}
