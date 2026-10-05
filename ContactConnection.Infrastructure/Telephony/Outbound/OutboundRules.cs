using ContactConnection.Domain.Entities;

namespace ContactConnection.Infrastructure.Telephony.Outbound;

/// <summary>US numbers for manual outbound (S179) — same NANP shape the dialplan accepts.</summary>
public static class OutboundNumber
{
    /// <summary>+1NXXNXXXXXX, or null when it isn't a dialable US number.</summary>
    public static string? ToE164(string? input)
    {
        var d = new string((input ?? "").Where(char.IsDigit).ToArray());
        if (d.Length == 11 && d[0] == '1') d = d[1..];
        if (d.Length != 10 || d[0] < '2' || d[3] < '2') return null;
        return "+1" + d;
    }

    /// <summary>(541) 641-3898 for display.</summary>
    public static string Display(string e164)
    {
        var d = e164.StartsWith("+1") ? e164[2..] : e164;
        return d.Length == 10 ? $"({d[..3]}) {d[3..6]}-{d[6..]}" : e164;
    }
}

/// <summary>
/// The caller ID a manual outbound call presents (S179) — decided here, server-side, and the same rule backs the
/// softphone's "Calling as …" preview, so the preview can never disagree with the real call. A campaign dial uses the
/// campaign's caller ID, else the tenant default; a direct dial uses the tenant default.
/// </summary>
public static class OutboundCallerId
{
    public record Choice(string? CallerId, bool IsTenantDefault);

    public static Choice Resolve(Campaign? campaign, string? tenantDefault)
    {
        if (campaign is not null && OutboundNumber.ToE164(campaign.CallerIdNumber) is { } own) return new(own, false);
        return new(OutboundNumber.ToE164(tenantDefault), true);
    }
}

/// <summary>
/// US state → the IANA time zones it spans (S179). Used to find a callee's local time from a prior call's address
/// state when the area-code / ZIP databases (Slice B) aren't in yet. States split across zones list every zone, and the
/// calling window must hold in all of them (strictest wins). Includes unofficial / partial zones where they exist
/// (Phenix City AL, the Navajo Nation in AZ, West Wendover NV) — erring toward not calling.
/// </summary>
public static class StateTimeZones
{
    private const string Eastern = "America/New_York", Central = "America/Chicago", Mountain = "America/Denver",
        Arizona = "America/Phoenix", Pacific = "America/Los_Angeles", Alaska = "America/Anchorage",
        Aleutian = "America/Adak", Hawaii = "Pacific/Honolulu";

    private static readonly Dictionary<string, string[]> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AL"] = [Central, Eastern], ["AK"] = [Alaska, Aleutian], ["AZ"] = [Arizona, Mountain], ["AR"] = [Central],
        ["CA"] = [Pacific], ["CO"] = [Mountain], ["CT"] = [Eastern], ["DE"] = [Eastern], ["DC"] = [Eastern],
        ["FL"] = [Eastern, Central], ["GA"] = [Eastern], ["HI"] = [Hawaii], ["ID"] = [Mountain, Pacific],
        ["IL"] = [Central], ["IN"] = [Eastern, Central], ["IA"] = [Central], ["KS"] = [Central, Mountain],
        ["KY"] = [Eastern, Central], ["LA"] = [Central], ["ME"] = [Eastern], ["MD"] = [Eastern], ["MA"] = [Eastern],
        ["MI"] = [Eastern, Central], ["MN"] = [Central], ["MS"] = [Central], ["MO"] = [Central], ["MT"] = [Mountain],
        ["NE"] = [Central, Mountain], ["NV"] = [Pacific, Mountain], ["NH"] = [Eastern], ["NJ"] = [Eastern],
        ["NM"] = [Mountain], ["NY"] = [Eastern], ["NC"] = [Eastern], ["ND"] = [Central, Mountain], ["OH"] = [Eastern],
        ["OK"] = [Central], ["OR"] = [Pacific, Mountain], ["PA"] = [Eastern], ["RI"] = [Eastern], ["SC"] = [Eastern],
        ["SD"] = [Central, Mountain], ["TN"] = [Central, Eastern], ["TX"] = [Central, Mountain], ["UT"] = [Mountain],
        ["VT"] = [Eastern], ["VA"] = [Eastern], ["WA"] = [Pacific], ["WV"] = [Eastern], ["WI"] = [Central],
        ["WY"] = [Mountain], ["PR"] = ["America/Puerto_Rico"], ["VI"] = ["America/St_Thomas"], ["GU"] = ["Pacific/Guam"],
    };

    public static IReadOnlyList<string>? For(string? state) =>
        state is { Length: 2 } && Map.TryGetValue(state.Trim(), out var zones) ? zones : null;
}

/// <summary>Is it inside the campaign's calling window, in the callee's local time, in every zone they might be in?</summary>
public static class OutboundCallingWindow
{
    public record Verdict(bool Allowed, string? Reason);

    public static Verdict Check(TimeOnly start, TimeOnly end, IReadOnlyList<string> timeZones, DateTimeOffset nowUtc)
    {
        foreach (var zone in timeZones)
        {
            TimeZoneInfo tz;
            try { tz = TimeZoneInfo.FindSystemTimeZoneById(zone); }
            catch (TimeZoneNotFoundException) { continue; }
            var local = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, tz).DateTime);
            if (local < start || local >= end)
                return new(false,
                    $"Outside calling hours: it's {local:h:mm tt} for the customer ({zone}). " +
                    $"This campaign calls {start:h:mm tt} – {end:h:mm tt} in the customer's local time.");
        }
        return new(true, null);
    }
}
