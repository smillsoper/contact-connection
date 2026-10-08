using System.Collections.Concurrent;
using ContactConnection.Api.Hubs;
using ContactConnection.Application.Services;
using Microsoft.AspNetCore.SignalR;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// Connection health (S183): each agent portal reports its softphone's live call statistics (WebRTC getStats — loss,
/// jitter, round trip, relay, mic level) every few seconds on a call and its network now and then when idle. The server
/// scores it (E-model MOS → Good / Fair / Poor, plus "no mic audio") and pushes changes to supervisors' Agent Lists.
/// Kept in memory only — it's a live view, not a record.
/// </summary>
public static class AgentHealthEndpoints
{
    public static IEndpointRouteBuilder MapAgentHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/agent-health", Report).RequireAuthorization();
        return app;
    }

    public record HealthReport(bool OnCall, double? LossInPct, double? LossOutPct, double? JitterMs, double? RttMs, bool? Relay,
        double? MicLevel, bool? MicSilent, bool? Muted, bool? Registered, string? NetworkType, double? DownlinkMbps);

    private static double? Clamp(double? v, double lo, double hi) => v is null || double.IsNaN(v.Value) ? null : Math.Clamp(v.Value, lo, hi);

    /// <summary>Simplified ITU-T G.107 E-model: delay (round trip + jitter buffer) and loss → R → MOS (1–4.5).</summary>
    public static double? Mos(double? rttMs, double? jitterMs, double? lossPct)
    {
        if (rttMs is null && jitterMs is null && lossPct is null) return null;
        var latency = (rttMs ?? 0) / 2 + (jitterMs ?? 0) * 2 + 10;
        var r = latency < 160 ? 93.2 - latency / 40 : 93.2 - (latency - 120) / 10;
        r -= (lossPct ?? 0) * 2.5;
        r = Math.Clamp(r, 0, 100);
        return Math.Round(1 + 0.035 * r + 0.000007 * r * (r - 60) * (100 - r), 2);
    }

    public static string Grade(double? mos, bool micSilent) =>
        micSilent ? "poor" : mos is null ? "unknown" : mos >= 4.0 ? "good" : mos >= 3.6 ? "fair" : "poor";

    private static IResult Report(HealthReport r, TenantContext tc, AgentHealthStore store, IHubContext<FlowHub, IFlowHubClient> hub, HttpContext http)
    {
        if (tc.Current is not { } tenant || !Guid.TryParse(http.User.FindFirst("sub")?.Value, out var me)) return Results.Unauthorized();
        var lossIn = Clamp(r.LossInPct, 0, 100);
        var lossOut = Clamp(r.LossOutPct, 0, 100);
        var jitter = Clamp(r.JitterMs, 0, 5000);
        var rtt = Clamp(r.RttMs, 0, 10000);
        var worstLoss = lossIn is null && lossOut is null ? (double?)null : Math.Max(lossIn ?? 0, lossOut ?? 0);
        var micSilent = r.OnCall && r.MicSilent == true && r.Muted != true;
        var mos = r.OnCall ? Mos(rtt, jitter, worstLoss) : null;
        var health = new AgentHealth(r.OnCall, mos, r.OnCall ? Grade(mos, micSilent) : "idle", lossIn, lossOut, jitter, rtt, r.Relay,
            Clamp(r.MicLevel, 0, 1), micSilent, r.Muted == true, r.Registered, r.NetworkType?.Length > 12 ? null : r.NetworkType,
            Clamp(r.DownlinkMbps, 0, 10000), DateTimeOffset.UtcNow);
        if (store.Put(tenant.Id, me, health))
            _ = hub.Clients.Group($"supervisor:{tenant.Id}").ReceiveAgentHealth(me.ToString(), health);
        return Results.Ok(health);
    }
}

/// <summary>An agent's latest connection health (what the Agent List shows).</summary>
public record AgentHealth(bool OnCall, double? Mos, string Grade, double? LossInPct, double? LossOutPct, double? JitterMs, double? RttMs,
    bool? Relay, double? MicLevel, bool MicSilent, bool Muted, bool? Registered, string? NetworkType, double? DownlinkMbps, DateTimeOffset At);

/// <summary>Latest health per agent, in memory. <see cref="Put"/> says whether supervisors should be pushed: the grade or
/// the mic warning changed, or 15 s passed since the last push for this agent.</summary>
public class AgentHealthStore
{
    public static readonly TimeSpan Stale = TimeSpan.FromSeconds(90);
    private readonly ConcurrentDictionary<(Guid, Guid), (AgentHealth Health, DateTimeOffset Pushed)> _latest = new();

    public bool Put(Guid tenantId, Guid agentId, AgentHealth h)
    {
        var key = (tenantId, agentId);
        var push = !_latest.TryGetValue(key, out var prev)
            || prev.Health.Grade != h.Grade || prev.Health.MicSilent != h.MicSilent || prev.Health.OnCall != h.OnCall
            || h.At - prev.Pushed >= TimeSpan.FromSeconds(15);
        _latest[key] = (h, push ? h.At : prev.Pushed);
        return push;
    }

    /// <summary>Fresh readings only — an agent whose portal stopped reporting has no health shown.</summary>
    public AgentHealth? Get(Guid tenantId, Guid agentId) =>
        _latest.TryGetValue((tenantId, agentId), out var v) && DateTimeOffset.UtcNow - v.Health.At < Stale ? v.Health : null;
}
