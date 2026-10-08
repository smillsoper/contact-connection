namespace ContactConnection.Domain.Entities;

// Platform health (S184): the Worker runs every check once a minute; the Portal's Health page shows them and Owners
// (plus any extra addresses) are emailed when one goes to warning / critical and again when it recovers.

public static class HealthStatus
{
    public const string Ok = "ok";
    public const string Warning = "warning";
    public const string Critical = "critical";
    /// <summary>Not measured (no data yet, or not applicable — e.g. too few calls to judge).</summary>
    public const string Unknown = "unknown";

    public static int Rank(string s) => s switch { Critical => 3, Warning => 2, Ok => 1, _ => 0 };
}

/// <summary>A check's definition: what it measures, its unit and its default warning / critical levels.</summary>
/// <param name="HigherIsWorse">True when a bigger value is worse (latency, failures); false when smaller is worse (free disk).</param>
/// <param name="Warn">Default warning level; null = the check decides its own status (up / down).</param>
public sealed record HealthCheckDefinition(string Key, string Area, string Name, string Unit, bool HigherIsWorse,
    double? Warn, double? Crit, string Description)
{
    /// <summary>Status for a value against the effective levels.</summary>
    public string Evaluate(double value, double? warn, double? crit)
    {
        bool Past(double? level) => level is { } l && (HigherIsWorse ? value >= l : value <= l);
        return Past(crit) ? HealthStatus.Critical : Past(warn) ? HealthStatus.Warning : HealthStatus.Ok;
    }
}

public static class HealthCatalog
{
    public const string Core = "Core services";
    public const string Telephony = "Telephony";
    public const string Jobs = "Background jobs";
    public const string Work = "Background work";
    public const string Integrations = "Integrations";
    public const string Expiring = "Expiring";

    public static readonly IReadOnlyList<HealthCheckDefinition> Checks =
    [
        new("api", Core, "API", "s since heartbeat", true, 90, 180, "The API reports in every 20 seconds."),
        new("worker", Core, "Worker", "s since heartbeat", true, 150, 300, "The Worker reports in every minute (watched by the API)."),
        new("postgres", Core, "PostgreSQL response", "ms", true, 250, 1000, "Time for a trivial query."),
        new("postgres_connections", Core, "PostgreSQL connections", "% used", true, 70, 90, "Open connections against the server's limit."),
        new("redis", Core, "Redis response", "ms", true, 100, 500, "Time for a PING."),
        new("storage", Core, "File storage free space", "% free", false, 15, 5, "Free space where recordings and files are kept."),

        new("esl", Telephony, "API ↔ FreeSWITCH", "", true, null, null, "The API's live event connection to FreeSWITCH — inbound calls stop without it."),
        new("freeswitch", Telephony, "FreeSWITCH", "ms", true, 500, 2000, "FreeSWITCH answers a status request."),
        new("trunk", Telephony, "SignalWire trunk", "", true, null, null, "The SIP trunk is registered with SignalWire."),
        new("turn", Telephony, "TURN relay", "ms", true, 500, 2000, "The relay remote softphones use answers."),
        new("live_calls", Telephony, "Live calls", "channels", true, null, null, "Channels up in FreeSWITCH right now (information only)."),
        new("abandon_rate", Telephony, "Abandoned in queue (15 min)", "%", true, 10, 20, "Callers who hung up waiting, of those who queued — judged once 10+ calls queued."),
        new("queue_wait", Telephony, "Longest wait in queue now", "s", true, 120, 300, "The caller waiting longest for an agent, across all tenants."),
        new("agent_connections", Telephony, "Agents with poor connections", "% on call", true, 20, 40, "Agents on a call whose softphone connection grades poor — judged once 3+ are on calls."),

        new("exports_failed", Work, "Failed export deliveries (24 h)", "", true, 1, 3, "Data export files that could not be delivered."),
        new("callbacks_late", Work, "Scheduled callbacks running late", "", true, 1, 5, "Callbacks more than 5 minutes past due and not yet dialed."),
        new("recordings_backlog", Work, "Recording merges waiting", "", true, 5, 25, "Recordings waiting over 30 minutes to be merged."),

        new("client_apis", Integrations, "Client APIs (1 h)", "% failed", true, 10, 25, "The worst client / vendor API's failures (5xx, no answer) — judged per API once it has 5+ calls."),
        new("api_circuits", Integrations, "Client API circuit breakers", "", true, null, null, "An API that kept failing gets its calls refused for 30 s at a time."),
        new("payments", Integrations, "Payment gateway errors (1 h)", "%", true, 5, 15, "Gateway errors, not declines — judged at 5+ payment attempts."),
        new("tax", Integrations, "Tax service errors (1 h)", "% failed", true, 10, 25, "Avalara unreachable or refusing our credentials — judged at 5+ calls."),
        new("tts", Integrations, "Text-to-speech errors (1 h)", "% failed", true, 10, 25, "Streaming TTS that failed or returned no audio — judged at 5+ prompts."),
        new("stt", Integrations, "Speech recognition errors (1 h)", "% failed", true, 10, 25, "Voice capture that failed — judged at 5+ captures."),
        new("email", Integrations, "Email send failures (1 h)", "", true, 1, 5, "Emails Resend couldn't send (invites, receipts, alerts, exports)."),
        new("stripe_webhooks", Integrations, "Stripe webhooks rejected (1 h)", "", true, 1, 3, "Usually means our webhook secret no longer matches Stripe's."),

        new("credentials", Expiring, "Soonest credential expiry", "days", false, 30, 7, "Credentials in Key Vault that have an expiry date set."),
        new("tls", Expiring, "TLS certificate", "days", false, 21, 7, "Days left on the website's certificate."),
    ];

