using System.Text.Json;

namespace ContactConnection.Domain.Entities;

public static class DedicationMode
{
    /// <summary>From now for a number of minutes.</summary>
    public const string Duration = "duration";
    /// <summary>From now until a date and time.</summary>
    public const string Until    = "until";
    /// <summary>Weekly windows (days + times in the tenant's time zone), optionally ending on a date.</summary>
    public const string Schedule = "schedule";

    public static readonly IReadOnlyList<string> All = [Duration, Until, Schedule];
}

/// <summary>One weekly window: <see cref="Days"/> 0 = Sunday … 6 = Saturday; "HH:mm" times. An end at or before the start
/// runs past midnight into the next day (e.g. 22:00–02:00).</summary>
public record DedicationWindow(int[] Days, string Start, string End);

/// <summary>
/// A supervisor dedicates an agent to a set of campaigns (S183). While it's active the agent takes inbound calls ONLY from
/// those campaigns — their other assignments pause and resume by themselves when it ends — and the dedication itself
/// lets them take the dedicated campaigns' calls even without an assignment. Applied where routing decides who can
/// take a campaign's calls (EligibleAgentRanker). Several at once combine (any active one's campaigns).
/// </summary>
public class AgentDedication
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid AgentId { get; private set; }
    public List<Guid> CampaignIds { get; private set; } = [];
    public string Mode { get; private set; } = DedicationMode.Duration;
    public DateTimeOffset StartsAt { get; private set; }
    /// <summary>Duration / until: when it ends. Schedule: the optional last day (end of that day, tenant time).</summary>
    public DateTimeOffset? EndsAt { get; private set; }
    /// <summary>Schedule only — the weekly windows as JSON.</summary>
    public string? WindowsJson { get; private set; }
    /// <summary>The tenant's IANA time zone when it was created — schedule windows are read in it.</summary>
    public string TimeZone { get; private set; } = "UTC";
    public string? Note { get; private set; }
    public Guid CreatedById { get; private set; }
    public string CreatedByName { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    /// <summary>Ended early by a supervisor.</summary>
    public DateTimeOffset? EndedAt { get; private set; }
    public Guid? EndedById { get; private set; }

    private AgentDedication() { }

    public static AgentDedication Create(Guid tenantId, Guid agentId, IEnumerable<Guid> campaignIds, string mode,
        DateTimeOffset now, DateTimeOffset? endsAt, IReadOnlyList<DedicationWindow>? windows, string timeZone,
        string? note, Guid createdById, string createdByName)
    {
        var campaigns = campaignIds.Distinct().ToList();
        if (campaigns.Count == 0) throw new ArgumentException("Choose at least one campaign.");
        if (!DedicationMode.All.Contains(mode)) throw new ArgumentException("Unknown dedication type.");
        if ((mode is DedicationMode.Duration or DedicationMode.Until) && (endsAt is null || endsAt <= now))
            throw new ArgumentException("The end must be in the future.");
        if (mode == DedicationMode.Schedule)
        {
            if (windows is null || windows.Count == 0) throw new ArgumentException("Add at least one weekly window.");
            foreach (var w in windows)
            {
                if (w.Days is null || w.Days.Length == 0 || w.Days.Any(d => d is < 0 or > 6)) throw new ArgumentException("Each window needs at least one day.");
                if (!TimeOnly.TryParse(w.Start, out var s) || !TimeOnly.TryParse(w.End, out var e)) throw new ArgumentException("Window times must be like 09:00.");
                if (s == e) throw new ArgumentException("A window's start and end can't be the same time.");
            }
            if (endsAt is { } last && last <= now) throw new ArgumentException("The schedule's last day must be in the future.");
        }
        return new AgentDedication
        {
            Id = Guid.NewGuid(), TenantId = tenantId, AgentId = agentId, CampaignIds = campaigns, Mode = mode,
            // Stored as UTC (Postgres timestamptz takes offset 0 only).
            StartsAt = now.ToUniversalTime(), EndsAt = endsAt?.ToUniversalTime(),
            WindowsJson = mode == DedicationMode.Schedule ? JsonSerializer.Serialize(windows) : null,
            TimeZone = string.IsNullOrWhiteSpace(timeZone) ? "UTC" : timeZone,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()[..Math.Min(note.Trim().Length, 200)],
            CreatedById = createdById, CreatedByName = createdByName, CreatedAt = now,
        };
    }

    public IReadOnlyList<DedicationWindow> Windows =>
        string.IsNullOrEmpty(WindowsJson) ? [] : JsonSerializer.Deserialize<List<DedicationWindow>>(WindowsJson) ?? [];

    /// <summary>Not ended early and not past its end — it may still be outside a schedule window right now.</summary>
    public bool IsCurrent(DateTimeOffset now) => EndedAt is null && (EndsAt is null || now < EndsAt);

    public bool IsActiveAt(DateTimeOffset now)
    {
        if (EndedAt is not null || now < StartsAt || (EndsAt is { } end && now >= end)) return false;
        if (Mode != DedicationMode.Schedule) return true;
        var local = TimeZoneInfo.ConvertTime(now, Zone());
        var t = TimeOnly.FromDateTime(local.DateTime);
        var today = (int)local.DayOfWeek;
        var yesterday = (today + 6) % 7;
        foreach (var w in Windows)
        {
            var s = TimeOnly.Parse(w.Start); var e = TimeOnly.Parse(w.End);
            if (e > s) { if (w.Days.Contains(today) && t >= s && t < e) return true; }
            else if ((w.Days.Contains(today) && t >= s) || (w.Days.Contains(yesterday) && t < e)) return true;
        }
        return false;
    }

    /// <summary>The next moment this dedication switches on or off after <paramref name="now"/> (null: never again) —
    /// the agent portal refreshes its queue then.</summary>
    public DateTimeOffset? NextChangeAfter(DateTimeOffset now)
    {
        if (EndedAt is not null || (EndsAt is { } end0 && now >= end0)) return null;
        var candidates = new List<DateTimeOffset>();
        if (StartsAt > now) candidates.Add(StartsAt);
        if (EndsAt is { } end && end > now) candidates.Add(end);
        if (Mode == DedicationMode.Schedule)
        {
            var zone = Zone();
            var localToday = TimeZoneInfo.ConvertTime(now, zone).Date;
            for (var d = -1; d <= 8; d++)
            {
                var day = localToday.AddDays(d);
                foreach (var w in Windows.Where(w => w.Days.Contains((int)day.DayOfWeek)))
                {
                    var s = TimeOnly.Parse(w.Start); var e = TimeOnly.Parse(w.End);
                    var startLocal = day.Add(s.ToTimeSpan());
                    var endLocal = (e > s ? day : day.AddDays(1)).Add(e.ToTimeSpan());
                    foreach (var local in new[] { startLocal, endLocal })
                    {
                        var utc = new DateTimeOffset(local, zone.GetUtcOffset(local));
                        if (utc > now && (EndsAt is null || utc < EndsAt)) candidates.Add(utc);
                    }
                }
            }
        }
        return candidates.Count == 0 ? null : candidates.Min();
    }

    public void End(Guid byId, DateTimeOffset now)
    {
        if (EndedAt is not null) return;
        EndedAt = now.ToUniversalTime(); EndedById = byId;
    }

    private TimeZoneInfo Zone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(TimeZone); } catch (Exception) { return TimeZoneInfo.Utc; }
    }
}
