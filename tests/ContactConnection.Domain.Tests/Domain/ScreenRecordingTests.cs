using ContactConnection.Domain.Entities;
using Xunit;

namespace ContactConnection.Domain.Tests.Domain;

/// <summary>
/// Step 4 of the call-recording build: ScreenRecording — client↔server clock offset,
/// resumable-chunk bookkeeping, cue points, and the contiguity guard on completion.
/// </summary>
public class ScreenRecordingTests
{
    private static readonly DateTimeOffset Server = new(2026, 8, 30, 19, 0, 5, TimeSpan.Zero);

    private static ScreenRecording New(DateTimeOffset? clientStart = null) =>
        ScreenRecording.Create(
            tenantId: Guid.NewGuid(), callRecordId: Guid.NewGuid(), interactionId: null, agentId: Guid.NewGuid(),
            container: "webm", codec: "vp9,opus",
            startedAtClient: clientStart ?? Server.AddMilliseconds(-1200),
            serverNow: Server);

    [Fact]
    public void Create_ComputesClockOffset_AndDerivesStorageKey()
    {
        var r = New(Server.AddMilliseconds(-1500));
        Assert.Equal(1500, r.ClientClockOffsetMs);
        Assert.Equal(Server, r.StartedAtServer);
        Assert.Equal(ScreenRecordingStatus.Recording, r.Status);
        Assert.Equal($"screen/{r.CallRecordId}/{r.Id}", r.StorageKey);
        Assert.Equal(0, r.ChunkCount);
    }

    [Fact]
    public void Create_ClientClockAhead_YieldsNegativeOffset()
    {
        var r = New(Server.AddMilliseconds(400));   // agent clock is 400ms ahead of server
        Assert.Equal(-400, r.ClientClockOffsetMs);
    }

    [Fact]
    public void RegisterChunk_TracksIndicesSortedAndBytes_AndMovesToUploading()
    {
        var r = New();
        r.RegisterChunk(2, 1000);
        r.RegisterChunk(0, 500);
        r.RegisterChunk(1, 750);

        Assert.Equal(new[] { 0, 1, 2 }, r.ReceivedChunkIndices);
        Assert.Equal(3, r.ChunkCount);
        Assert.Equal(2250, r.TotalBytes);
        Assert.Equal(ScreenRecordingStatus.Uploading, r.Status);
    }

    [Fact]
    public void RegisterChunk_ReUpload_IsIdempotent_DoesNotDoubleCountBytes()
    {
        var r = New();
        r.RegisterChunk(0, 500);
        r.RegisterChunk(0, 900);   // retry of the same chunk

        Assert.Equal(new[] { 0 }, r.ReceivedChunkIndices);
        Assert.Equal(500, r.TotalBytes);
    }

    [Fact]
    public void RegisterChunk_AfterComplete_Throws()
    {
        var r = New();
        r.RegisterChunk(0, 10);
        r.MarkComplete(1000, null);
        Assert.Throws<InvalidOperationException>(() => r.RegisterChunk(1, 10));
    }

    [Fact]
    public void MarkComplete_ContiguousChunks_Completes()
    {
        var r = New();
        r.RegisterChunk(0, 10);
        r.RegisterChunk(1, 10);
        r.RegisterChunk(2, 10);

        r.MarkComplete(4321, "abc123");

        Assert.Equal(ScreenRecordingStatus.Complete, r.Status);
        Assert.Equal(4321, r.DurationMs);
        Assert.Equal("abc123", r.Sha256);
        Assert.NotNull(r.CompletedAt);
    }

    [Fact]
    public void MarkComplete_GapInChunks_Throws()
    {
        var r = New();
        r.RegisterChunk(0, 10);
        r.RegisterChunk(2, 10);   // missing 1

        var ex = Assert.Throws<InvalidOperationException>(() => r.MarkComplete(1000, null));
        Assert.Contains("missing index 1", ex.Message);
        Assert.NotEqual(ScreenRecordingStatus.Complete, r.Status);
    }

    [Fact]
    public void MarkComplete_NoChunks_Completes()
    {
        // zero-length capture (agent answered and immediately hung up) — no gap, allowed
        var r = New();
        r.MarkComplete(0, null);
        Assert.Equal(ScreenRecordingStatus.Complete, r.Status);
        Assert.Null(r.DurationMs);
    }

    [Fact]
    public void AddCuePoint_AppendsAndCoercesUnknownKind()
    {
        var r = New();
        r.AddCuePoint(1200, ScreenRecordingCuePointKind.Bridge, "agent 1000");
        r.AddCuePoint(5400, "rubbish");

        Assert.Equal(2, r.CuePoints.Count);
        Assert.Equal(ScreenRecordingCuePointKind.Bridge, r.CuePoints[0].Kind);
        Assert.Equal("agent 1000", r.CuePoints[0].Detail);
        Assert.Equal(ScreenRecordingCuePointKind.Custom, r.CuePoints[1].Kind);
    }

    [Fact]
    public void Abort_ThenComplete_Throws()
    {
        var r = New();
        r.Abort();
        Assert.Equal(ScreenRecordingStatus.Aborted, r.Status);
        Assert.Throws<InvalidOperationException>(() => r.MarkComplete(1000, null));
    }

    [Fact]
    public void MarkFailed_RecordsReason()
    {
        var r = New();
        r.MarkFailed("browser crashed");
        Assert.Equal(ScreenRecordingStatus.Failed, r.Status);
        Assert.Equal("browser crashed", r.FailureReason);
    }
}
