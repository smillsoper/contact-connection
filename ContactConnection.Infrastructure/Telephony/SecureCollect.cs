using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.Telephony;

/// <summary>How a captured tf_secure_collect field's digits are validated after
/// play_and_get_digits' own min/max length + regex have already passed.</summary>
public static class SecureCollectValidation
{
    public const string None       = "none";
    public const string Luhn       = "luhn";        // credit-card PAN mod-10 check
    public const string ExpiryMmyy = "expiry_mmyy"; // 4 digits MMYY, month 01-12, not in the past
    public const string Cvv        = "cvv";         // 3 or 4 digits, that's the whole rule

    public static bool IsValid(string v) => v is None or Luhn or ExpiryMmyy or Cvv;
}

/// <summary>One prompt/collect step of a tf_secure_collect node.</summary>
/// <param name="Key">Sub-key the digits are stored under (SensitiveData JSON + <c>{{secure.Key}}</c>).</param>
/// <param name="PromptFileId">Audio ref for the prompt (resolved by the handler).</param>
/// <param name="MinDigits">play_and_get_digits min length.</param>
/// <param name="MaxDigits">play_and_get_digits max length.</param>
/// <param name="Terminator">Terminator key(s) or "none".</param>
/// <param name="Validation">A <see cref="SecureCollectValidation"/> value.</param>
public sealed record SecureCollectField(
    string Key,
    string? PromptFileId,
    int MinDigits,
    int MaxDigits,
    string Terminator,
    string Validation);

/// <summary>
/// Pure helpers for tf_secure_collect — shared between the node handler (builds the first
/// play_and_get_digits invocation) and EslBackgroundService (validates each captured field and
/// drives the next one). No plaintext ever passes through logging here; callers must keep it out
/// of session vars / traces too.
/// </summary>
public static class SecureCollect
{
    /// <summary>Parses the node's <c>fields</c> array. Rows missing a key are skipped; bad
    /// numbers / validation values fall back to safe defaults. Returns an empty list when the
    /// node has no usable fields (handler then takes <c>failed</c>).</summary>
    public static List<SecureCollectField> ParseFields(JsonNode? fieldsNode)
    {
        var result = new List<SecureCollectField>();
        if (fieldsNode is not JsonArray arr) return result;

        foreach (var item in arr)
        {
            if (item is not JsonObject o) continue;
            var key = o["key"]?.GetValue<string>()?.Trim();
            if (string.IsNullOrEmpty(key)) continue;

            var min = TryInt(o["minDigits"], 1);
            var max = TryInt(o["maxDigits"], Math.Max(min, 1));
            if (max < min) max = min;

            var term = o["terminator"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(term)) term = max > 1 ? "#" : "none";

            var validation = o["validation"]?.GetValue<string>()?.Trim().ToLowerInvariant() ?? SecureCollectValidation.None;
            if (!SecureCollectValidation.IsValid(validation)) validation = SecureCollectValidation.None;

            result.Add(new SecureCollectField(
                key,
                o["promptAudioFileId"]?.GetValue<string>(),
                min, max, term!, validation));
        }

        return result;
    }

    /// <summary>The anchored length regexp for play_and_get_digits, e.g. <c>^\d{3,4}$</c>.</summary>
    public static string LengthRegexp(int min, int max) =>
        min == max ? $@"^\d{{{min}}}$" : $@"^\d{{{min},{max}}}$";

    /// <summary>
    /// Sets the <c>cc_sc_*</c> channel vars for one capture step, immediately before a
    /// <c>uuid_transfer &lt;uuid&gt; secure_collect</c>. <paramref name="spec"/> is one entry of the
    /// <c>_sc_fields_json</c> array (<c>{k,min,max,term,val,prompt}</c>). Shared by the node
    /// handler (field 0) and EslBackgroundService (each subsequent field). All values are
    /// space-free — play_and_get_digits' arg parser is positional.
    /// </summary>
    public static async Task ApplyFieldVarsAsync(
        IEslCommander esl, string channelUuid, JsonObject spec,
        int maxTries, int timeoutMs, int interDigitMs, string invalidArg, CancellationToken ct)
    {
        var min    = spec["min"]!.GetValue<int>();
        var max    = spec["max"]!.GetValue<int>();
        var term   = spec["term"]!.GetValue<string>();
        var key    = spec["k"]!.GetValue<string>();
        var prompt = spec["prompt"]!.GetValue<string>();

        await esl.SetChannelVarAsync(channelUuid, "cc_sc_min", min.ToString(), ct);
        await esl.SetChannelVarAsync(channelUuid, "cc_sc_max", max.ToString(), ct);
        await esl.SetChannelVarAsync(channelUuid, "cc_sc_tries", maxTries.ToString(), ct);
        await esl.SetChannelVarAsync(channelUuid, "cc_sc_timeout", timeoutMs.ToString(), ct);
        await esl.SetChannelVarAsync(channelUuid, "cc_sc_term", term, ct);
        await esl.SetChannelVarAsync(channelUuid, "cc_sc_prompt", prompt, ct);
        await esl.SetChannelVarAsync(channelUuid, "cc_sc_invalid", invalidArg, ct);
        await esl.SetChannelVarAsync(channelUuid, "cc_sc_regex", LengthRegexp(min, max), ct);
        await esl.SetChannelVarAsync(channelUuid, "cc_sc_digit_timeout", interDigitMs.ToString(), ct);
        await esl.SetChannelVarAsync(channelUuid, "cc_sc_field", key, ct);
    }

    /// <summary>Post-length validation for a captured value. An empty string is always invalid
    /// (that's a timeout / no-entry, handled separately by the caller).</summary>
    public static bool Validate(string validation, string digits)
    {
        if (string.IsNullOrEmpty(digits) || !digits.All(char.IsDigit)) return false;
        return validation switch
        {
            SecureCollectValidation.Luhn       => LuhnValid(digits),
            SecureCollectValidation.ExpiryMmyy => ExpiryValid(digits),
            SecureCollectValidation.Cvv        => digits.Length is 3 or 4,
            _                                  => true,
        };
    }

    /// <summary>Mod-10 (Luhn) check used for card PANs.</summary>
    public static bool LuhnValid(string digits)
    {
        if (digits.Length < 12) return false;
        int sum = 0;
        bool dbl = false;
        for (int i = digits.Length - 1; i >= 0; i--)
        {
            int d = digits[i] - '0';
            if (dbl) { d *= 2; if (d > 9) d -= 9; }
            sum += d;
            dbl = !dbl;
        }
        return sum % 10 == 0;
    }

    /// <summary>MMYY, month 01-12, and the month is this month or later (2000-relative year).</summary>
    public static bool ExpiryValid(string digits) => ExpiryValid(digits, DateTimeOffset.UtcNow);

    internal static bool ExpiryValid(string digits, DateTimeOffset now)
    {
        if (digits.Length != 4) return false;
        int mm = (digits[0] - '0') * 10 + (digits[1] - '0');
        int yy = (digits[2] - '0') * 10 + (digits[3] - '0');
        if (mm is < 1 or > 12) return false;

        var expYear  = 2000 + yy;
        var lastValid = new DateTimeOffset(expYear, mm, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1).AddSeconds(-1);
        return lastValid >= now;
    }

    private static int TryInt(JsonNode? n, int fallback)
    {
        try { return n is null ? fallback : n.GetValue<int>(); }
        catch { return fallback; }
    }
}