    /// <summary>The Worker's background jobs (each reports a heartbeat every loop).</summary>
    public static HealthCheckDefinition Job(string name) =>
        new($"job:{name}", Jobs, name, "min since last run", true, null, null, "A Worker job that should run on a schedule.");

    public static HealthCheckDefinition? Find(string key) =>
        Checks.FirstOrDefault(c => c.Key == key) ?? (key.StartsWith("job:") ? Job(key[4..]) : null);
}

/// <summary>A check's current state, its threshold overrides, mute and acknowledgement.</summary>
public class PlatformHealthCheck
{
    public static readonly TimeSpan RepeatCriticalEvery = TimeSpan.FromMinutes(30);

    public string Key { get; private set; } = "";
    public string Status { get; private set; } = HealthStatus.Unknown;
    public double? Value { get; private set; }
    public string? Detail { get; private set; }
    public DateTimeOffset CheckedAt { get; private set; }
    public DateTimeOffset StatusSince { get; private set; }
    public double? WarnOverride { get; private set; }
    public double? CritOverride { get; private set; }
    public DateTimeOffset? MutedUntil { get; private set; }
    public DateTimeOffset? AcknowledgedAt { get; private set; }
    public string? AcknowledgedBy { get; private set; }
    public DateTimeOffset? LastAlertedAt { get; private set; }
    /// <summary>The status the last alert was about (so a recovery mail goes only after a problem mail).</summary>
    public string? LastAlertedStatus { get; private set; }

    private PlatformHealthCheck() { }

    public static PlatformHealthCheck New(string key, DateTimeOffset now) =>
        new() { Key = key, CheckedAt = now, StatusSince = now };

    public bool IsMuted(DateTimeOffset now) => MutedUntil is { } m && m > now;

    /// <summary>Records a result. Returns the previous status when it changed (null when unchanged).</summary>
    public string? Record(string status, double? value, string? detail, DateTimeOffset now)
    {
        Value = value;
        Detail = detail is { Length: > 500 } ? detail[..500] : detail;
        CheckedAt = now;
        if (status == Status) return null;
        var before = Status;
        Status = status;
        StatusSince = now;
        // A new problem (or a worse one) needs a fresh acknowledgement.
        if (HealthStatus.Rank(status) > HealthStatus.Rank(before)) { AcknowledgedAt = null; AcknowledgedBy = null; }
        return before;
    }

