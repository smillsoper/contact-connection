using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Repositories;

/// <summary>
/// <see cref="CallRecordRepository.FindRetainedRecordingIdsOldestFirstAsync"/> — the query the
/// Worker's RecordingRetentionService pulls each batch from. Only records that still hold a
/// recording (retained + a start timestamp) come back; purged / never-recorded ones don't.
/// </summary>
public class CallRecordRepositoryRetentionTests
{
    private static (CallRecordRepository repo, TenantDbContext db) NewRepo()
    {
        var db = new TenantDbContext(new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var tenantContext = new TenantContext { Current = Tenant.Create("Test", "test-tenant", "America/Chicago") };
        var factory = new Mock<ITenantDbContextFactory>();
        factory.Setup(f => f.Create(It.IsAny<string>())).Returns(db);

        return (new CallRecordRepository(new ScopedTenantDbContextFactory(tenantContext, factory.Object)), db);
    }

    private static CallRecord WithRecording(CallRecord r)
    {
        r.AppendRecordingEvent(RecordingEvent.Start(
            DateTimeOffset.UtcNow, RecordingEventSource.FlowNode, null, "/rec/x.wav"));
        return r;
    }

    private static CallRecord NewRecord() =>
        CallRecord.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CallSource.Inbound, "+15551230000");

    [Fact]
    public async Task ReturnsOnly_Retained_WithRecordingStart()
    {
        var (repo, db) = NewRepo();

        var recorded    = WithRecording(NewRecord());
        var noRecording = NewRecord();                       // never recorded
        var purged      = WithRecording(NewRecord());
        purged.MarkRecordingPurged("retention_expired");     // retained = false now

        db.CallRecords.AddRange(recorded, noRecording, purged);
        await db.SaveChangesAsync();

        var ids = await repo.FindRetainedRecordingIdsOldestFirstAsync(50);

        Assert.Equal(new[] { recorded.Id }, ids);
    }

    [Fact]
    public async Task RespectsLimit()
    {
        var (repo, db) = NewRepo();
        for (var i = 0; i < 5; i++)
            db.CallRecords.Add(WithRecording(NewRecord()));
        await db.SaveChangesAsync();

        var ids = await repo.FindRetainedRecordingIdsOldestFirstAsync(3);

        Assert.Equal(3, ids.Count);
    }

    [Fact]
    public async Task Empty_WhenNothingRecorded()
    {
        var (repo, db) = NewRepo();
        db.CallRecords.Add(NewRecord());
        await db.SaveChangesAsync();

        Assert.Empty(await repo.FindRetainedRecordingIdsOldestFirstAsync(50));
    }
}
