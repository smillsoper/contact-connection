using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

/// <summary>
/// tf_repeat — a general-purpose bounded loop primitive. Single entry point like every other
/// node; two source transitions, "repeat" and "finished". A tenant wires whatever nodes they want
/// onto the "repeat" branch and connects that chain's tail back to this SAME node's entry handle
/// — the canvas graph itself is the loop, not a special nested/structured construct. Motivating
/// use case: a "ring an agent extension, wait a beat (tf_delay), try again up to N times" retry
/// loop, but this is deliberately generic — any "do this again up to N times" flow works.
///
/// Mechanics (locked with the user, S138; count semantics corrected same session after a live
/// test showed the original reading was off by one): every time control reaches this node's
/// entry — the very first arrival from upstream, and every subsequent arrival via the tenant's
/// own loop-back wire — its counter increments. "repeat" fires for every hit up to and including
/// repeatCount; the FIRST hit past repeatCount exits "finished" instead. So with repeatCount = 3,
/// the loop body wired on "repeat" runs exactly 3 times (hits 1, 2, 3 → repeat) and "finished"
/// fires on hit 4 — matching what a tenant reading "repeat count: 3" would actually expect,
/// rather than the loop body running one fewer time than the configured number.
///
/// Pure ctx.Vars bookkeeping — no telephony I/O, no suspend. The counter is keyed by this node's
/// own id (TelephonyFlowEngine stamps node["nodeId"] before dispatch) so multiple tf_repeat nodes
/// in one flow don't collide, and lives entirely in the normal synchronous-step var sync (see
/// TelephonyFlowEngine.ExecuteAsync/ResumeFromNodeAsync's ctx.Vars → session.Vars flush after
/// every step) — no manual session-store access needed, unlike a suspending node such as
/// tf_delay. The engine's own MaxIterations (50) is the backstop against a misconfigured loop
/// whose body never suspends; no additional cap is enforced here.
/// </summary>
public class RepeatNodeHandler : ITelephonyNodeHandler
{
    public string NodeType => "tf_repeat";

    public Task<TelephonyNodeResult> ExecuteAsync(
        JsonObject node, TelephonyFlowContext ctx, CancellationToken ct = default)
    {
        var nodeId      = node["nodeId"]?.GetValue<string>() ?? "tf_repeat";
        var repeatCount = Math.Max(1, node["repeatCount"]?.GetValue<int>() ?? 1);
        var transitions = node["transitions"]?.AsObject();
        var counterKey  = $"_repeat_{nodeId}_count";

        var count = int.TryParse(ctx.Vars.GetValueOrDefault(counterKey), out var c) ? c : 0;
        count++;

        if (count > repeatCount)
        {
            // Reset so a later, unrelated re-entry into this same node (e.g. from an outer loop,
            // or a fresh call reusing a cached flow definition) starts its own count from zero
            // rather than inheriting this cycle's leftover state.
            ctx.Vars.Remove(counterKey);
            var finishedNode = transitions?["finished"]?.GetValue<string>();
            return Task.FromResult(new TelephonyNodeResult(finishedNode, "finished"));
        }

        ctx.Vars[counterKey] = count.ToString();
        var repeatNode = transitions?["repeat"]?.GetValue<string>();
        return Task.FromResult(new TelephonyNodeResult(repeatNode, "repeat"));
    }
}
