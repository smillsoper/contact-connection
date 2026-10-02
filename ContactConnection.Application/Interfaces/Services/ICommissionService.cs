namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// Keeps a call's commission ledger entries in line with its commission rules (S171). Every method
/// is idempotent: it computes what the call should earn now and, only if that differs from the entries
/// in force, reverses those and writes the new ones. Safe to call from any trigger, any number of times.
/// </summary>
public interface ICommissionService
{
    /// <summary>Recompute after something that can change the result (script finished, a custom field
    /// edited). <paramref name="trigger"/> is noted on any reversal.</summary>
    Task RecalculateAsync(Guid callRecordId, string trigger, CancellationToken ct = default);

    /// <summary>The call's order went through: stamp the order time (first time only) and recompute.</summary>
    Task OrderSubmittedAsync(Guid callRecordId, CancellationToken ct = default);

    /// <summary>Admin: the call earns nothing (e.g. the order was cancelled) — reverses what's in force.</summary>
    Task ReverseAsync(Guid callRecordId, string reason, CancellationToken ct = default);

    /// <summary>Admin: undo <see cref="ReverseAsync"/> and recompute.</summary>
    Task RestoreAsync(Guid callRecordId, CancellationToken ct = default);
}

public static class CommissionTrigger
{
    public const string ScriptCompleted   = "script completed";
    public const string OrderSubmitted    = "order submitted";
    public const string CustomFieldEdited = "custom field edited";
    public const string OrderCancelled    = "order cancelled";
    public const string Restored          = "restored";
}
