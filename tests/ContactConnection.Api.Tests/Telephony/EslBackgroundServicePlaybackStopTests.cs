using ContactConnection.Api.Telephony;
using Xunit;

namespace ContactConnection.Api.Tests.Telephony;

/// <summary>
/// EslBackgroundService.IsRtpPrimeSilenceStop — the S133 guard that stops a tf_answer RTP-prime
/// (or tf_play leadInSilenceMs) silence_stream PLAYBACK_STOP from being misread as the tf_play
/// main media finishing, which resumed the flow early and let the next node's uuid_transfer cut
/// the real prompt off before it was audible (queue greeting vanished once an IVR menu followed it).
/// </summary>
public class EslBackgroundServicePlaybackStopTests
{
    private const string Ogg = "/usr/share/freeswitch/sounds/contactconnection/_platform/will/hold_all_agents_busy.ogg";

    [Theory]
    [InlineData("silence_stream://300,0")]
    [InlineData("silence_stream://1500,0")]
    [InlineData("SILENCE_STREAM://300,0")] // case-insensitive
    public void PrimeSilenceStop_WhileRealMediaPending_IsIgnored(string stopped) =>
        Assert.True(EslBackgroundService.IsRtpPrimeSilenceStop("main", stopped, Ogg));

    [Fact]
    public void RealMediaStop_IsNotIgnored() =>
        Assert.False(EslBackgroundService.IsRtpPrimeSilenceStop("main", Ogg, Ogg));

    [Fact]
    public void RealMediaStop_ReportedAsBareFilename_IsNotIgnored() =>
        Assert.False(EslBackgroundService.IsRtpPrimeSilenceStop("main", "hold_all_agents_busy.ogg", Ogg));

    [Fact]
    public void EmptyStoppedPath_IsNotIgnored_KeepsPriorBehavior() =>
        Assert.False(EslBackgroundService.IsRtpPrimeSilenceStop("main", "", Ogg));

    [Fact]
    public void NodeWhoseOwnMediaIsSilence_StillAdvances()
    {
        // A tf_play deliberately configured to play silence (e.g. "hold N seconds") must still
        // advance on its own stop — the guard only fires when the silence isn't the node's media.
        const string silence = "silence_stream://5000,0";
        Assert.False(EslBackgroundService.IsRtpPrimeSilenceStop("main", silence, silence));
    }

    [Fact]
    public void AnnouncementState_IsNeverShortCircuitedHere()
    {
        // The periodic-announcement branch does its own file matching — this guard must defer to it.
        Assert.False(EslBackgroundService.IsRtpPrimeSilenceStop("announcement", "silence_stream://300,0", Ogg));
    }

    [Fact]
    public void NonSilenceMismatch_IsNotIgnoredHere()
    {
        // A different real file stopping is out of scope for this narrow guard (kept minimal to
        // avoid hanging a flite-TTS resolved temp path that legitimately won't string-match).
        Assert.False(EslBackgroundService.IsRtpPrimeSilenceStop("main", "/tmp/some-other-prompt.wav", Ogg));
    }
}
