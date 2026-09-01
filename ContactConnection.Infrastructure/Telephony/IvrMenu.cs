using System.Text.RegularExpressions;

namespace ContactConnection.Infrastructure.Telephony;

/// <summary>
/// Pure helpers for the tf_ivr_menu node — shared between the node handler (which builds the
/// FreeSWITCH play_and_get_digits invocation) and EslBackgroundService (which resolves the
/// collected digits back to a transition target on CHANNEL_EXECUTE_COMPLETE).
/// </summary>
public static class IvrMenu
{
    /// <summary>
    /// Builds the <c>play_and_get_digits</c> validation regexp from the menu's option keys —
    /// an anchored alternation, e.g. <c>^(1|2|9)$</c>. Falls back to "any digits" when the menu
    /// has no fixed options.
    /// </summary>
    public static string BuildRegexp(IEnumerable<string?> optionDigits)
    {
        var alts = optionDigits
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d => Regex.Escape(d!.Trim()))
            .Distinct()
            .ToList();

        return alts.Count == 0 ? @"\d+" : $"^({string.Join("|", alts)})$";
    }

    /// <summary>
    /// Maps the digits the caller entered to the target node id. Empty/unmatched input falls
    /// through to <paramref name="noMatchTarget"/> (which may itself be null → dead-end the branch).
    /// </summary>
    public static string? ResolveTarget(
        string? digits, IReadOnlyDictionary<string, string> options, string? noMatchTarget)
    {
        if (!string.IsNullOrEmpty(digits)
            && options.TryGetValue(digits, out var target)
            && !string.IsNullOrEmpty(target))
            return target;

        return string.IsNullOrEmpty(noMatchTarget) ? null : noMatchTarget;
    }
}
