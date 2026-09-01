using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Repositories;

public interface IScreenRecordingRepository
{
    Task<ScreenRecording?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ScreenRecording>> ListByCallRecordAsync(Guid callRecordId, CancellationToken ct = default);
    Task AddAsync(ScreenRecording recording, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
