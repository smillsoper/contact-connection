using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Storage;
using ContactConnection.Infrastructure.Telephony.Recording;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony.Recording;

/// <summary>
/// Blob orchestration around ffmpeg: pulling + concatenating screen chunks, choosing audio-only
/// vs mux, the mux→audio-only fallback, alignment offset, and publishing the output to blob
/// storage. The ffmpeg CLI itself is faked (<see cref="FakeFfmpegRunner"/>) — no media toolchain
/// required to run these.
/// </summary>
public class FfmpegRecordingMergerTests : IDisposable
{
    private readonly string _tmpRoot;
    private readonly string _blobRoot;
    private readonly LocalFileBlobStorage _blobs;

    public FfmpegRecordingMergerTests()
    {
        _tmpRoot  = Path.Combine(Path.GetTempPath(), "cc-merge-tests", Guid.NewGuid().ToString("N"));
        _blobRoot = Path.Combine(_tmpRoot, "blobs");
        Directory.CreateDirectory(_tmpRoot);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:LocalRoot"] = _blobRoot })
            .Build();
        _blobs = new LocalFileBlobStorage(config, NullLogger<LocalFileBlobStorage>.Instance);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tmpRoot)) Directory.Delete(_tmpRoot, recursive: true); } catch { }
    }

    // ── fake ffmpeg ─────────────────────────────────────────────────────────
    private sealed class FakeFfmpegRunner : IFfmpegRunner
    {
        // args -> (exitCode, writeOutputFile?). Output path is the last arg.
        public Func<IReadOnlyList<string>, (int exit, bool writeOutput)> Behavior { get; set; } = _ => (0, true);
        public long? Duration { get; set; } = 123_000;
        public List<IReadOnlyList<string>> Calls { get; } = [];

        public Task<FfmpegRunResult> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
        {
            Calls.Add(args);
            var (exit, write) = Behavior(args);
            if (write) File.WriteAllBytes(args[^1], new byte[] { 0x00, 0x01, 0x02, 0x03 });
            return Task.FromResult(new FfmpegRunResult(exit, exit == 0 ? "" : "boom stderr"));
        }

        public Task<long?> ProbeDurationMsAsync(string path, CancellationToken ct = default) =>
            Task.FromResult(Duration);
    }

    private static bool IsMux(IReadOnlyList<string> args) => args.Contains("-map");

    // ── helpers ────────────────────────────────────────────────────────────
    private string WriteAudioFile()
    {
        var p = Path.Combine(_tmpRoot, "call.wav");
        File.WriteAllBytes(p, new byte[512]);
        return p;
    }

    private async Task<ScreenRecordingInput> PutScreenAsync(Guid callId, int chunkCount, DateTimeOffset startedAtServer)
    {
        var id  = Guid.NewGuid();
        var key = $"screen/{callId}/{id}";
        for (var i = 0; i < chunkCount; i++)
        {
            using var ms = new MemoryStream(new byte[] { (byte)i, (byte)i, (byte)i });
            await _blobs.PutAsync($"{key}/{i:D6}.webm", ms, "video/webm");
        }
        return new ScreenRecordingInput(id, key, "webm",
            Enumerable.Range(0, chunkCount).ToList(), startedAtServer, DurationMs: 60_000);
    }

    private FfmpegRecordingMerger NewMerger(IFfmpegRunner runner) =>
        new(runner, _blobs, NullLogger<FfmpegRecordingMerger>.Instance);

    // ── tests ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Merge_NoScreenRecordings_ProducesAudioOnlyM4a()
    {
        var callId = Guid.NewGuid();
        var runner = new FakeFfmpegRunner();
        var merger = NewMerger(runner);

        var result = await merger.MergeAsync(new RecordingMergeRequest(
            callId, WriteAudioFile(), DateTimeOffset.UtcNow, [], "recordings"));

        Assert.True(result.Success);
        Assert.False(result.HadVideo);
        Assert.Equal("m4a", result.OutputFormat);
        Assert.Equal($"recordings/{callId}/merged.m4a", result.OutputBlobKey);
        Assert.Null(result.ScreenRecordingId);
        Assert.Equal(123_000, result.OutputDurationMs);
        Assert.True(await _blobs.ExistsAsync(result.OutputBlobKey!));
        Assert.Single(runner.Calls);
        Assert.False(IsMux(runner.Calls[0]));
    }

    [Fact]
    public async Task Merge_WithScreenRecording_ProducesMuxedMp4_AndAlignsByServerClockDelta()
    {
        var callId = Guid.NewGuid();
        var recStart = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        var screen = await PutScreenAsync(callId, chunkCount: 3, startedAtServer: recStart.AddSeconds(5));

        var runner = new FakeFfmpegRunner();
        var result = await NewMerger(runner).MergeAsync(new RecordingMergeRequest(
            callId, WriteAudioFile(), recStart, [screen], "recordings"));

        Assert.True(result.Success);
        Assert.True(result.HadVideo);
        Assert.Equal("mp4", result.OutputFormat);
        Assert.Equal($"recordings/{callId}/merged.mp4", result.OutputBlobKey);
        Assert.Equal(screen.Id, result.ScreenRecordingId);
        Assert.Equal(1, result.ScreenRecordingCount);
        Assert.True(await _blobs.ExistsAsync(result.OutputBlobKey!));

        var muxCall = Assert.Single(runner.Calls, c => IsMux(c));
        var joined = string.Join(' ', muxCall);
        Assert.Contains("-itsoffset 5", joined);
    }

    [Fact]
    public async Task Merge_MissingAudioSource_Fails()
    {
        var result = await NewMerger(new FakeFfmpegRunner()).MergeAsync(new RecordingMergeRequest(
            Guid.NewGuid(), Path.Combine(_tmpRoot, "does-not-exist.wav"),
            DateTimeOffset.UtcNow, [], "recordings"));

        Assert.False(result.Success);
        Assert.Contains("audio source not found", result.Error);
    }

    [Fact]
    public async Task Merge_MuxFails_FallsBackToAudioOnly()
    {
        var callId = Guid.NewGuid();
        var screen = await PutScreenAsync(callId, 2, DateTimeOffset.UtcNow);

        var runner = new FakeFfmpegRunner
        {
            // fail the mux, succeed the audio-only retry
            Behavior = args => IsMux(args) ? (1, false) : (0, true),
        };

        var result = await NewMerger(runner).MergeAsync(new RecordingMergeRequest(
            callId, WriteAudioFile(), DateTimeOffset.UtcNow, [screen], "recordings"));

        Assert.True(result.Success);
        Assert.False(result.HadVideo);
        Assert.Equal("m4a", result.OutputFormat);
        Assert.Equal(2, runner.Calls.Count);           // mux attempt + audio-only fallback
        Assert.True(IsMux(runner.Calls[0]));
        Assert.False(IsMux(runner.Calls[1]));
    }

    [Fact]
    public async Task Merge_AllFfmpegRunsFail_ReturnsFailureWithStderr()
    {
        var runner = new FakeFfmpegRunner { Behavior = _ => (1, false) };

        var result = await NewMerger(runner).MergeAsync(new RecordingMergeRequest(
            Guid.NewGuid(), WriteAudioFile(), DateTimeOffset.UtcNow, [], "recordings"));

        Assert.False(result.Success);
        Assert.Contains("ffmpeg exit 1", result.Error);
        Assert.Contains("boom stderr", result.Error);
    }

    [Fact]
    public async Task Merge_ScreenChunkBlobMissing_DegradesToAudioOnly()
    {
        var callId = Guid.NewGuid();
        // Declares 3 chunks but only two exist in blob storage.
        var partial = await PutScreenAsync(callId, chunkCount: 2, DateTimeOffset.UtcNow);
        var screen = partial with { ChunkIndices = new List<int> { 0, 1, 2 } };

        var runner = new FakeFfmpegRunner();
        var result = await NewMerger(runner).MergeAsync(new RecordingMergeRequest(
            callId, WriteAudioFile(), DateTimeOffset.UtcNow, [screen], "recordings"));

        Assert.True(result.Success);
        Assert.False(result.HadVideo);
        Assert.Equal("m4a", result.OutputFormat);
        Assert.Single(runner.Calls);
        Assert.False(IsMux(runner.Calls[0]));
    }

    [Fact]
    public async Task Merge_MultipleScreenRecordings_PicksLongest_AndReportsCount()
    {
        var callId = Guid.NewGuid();
        var t0 = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

        var shortSeg = (await PutScreenAsync(callId, 2, t0)) with { DurationMs = 10_000 };
        var longSeg  = (await PutScreenAsync(callId, 2, t0.AddSeconds(30))) with { DurationMs = 90_000 };

        var runner = new FakeFfmpegRunner();
        var result = await NewMerger(runner).MergeAsync(new RecordingMergeRequest(
            callId, WriteAudioFile(), t0, [shortSeg, longSeg], "recordings"));

        Assert.True(result.HadVideo);
        Assert.Equal(longSeg.Id, result.ScreenRecordingId);
        Assert.Equal(2, result.ScreenRecordingCount);
    }
}
