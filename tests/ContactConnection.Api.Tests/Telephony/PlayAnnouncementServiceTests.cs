using System.Text.Json;
using ContactConnection.Api.Telephony;
using ContactConnection.Application.Interfaces.Services;
using Xunit;

namespace ContactConnection.Api.Tests.Telephony;

/// <summary>
/// Covers PlayAnnouncementService.DueAnnouncement — the pure "is a periodic hold announcement due
/// for this session, and if so which one" decision. This is what makes intermittent announcements
/// fire on a looping MOH source that never (or rarely) hits PLAYBACK_STOP.
/// </summary>
public class PlayAnnouncementServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-06T18:00:00Z");

    private static TelephonyCallSession Session(Action<Dictionary<string, string>> configure)
    {
        var s = new TelephonyCallSession { ChannelUuid = Guid.NewGuid().ToString() };
        s.Vars["_play_loop"]                 = "true";
        s.Vars["_play_state"]                = "main";
        s.Vars["_play_announcements_json"]   = JsonSerializer.Serialize(new[] { "/snd/ann1.wav", "/snd/ann2.wav" });
        s.Vars["_play_announcement_interval"] = "20";
        s.Vars["_play_announcement_index"]   = "0";
        s.Vars["_play_started_at"]           = Now.AddSeconds(-25).ToString("O");
        configure(s.Vars);
        return s;
    }

    [Fact]
    public void Due_WhenIntervalElapsedSinceStart_ReturnsIndexedEntry()
    {
        var s = Session(_ => { });
        Assert.Equal("/snd/ann1.wav", PlayAnnouncementService.DueAnnouncement(s, Now));
    }

    [Fact]
    public void NotDue_BeforeIntervalElapses()
    {
        var s = Session(v => v["_play_last_announcement_at"] = Now.AddSeconds(-5).ToString("O"));
        Assert.Null(PlayAnnouncementService.DueAnnouncement(s, Now));
    }

    [Fact]
    public void UsesLastAnnouncementTimeOverStartTime()
    {
        var s = Session(v => v["_play_last_announcement_at"] = Now.AddSeconds(-21).ToString("O"));
        Assert.Equal("/snd/ann1.wav", PlayAnnouncementService.DueAnnouncement(s, Now));
    }

    [Fact]
    public void Due_HonoursPlaylistIndex()
    {
        var s = Session(v => v["_play_announcement_index"] = "1");
        Assert.Equal("/snd/ann2.wav", PlayAnnouncementService.DueAnnouncement(s, Now));
    }

    [Fact]
    public void IndexWrapsPastEnd()
    {
        var s = Session(v => v["_play_announcement_index"] = "3"); // 2-entry list
        Assert.Equal("/snd/ann2.wav", PlayAnnouncementService.DueAnnouncement(s, Now));
    }

    [Fact]
    public void NotDue_WhenAlreadyPlayingAnAnnouncement()
    {
        var s = Session(v => v["_play_state"] = "announcement");
        Assert.Null(PlayAnnouncementService.DueAnnouncement(s, Now));
    }

    [Fact]
    public void NotDue_WhenNotLooping()
    {
        var s = Session(v => v["_play_loop"] = "false");
        Assert.Null(PlayAnnouncementService.DueAnnouncement(s, Now));
    }

    [Fact]
    public void NotDue_WhenNoAnnouncementsConfigured()
    {
        var s = Session(v => v.Remove("_play_announcements_json"));
        Assert.Null(PlayAnnouncementService.DueAnnouncement(s, Now));
    }

    [Fact]
    public void NotDue_WhenIntervalIsZeroOrUnset()
    {
        var s = Session(v => v["_play_announcement_interval"] = "0");
        Assert.Null(PlayAnnouncementService.DueAnnouncement(s, Now));
    }

    [Fact]
    public void NotDue_WhenPlaylistJsonIsMalformed()
    {
        var s = Session(v => v["_play_announcements_json"] = "{not valid");
        Assert.Null(PlayAnnouncementService.DueAnnouncement(s, Now));
    }
}
