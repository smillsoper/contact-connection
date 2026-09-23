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

/// <summary>
/// EslBackgroundService.IsStaleFileStop — the Session 154 guard for a hot-digit redirect's
/// uuid_break generating its own PLAYBACK_STOP for the audio it just interrupted, which can arrive
/// after a NEW tf_play has already overwritten the same flat _play_* vars and get misread as that
/// new node's own completion.
/// </summary>
public class EslBackgroundServiceStaleFileStopTests
{
    private const string Ogg = "/usr/share/freeswitch/sounds/contactconnection/_platform/will/hold_all_agents_busy.ogg";
    private const string OtherOgg = "/usr/share/freeswitch/sounds/contactconnection/_platform/will/callback_confirmation.ogg";

    [Fact]
    public void MismatchedFile_FromASupersededPlay_IsStale()
    {
        // The exact reproduction: the OLD (interrupted) hold announcement's late stop arrives after
        // the NEW tf_play (a different file) has already taken over the same session vars.
        Assert.True(EslBackgroundService.IsStaleFileStop("file", Ogg, OtherOgg));
    }

    [Fact]
    public void MatchingFile_GenuineCompletion_IsNotStale() =>
        Assert.False(EslBackgroundService.IsStaleFileStop("file", Ogg, Ogg));

    [Fact]
    public void MatchingFile_ReportedAsBareFilename_IsNotStale() =>
        Assert.False(EslBackgroundService.IsStaleFileStop("file", "hold_all_agents_busy.ogg", Ogg));

    [Fact]
    public void EmptyStoppedPath_IsNotStale_KeepsPriorBehavior()
    {
        // Some media types don't report a Playback-File-Path at all — falls through unchanged
        // rather than being (incorrectly) treated as either stale or confirmed.
        Assert.False(EslBackgroundService.IsStaleFileStop("file", "", OtherOgg));
    }

    [Fact]
    public void TtsAudioSource_NeverFlaggedStale_EvenOnMismatch()
    {
        // A flite tf_play's mediaArg is a synthetic "tts://flite|voice|..." string that FreeSWITCH
        // will never echo back verbatim as Playback-File-Path — comparing here would false-reject
        // genuine flite completions, so the check is scoped away from "tts" entirely.
        Assert.False(EslBackgroundService.IsStaleFileStop("tts", "/tmp/flite-synth-12345.wav", "tts://flite|kal|hello"));
    }
}
