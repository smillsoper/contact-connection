using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects.Exports;
using Xunit;

namespace ContactConnection.Domain.Tests.Exports;

public class ExportLifecycleTests
{
    private static ExportDefinition New() => ExportDefinition.Create(Guid.NewGuid(), "Cannella SF", null, new ExportSpec());

    [Fact]
    public void FullLifecycle_DraftToLive_RecordsTheApproval()
    {
        var d = New();
        var run = Guid.NewGuid();
        d.StartTesting();
        d.Approve("Jane at Cannella", "Stephen Soper", "Looks good", run);
        Assert.Equal(ExportStatus.Approved, d.Status);
        Assert.Equal(run, d.ApprovedRunId);
        Assert.Equal(1, d.ApprovedSpecRevision);
        d.GoLive();
        d.Pause();
        d.GoLive();
        Assert.Equal(ExportStatus.Live, d.Status);
    }

    [Fact]
    public void CannotApproveWithoutTesting_OrGoLiveUnapproved()
    {
        var d = New();
        Assert.Throws<InvalidOperationException>(() => d.Approve("x", "y", null, Guid.NewGuid()));
        Assert.Throws<InvalidOperationException>(() => d.GoLive());
    }

    [Fact]
    public void SpecChangeAfterApproval_IsFlagged_NotSilentlyDropped()
    {
        var d = New();
        d.StartTesting();
        d.Approve("x", "y", null, Guid.NewGuid());
        d.GoLive();
        Assert.False(d.UpdateSpec(new ExportSpec()));   // identical spec — no new revision
        Assert.True(d.UpdateSpec(new ExportSpec { Delimiter = "|" }));
        Assert.True(d.ChangedSinceApproval);
        Assert.Equal(ExportStatus.Live, d.Status);
    }

    [Fact]
    public void Run_PracticeCallsOnlyInTestFiles_AndPermanentFailureStops()
    {
        var d = New();
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentException>(() =>
            ExportRun.Queue(d, ExportRunKind.Manual, false, ExportDataSource.Practice, now.AddDays(-1), now, null, null));
        var run = ExportRun.Queue(d, ExportRunKind.Test, true, ExportDataSource.Practice, now.AddDays(-1), now, null, null);
        run.Fail("template error", TimeSpan.FromMinutes(1), permanent: true);
        Assert.Equal(ExportRunStatus.Failed, run.Status);
        var retry = ExportRun.Queue(d, ExportRunKind.Manual, false, ExportDataSource.Production, now.AddDays(-1), now, null, null);
        retry.Fail("db down", TimeSpan.FromMinutes(1));
        Assert.Equal(ExportRunStatus.Queued, retry.Status);
    }
}
