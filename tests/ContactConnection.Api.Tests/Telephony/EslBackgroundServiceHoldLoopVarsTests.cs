using ContactConnection.Api.Telephony;
using ContactConnection.Application.Interfaces.Services;
using Xunit;

namespace ContactConnection.Api.Tests.Telephony;

/// <summary>
/// EslBackgroundService.ClearHoldLoopVars — the shared sweep for whatever "hold-style loop" node
/// (tf_play, its streaming-TTS sibling, tf_delay, a tf_transfer announcement) is in flight when
/// something else supersedes it: a hot-digit redirect (HandleDtmfAsync) or a real bridge
/// (HandleChannelBridgeAsync). Session 138 flagged that the bridge path only cleared _play_* and
/// left _tts_*/_delay_*/_announce_* dangling — this is the fix, verified at the pure sweep-logic
/// level (the bridge/DTMF handlers themselves need a live ESL connection to exercise).
/// </summary>
public class EslBackgroundServiceHoldLoopVarsTests
{
    [Fact]
    public void ClearsAllFourFamilies()
    {
        var session = new TelephonyCallSession
        {
            Vars = new()
            {
                ["_play_media_arg"]     = "moh.wav",
                ["_play_loop"]          = "true",
                ["_tts_in_progress"]    = "true",
                ["_tts_next_finished"]  = "n1",
                ["_delay_in_progress"]  = "true",
                ["_delay_next_node_id"] = "n2",
                ["_announce_in_progress"] = "true",
                ["_announce_node_id"]     = "n3",
            },
        };

        EslBackgroundService.ClearHoldLoopVars(session);

        Assert.Empty(session.Vars);
    }

    [Fact]
    public void LeavesUnrelatedFamilies_Untouched()
    {
        var session = new TelephonyCallSession
        {
            Vars = new()
            {
                ["_play_media_arg"]    = "moh.wav",
                ["_sc_in_progress"]    = "true",     // tf_secure_collect — synchronous capture, not a hold loop
                ["_vm_in_progress"]    = "true",     // voicemail — same
                ["_ivr_in_progress"]   = "true",     // tf_ivr_menu blocking capture — same
                ["_assigned_agent_id"] = "agent-1",  // unrelated bookkeeping
            },
        };

        EslBackgroundService.ClearHoldLoopVars(session);

        Assert.Equal(
            new[] { "_assigned_agent_id", "_ivr_in_progress", "_sc_in_progress", "_vm_in_progress" },
            session.Vars.Keys.OrderBy(k => k));
    }

    [Fact]
    public void EmptyVars_NoOp()
    {
        var session = new TelephonyCallSession();
        EslBackgroundService.ClearHoldLoopVars(session);
        Assert.Empty(session.Vars);
    }

    [Fact]
    public void NoMatchingKeys_LeavesEverythingElseAlone()
    {
        var session = new TelephonyCallSession
        {
            Vars = new() { ["_agent_uuid"] = "abc", ["_queued"] = "true" },
        };

        EslBackgroundService.ClearHoldLoopVars(session);

        Assert.Equal(2, session.Vars.Count);
    }
}
