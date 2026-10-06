using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Storage;
using ContactConnection.Infrastructure.Telephony.Recording;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony.Recording;

/// <summary>"Keep conversation only" trimming (S181) — orchestration with a fake ffmpeg; real-ffmpeg behaviour is checked
/// separately against the dev toolchain.</summary>
public class RecordingTrimmerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cc-trim-tests", Guid.NewGuid().ToString("N"));
    private readonly IBlobStorage _blobs;

    public RecordingTrimmerTests()
    {
        Directory.CreateDirectory(_root);
        _blobs = new LocalFileBlobStorage(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:LocalRoot"] = Path.Combine(_root, "blobs") }).Build(),
            NullLogger<LocalFileBlobStorage>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private sealed class FakeRunner(int exit = 0) : IFfmpegRunner
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];
        public Task<FfmpegRunResult> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
        {
            Calls.Add(args);
            if (exit == 0) File.WriteAllBytes(args[^1], [9, 9, 9]);
            return Task.FromResult(new FfmpegRunResult(exit, ""));
        }
        public Task<long?> ProbeDurationMsAsync(string path, CancellationToken ct = default) => Task.FromResult<long?>(null);
    }

    [Fact]
    public async Task TrimsTheRawWavAndTheMergedFile_FromTheConnectPoint()
    {
        var raw = Path.Combine(_root, "call.wav");
        await File.WriteAllBytesAsync(raw, new byte[64]);
        await _blobs.PutAsync("recordings/c1/merged.m4a", new MemoryStream(new byte[64]), "audio/mp4");
        var runner = new FakeRunner();

        var error = await new RecordingTrimmer(runner, _blobs, NullLogger<RecordingTrimmer>.Instance)
            .TrimAsync(raw, "recordings/c1/merged.m4a", 42.5);

        Assert.Null(error);
        Assert.Equal(2, runner.Calls.Count);
        Assert.Equal(["-y", "-ss", "42.5"], runner.Calls[0].Take(3));
        Assert.Contains("copy", runner.Calls[0]);               // raw PCM: stream copy
        Assert.Contains("aac", runner.Calls[1]);                // merged: re-encoded from the cut
        Assert.Equal([9, 9, 9], await File.ReadAllBytesAsync(raw));
        await using var merged = await _blobs.OpenReadAsync("recordings/c1/merged.m4a");
        using var ms = new MemoryStream();
        await merged!.CopyToAsync(ms);
        Assert.Equal([9, 9, 9], ms.ToArray());
    }

    [Fact]
    public async Task NothingBeforeTheConversation_NoWork_AndFailuresReported()
    {
        var runner = new FakeRunner();
        Assert.Null(await new RecordingTrimmer(runner, _blobs, NullLogger<RecordingTrimmer>.Instance).TrimAsync(null, null, 0));
        Assert.Empty(runner.Calls);

        var raw = Path.Combine(_root, "call2.wav");
        await File.WriteAllBytesAsync(raw, new byte[8]);
        var error = await new RecordingTrimmer(new FakeRunner(exit: 1), _blobs, NullLogger<RecordingTrimmer>.Instance).TrimAsync(raw, null, 5);
        Assert.Contains("raw audio", error);
        Assert.Equal(new byte[8], await File.ReadAllBytesAsync(raw));   // untouched on failure
    }
}
