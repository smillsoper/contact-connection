namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// Call-wide variable store shared between the CRM script flow and the telephony call flow for
/// the same call — backs the {{shared.*}} namespace on both sides. Distinct from each engine's
/// own local flow.* variables (CRM FlowExecutionContext.FlowVars / telephony
/// TelephonyCallSession.Vars), which stay private to that one flow/session and are never visible
/// to the other side. Keyed by CallRecordId — the one id both a telephony call and any CRM script
/// session started against it already share — so either side can read a value the other just
/// wrote, independent of session/channel lifecycle (e.g. a telephony branch triggered mid-call by
/// a CRM script's trigger_telephony_event node can report a result back for the CRM script, which
/// is already running and waiting, to pick up).
/// </summary>
public interface ISharedCallVariableStore
{
    Task<Dictionary<string, string>> GetAllAsync(Guid callRecordId, CancellationToken ct = default);
    Task SetAsync(Guid callRecordId, string key, string value, CancellationToken ct = default);
}
