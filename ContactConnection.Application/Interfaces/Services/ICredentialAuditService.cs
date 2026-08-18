namespace ContactConnection.Application.Interfaces.Services;

public record CredentialAuditEntrySummary(
    string KeyName,
    string Action,
    Guid ActorId,
    string ActorName,
    DateTimeOffset CreatedAt);

/// <summary>
/// Append-only audit log for credential Set/Delete — records THAT a change happened (who, when,
/// which key, which action), never the secret value. See API_HARDENING_CHECKLIST.md Tier 1,
/// credential audit trail. Two implementations exist (resolved via keyed DI: "tenant"/"portal"),
/// same split as IVersionHistoryService, since tenant and portal credentials persist to different
/// DbContexts.
/// </summary>
public interface ICredentialAuditService
{
    Task RecordAsync(
        string keyName, string action, Guid actorId, string actorName, CancellationToken ct = default);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<CredentialAuditEntrySummary>> ListAsync(
        string keyName, CancellationToken ct = default);
}