    /// <summary>
    /// What, if anything, to email now: "problem" when the status is warning / critical and either it changed since the
    /// last alert or it's an unacknowledged critical due a reminder; "recovered" once it's back to OK after a problem
    /// alert. Nothing while muted, and nothing for unknown.
    /// </summary>
    public string? AlertDue(DateTimeOffset now)
    {
        if (IsMuted(now)) return null;
        var problem = Status is HealthStatus.Warning or HealthStatus.Critical;
        if (problem)
        {
            if (LastAlertedStatus != Status) return "problem";
            if (Status == HealthStatus.Critical && AcknowledgedAt is null && LastAlertedAt is { } last && now - last >= RepeatCriticalEvery)
                return "problem";
            return null;
        }
        if (Status == HealthStatus.Ok && LastAlertedStatus is HealthStatus.Warning or HealthStatus.Critical) return "recovered";
        return null;
    }

    public void MarkAlerted(DateTimeOffset now)
    {
        LastAlertedAt = now;
        LastAlertedStatus = Status == HealthStatus.Ok ? null : Status;
    }

    public void Acknowledge(string by, DateTimeOffset now) { AcknowledgedAt = now; AcknowledgedBy = by; }

    public void Mute(DateTimeOffset? until) => MutedUntil = until;

    public void SetLevels(double? warn, double? crit) { WarnOverride = warn; CritOverride = crit; }
}

/// <summary>One minute's result, kept 7 days for the page's history strips.</summary>
public class HealthSample
{
    public long Id { get; private set; }
    public string Key { get; private set; } = "";
    public DateTimeOffset At { get; private set; }
    public string Status { get; private set; } = "";
    public double? Value { get; private set; }

    private HealthSample() { }
    public static HealthSample Of(string key, DateTimeOffset at, string status, double? value) =>
        new() { Key = key, At = at, Status = status, Value = value };
}

/// <summary>A stretch of time a check spent in warning / critical.</summary>
public class HealthIncident
{
    public Guid Id { get; private set; }
    public string Key { get; private set; } = "";
    /// <summary>The worst status reached.</summary>
    public string Severity { get; private set; } = HealthStatus.Warning;
    public string? Detail { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }

    private HealthIncident() { }

    public static HealthIncident Open(string key, string severity, string? detail, DateTimeOffset now) =>
        new() { Id = Guid.NewGuid(), Key = key, Severity = severity, Detail = detail, StartedAt = now };

    public void Worsen(string severity, string? detail)
    {
        if (HealthStatus.Rank(severity) > HealthStatus.Rank(Severity)) Severity = severity;
        Detail = detail ?? Detail;
    }

    public void Resolve(DateTimeOffset now) => ResolvedAt ??= now;
}

/// <summary>Health alert settings — one row.</summary>
public class HealthSettings
{
    public const int SingletonId = 1;
    public int Id { get; private set; } = SingletonId;
    /// <summary>Addresses emailed besides every Portal Owner (e.g. an ops mailbox).</summary>
    public List<string> ExtraRecipients { get; private set; } = [];

    public static HealthSettings Default() => new();

    public void SetRecipients(IEnumerable<string> emails)
    {
        var list = emails.Select(e => e.Trim().ToLowerInvariant()).Where(e => e.Length > 0).Distinct().ToList();
        if (list.Any(e => !e.Contains('@') || e.Length > 320)) throw new ArgumentException("Enter valid email addresses.");
        if (list.Count > 20) throw new ArgumentException("At most 20 extra addresses.");
        ExtraRecipients = list;
    }
}

/// <summary>Someone who has signed in to the Portal (recorded at sign-in) — Owners here get health alerts.</summary>
public class PlatformUser
{
    public string EntraOid { get; private set; } = "";
    public string Email { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string Role { get; private set; } = "";
    public DateTimeOffset LastSignInAt { get; private set; }

    private PlatformUser() { }

    public static PlatformUser Seen(string oid, string email, string name, string role, DateTimeOffset now) =>
        new() { EntraOid = oid, Email = email, Name = name, Role = role, LastSignInAt = now };

    public void SeenAgain(string email, string name, string role, DateTimeOffset now)
    {
        Email = email; Name = name; Role = role; LastSignInAt = now;
    }
}
