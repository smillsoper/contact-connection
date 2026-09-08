namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// One-shot maintenance sweep, run on API startup, that closes calls left in a non-terminal
/// state by a previous process that stopped mid-call — a hard restart, a crash, or a dev Ctrl-C
/// with no graceful CHANNEL_HANGUP and no reconciliation. Without it, every such event
/// permanently strands a "phantom active call" on the supervisor dashboard: both the
/// <c>call_records</c> row (still <c>overall_status = active</c>, no <c>call_end_at</c>) and its
/// <c>call_state_history</c> timeline (latest row not Completed/Abandoned).
///
/// A call is only closed when it has <b>no live Redis telephony session</b> AND its record is
/// older than a short grace window (<c>Telephony:OrphanReconciliation:MinAgeMinutes</c>,
/// default 15) — so a call that is genuinely still in progress during a quick restart is never
/// touched. Idempotent; safe to run on every boot.
/// </summary>
public interface IOrphanedCallReconciler
{
    Task<OrphanedCallReconciliationResult> RunAsync(CancellationToken ct = default);
}

public record OrphanedCallReconciliationResult(
    int TenantsScanned,
    int CallRecordsClosed,
    int StateTimelinesClosed);
