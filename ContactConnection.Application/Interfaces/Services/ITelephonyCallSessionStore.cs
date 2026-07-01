namespace ContactConnection.Application.Interfaces.Services;

public interface ITelephonyCallSessionStore
{
    Task SaveAsync(TelephonyCallSession session, CancellationToken ct = default);
    Task<TelephonyCallSession?> GetAsync(string channelUuid, CancellationToken ct = default);
    Task DeleteAsync(string channelUuid, CancellationToken ct = default);
}
