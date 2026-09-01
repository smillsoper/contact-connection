using ContactConnection.Infrastructure.Telephony.Recording;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony.Recording;

/// <summary>
/// Argument construction for the two merge shapes — audio-only transcode and audio+screen mux —
/// including the A/V time-alignment (<c>-itsoffset</c> when the screen started after the audio,
/// <c>-ss</c> when it started first).
/// </summary>
public class FfmpegCommandBuilderTests
{
    [Fact]
    public void BuildAudioOnly_TranscodesToAac_WithFaststart()
    {
        var args = FfmpegCommandBuilder.BuildAudioOnly("/tmp/audio.wav", "/tmp/out.m4a");

        Assert.Equal("/tmp/audio.wav", ArgAfter(args, "-i"));
        Assert.Contains("aac", args);
        Assert.Equal("+faststart", ArgAfter(args, "-movflags"));
        Assert.Equal("/tmp/out.m4a", args[^1]);
        Assert.DoesNotContain("-map", args);   // single input, no explicit mapping needed
    }

    [Fact]
    public void BuildMux_ScreenStartedAfterAudio_DelaysVideoWithItsoffset()
    {
        // screen capture began 3.5s into the call audio
        var args = FfmpegCommandBuilder.BuildMux("/a.wav", "/s.webm", videoOffsetSeconds: 3.5, "/o.mp4");

        var joined = string.Join(' ', args);
        Assert.Contains("-itsoffset 3.5 -i /s.webm", joined);
        Assert.DoesNotContain("-ss", args);
        // audio input comes first, unshifted
        Assert.Equal("/a.wav", ArgAfter(args, "-i"));
        // explicit stream mapping: audio from input 0, video from input 1
        Assert.Contains("0:a", args);
        Assert.Contains("1:v", args);
        Assert.Contains("libx264", args);
        Assert.Equal("/o.mp4", args[^1]);
    }

    [Fact]
    public void BuildMux_ScreenStartedBeforeAudio_TrimsVideoHeadWithSs()
    {
        // screen capture began 2s before FreeSWITCH started recording
        var args = FfmpegCommandBuilder.BuildMux("/a.wav", "/s.webm", videoOffsetSeconds: -2, "/o.mp4");

        var joined = string.Join(' ', args);
        Assert.Contains("-ss 2 -i /s.webm", joined);
        Assert.DoesNotContain("-itsoffset", args);
    }

    [Fact]
    public void BuildMux_ZeroOffset_UsesItsoffsetZero_NotSs()
    {
        var args = FfmpegCommandBuilder.BuildMux("/a.wav", "/s.webm", videoOffsetSeconds: 0, "/o.mp4");

        Assert.Contains("-itsoffset", args);
        Assert.Equal("0", ArgAfter(args, "-itsoffset"));
    }

    [Fact]
    public void BuildMux_FormatsOffsetInvariantly()
    {
        var args = FfmpegCommandBuilder.BuildMux("/a.wav", "/s.webm", videoOffsetSeconds: 2.5, "/o.mp4");

        // '.' decimal separator regardless of the ambient culture
        Assert.Equal("2.5", ArgAfter(args, "-itsoffset"));
    }

    [Fact]
    public void BuildMux_KeepsFullAudio_NoShortestFlag()
    {
        var args = FfmpegCommandBuilder.BuildMux("/a.wav", "/s.webm", 1, "/o.mp4");
        Assert.DoesNotContain("-shortest", args);
    }

    private static string ArgAfter(IReadOnlyList<string> args, string flag)
    {
        var i = args.ToList().IndexOf(flag);
        Assert.True(i >= 0 && i + 1 < args.Count, $"flag {flag} not found with a following value");
        return args[i + 1];
    }
}
