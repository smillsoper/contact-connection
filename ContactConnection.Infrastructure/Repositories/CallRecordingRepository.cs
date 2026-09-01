using System.Collections.Concurrent;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.ValueObjects;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Repositories;

/// <summary>
/// Schema-scoped append of recording-lifecycle events to a call record. Registered as a
/// singleton (it holds a per-call write lock) — safe because its only dependency,
/// <see cref="ITenantDbContextFactory"/>, is itself a singleton that mints a fresh context
/// per call.
/// </summary>
public class CallRecordingRepository(ITenantDbContextFactory factory) : ICallRecordingRepository
{
    // One lock per call record so two near-simultaneous events (e.g. a field-focus mask and a
    // disconnect-driven stop) serialise their read-modify-write of the recording_events JSONB
    // instead of racing and losing one.
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public async Task<bool> AppendEventAsync(
        string tenantSchemaName, Guid callRecordId, RecordingEvent evt, CancellationToken ct = default)
    {
        var gate = _locks.GetOrAdd(callRecordId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            await using var db = factory.Create(tenantSchemaName);

            var record = await db.CallRecords.FirstOrDefaultAsync(r => r.Id == callRecordId, ct);
            if (record is null) return false;

            record.AppendRecordingEvent(evt);

            // The recording_events value converter has no ValueComparer, so EF's change
            // detection won't see the in-place list mutation on its own — mark it explicitly.
            db.Entry(record).Property(r => r.RecordingEvents).IsModified = true;

            await db.SaveChangesAsync(ct);
            return true;
        }
        finally
        {
            gate.Release();
        }
    }
}
