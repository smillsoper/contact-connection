using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects;
using ContactConnection.Infrastructure.Telephony.Recording;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony.Recording;

/// <summary>S183: screen recordings start on the server's clock, and the agent's clicks / keys are drawn on the merged video.</summary>
public class ScreenInputOverlayTests
{
    private static readonly DateTimeOffset Server = new(2026, 10, 7, 17, 0, 0, TimeSpan.Zero);

    private static ScreenRecording Create(DateTimeOffset clientStart, long? offset, long? rtt) =>
        ScreenRecording.Create(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), "webm", "vp9",
            clientStart, Server, offset, rtt, 1920, 1080);

    [Fact]
    public void Synced_start_is_the_client_start_plus_the_measured_offset()
    {
        // Agent PC runs 3.2 s fast; capture began 450 ms before the start request reached the server.
        var clientStart = Server.AddMilliseconds(3200 - 450);
        var r = Create(clientStart, offset: -3200, rtt: 40);
        Assert.Equal(Server.AddMilliseconds(-450), r.StartedAtServer);
        Assert.Equal(-3200, r.ClientClockOffsetMs);
        Assert.Equal(40, r.ClockSyncRttMs);
        Assert.Equal(1920, r.VideoWidth);
    }

    [Fact]
    public void Without_a_sync_the_start_is_when_the_request_arrived()
    {
        var r = Create(Server.AddSeconds(5), offset: null, rtt: null);
        Assert.Equal(Server, r.StartedAtServer);
        Assert.Null(r.ClockSyncRttMs);
    }

    [Fact]
    public void A_slow_sync_sample_is_not_trusted()
    {
        var r = Create(Server.AddSeconds(5), offset: -5000, rtt: ScreenRecording.MaxTrustedRttMs + 1);
        Assert.Equal(Server, r.StartedAtServer);
    }

    [Fact]
    public void Click_and_key_kinds_are_kept()
    {
        var r = Create(Server, 0, 10);
        r.AddCuePoint(100, "click", "10,20");
        r.AddCuePoint(200, "key", "Enter");
        Assert.Equal(["click", "key"], r.CuePoints.Select(c => c.Kind));
    }

    private static ScreenRecordingCuePoint Cue(long at, string kind, string detail) => new() { AtMs = at, Kind = kind, Detail = detail };

    [Fact]
    public void Nothing_to_draw_gives_no_script()
    {
        Assert.Null(InputOverlayBuilder.Build([Cue(10, "bridge", "")], 1920, 1080, 0));
        Assert.Null(InputOverlayBuilder.Build([Cue(10, "click", "5,5")], 0, 0, 0));
    }

    [Fact]
    public void Clicks_are_placed_on_the_output_timeline()
    {
        var ass = InputOverlayBuilder.Build([Cue(1500, "click", "960,540")], 1920, 1080, videoOffsetSeconds: 2.0)!;
        Assert.Contains("PlayResX: 1920", ass);
        Assert.Contains("Dialogue: 1,0:00:03.50,", ass);          // 2.0 s offset + 1.5 s
        Assert.Contains("\\pos(960,540)", ass);
    }

    [Fact]
    public void Events_before_the_trimmed_head_and_off_frame_clicks_are_dropped()
    {
        var ass = InputOverlayBuilder.Build(
            [Cue(500, "click", "10,10"), Cue(3000, "click", "5000,10"), Cue(4000, "click", "20,30")], 1920, 1080, videoOffsetSeconds: -1.0)!;
        Assert.DoesNotContain("\\pos(10,10)", ass);    // at −0.5 s
        Assert.DoesNotContain("\\pos(5000,10)", ass);  // off the frame
        Assert.Contains("\\pos(20,30)", ass);
    }

    [Fact]
    public void Typing_builds_one_run_and_a_pause_starts_a_new_one()
    {
        var ass = InputOverlayBuilder.Build(
            [Cue(0, "key", "H"), Cue(200, "key", "i"), Cue(400, "key", "Enter"), Cue(5000, "key", "x")], 1920, 1080, 0)!;
        Assert.Contains("⌨ Hi [Enter]", ass);
        Assert.Contains("⌨ x", ass);
        Assert.DoesNotContain("Hi [Enter] x", ass);
    }

    [Fact]
    public void Typed_braces_cannot_inject_override_tags()
    {
        var ass = InputOverlayBuilder.Build([Cue(0, "key", "{"), Cue(10, "key", "\\")], 1920, 1080, 0)!;
        var keyLines = ass.Split('\n').Where(l => l.Contains(",Keys,")).ToList();
        Assert.All(keyLines, l => Assert.DoesNotContain("{", l));
    }

    [Fact]
    public void Ass_time_format()
    {
        Assert.Equal("0:00:00.00", InputOverlayBuilder.Ts(-1));
        Assert.Equal("1:01:01.25", InputOverlayBuilder.Ts(3661.25));
    }

    [Fact]
    public void Filter_path_escapes_the_drive_colon_and_backslashes()
    {
        Assert.Equal(@"'C\:/Temp/cc/input-overlay.ass'", FfmpegCommandBuilder.FilterPath(@"C:\Temp\cc\input-overlay.ass"));
        var args = FfmpegCommandBuilder.BuildMux("a.wav", "s.webm", 1, "o.mp4", "/tmp/x.ass");
        Assert.Contains(args, a => a.EndsWith(",ass='/tmp/x.ass'"));
    }
}
