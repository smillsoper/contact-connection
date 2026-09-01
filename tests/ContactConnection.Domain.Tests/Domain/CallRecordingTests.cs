using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects;
using Xunit;

namespace ContactConnection.Domain.Tests.Domain;

/// <summary>
/// Step 1 of the call-recording build: the domain surface — Campaign recording policy,
/// the RecordingEvent value object, and CallRecord's recording-lifecycle aggregation
/// (recording_started_at / recording_stopped_at / recording_masked_seconds derived from
/// the event list). See DevLog "call recording" work / ARCHITECTURE.md §13.
/// </summary>
public class CallRecordingTests
{
    private static CallRecord NewCallRecord() =>
        CallRecord.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CallSource.Inbound, "+15551234567");

    private static Campaign NewCampaign() =>
        Campaign.Create(Guid.NewGuid(), Guid.NewGuid(), "Test Campaign", "test-campaign");

    private static readonly DateTimeOffset T0 = new(2026, 8, 30, 17, 0, 0, TimeSpan.Zero);

    // ── Campaign recording policy ────────────────────────────────────────────

    [Fact]
    public void Campaign_Create_HasSafeRecordingDefaults()
    {
        var c = NewCampaign();
        Assert.Equal(RecordingMode.Disabled, c.RecordingMode);
        Assert.Equal(ConsentModel.OneParty, c.ConsentModel);
        Assert.False(c.RecordingRequired);
        Assert.True(c.RecordStereo);
        Assert.False(c.RecordingBeepEnabled);
        Assert.False(c.AutoMaskOnHold);
        Assert.Equal(90, c.RecordingRetentionDays);
    }

    [Fact]
    public void ConfigureRecording_ValidValues_AreApplied()
    {
        var c = NewCampaign();
        c.ConfigureRecording(
            RecordingMode.Conversation, ConsentModel.TwoPartyAnnounceOptout,
            recordingRequired: true, recordStereo: false, recordingBeepEnabled: true,
            autoMaskOnHold: true, recordingRetentionDays: 365);

        Assert.Equal(RecordingMode.Conversation, c.RecordingMode);
        Assert.Equal(ConsentModel.TwoPartyAnnounceOptout, c.ConsentModel);
        Assert.True(c.RecordingRequired);
        Assert.False(c.RecordStereo);
        Assert.True(c.RecordingBeepEnabled);
        Assert.True(c.AutoMaskOnHold);
        Assert.Equal(365, c.RecordingRetentionDays);
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("")]
    [InlineData("FULL")]
    public void ConfigureRecording_InvalidMode_FallsBackToDisabled(string mode)
    {
        var c = NewCampaign();
        c.ConfigureRecording(mode, ConsentModel.OneParty, false, true, false, false, 90);
        Assert.Equal(RecordingMode.Disabled, c.RecordingMode);
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("")]
    public void ConfigureRecording_InvalidConsentModel_FallsBackToOneParty(string consent)
    {
        var c = NewCampaign();
        c.ConfigureRecording(RecordingMode.Full, consent, false, true, false, false, 90);
        Assert.Equal(ConsentModel.OneParty, c.ConsentModel);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(5000, 3650)]
    [InlineData(90, 90)]
    public void ConfigureRecording_RetentionDays_IsClamped(int input, int expected)
    {
        var c = NewCampaign();
        c.ConfigureRecording(RecordingMode.Full, ConsentModel.OneParty, false, true, false, false, input);
        Assert.Equal(expected, c.RecordingRetentionDays);
    }

    [Theory]
    [InlineData(RecordingMode.Full, true)]
    [InlineData(RecordingMode.RecordAlwaysRetainByDisposition, true)]
    [InlineData(RecordingMode.Conversation, false)]
    [InlineData(RecordingMode.Disabled, false)]
    public void RecordingMode_AllowsPreBridge(string mode, bool expected)
        => Assert.Equal(expected, RecordingMode.AllowsPreBridge(mode));

    [Theory]
    [InlineData(ConsentModel.OneParty, false)]
    [InlineData(ConsentModel.TwoPartyAnnounce, true)]
    [InlineData(ConsentModel.TwoPartyAnnounceOptout, true)]
    public void ConsentModel_RequiresAnnouncement(string model, bool expected)
        => Assert.Equal(expected, ConsentModel.RequiresAnnouncement(model));

    // ── RecordingEvent value object ─────────────────────────────────────────

    [Fact]
    public void RecordingEvent_Factories_SetActionAndFields()
    {
        var start = RecordingEvent.Start(T0, RecordingEventSource.FlowNode, "tf_record_1", "/rec/call.wav");
        Assert.Equal(RecordingEventAction.Start, start.Action);
        Assert.Equal("/rec/call.wav", start.RecordingPath);
        Assert.Equal("tf_record_1", start.NodeId);

        var mask = RecordingEvent.Mask(T0, RecordingEventSource.FieldFocus, MaskFillKind.Tone, reason: "pan", frameUrl: "https://pay.example/card");
        Assert.Equal(RecordingEventAction.Mask, mask.Action);
        Assert.Equal(MaskFillKind.Tone, mask.MaskFill);
        Assert.Equal("https://pay.example/card", mask.FrameUrl);

        var unmask = RecordingEvent.Unmask(T0, RecordingEventSource.Watchdog);
        Assert.Equal(RecordingEventAction.Unmask, unmask.Action);

        var stop = RecordingEvent.Stop(T0, RecordingEventSource.Disconnect);
        Assert.Equal(RecordingEventAction.Stop, stop.Action);
    }

    [Fact]
    public void RecordingEvent_Mask_InvalidFill_FallsBackToSilence()
    {
        var mask = RecordingEvent.Mask(T0, RecordingEventSource.Manual, "rainbows");
        Assert.Equal(MaskFillKind.Silence, mask.MaskFill);
    }

    [Theory]
    [InlineData("start", true)]
    [InlineData("mask", true)]
    [InlineData("pause", false)]
    public void RecordingEventAction_IsValid(string value, bool expected)
        => Assert.Equal(expected, RecordingEventAction.IsValid(value));

    // ── CallRecord recording aggregation ───────────────────────────────────

    [Fact]
    public void AppendRecordingEvent_StartAndStop_SetTimestamps()
    {
        var r = NewCallRecord();
        r.AppendRecordingEvent(RecordingEvent.Start(T0, RecordingEventSource.FlowNode, "n1", "/x.wav"));
        r.AppendRecordingEvent(RecordingEvent.Stop(T0.AddMinutes(5), RecordingEventSource.Disconnect));

        Assert.Equal(T0, r.RecordingStartedAt);
        Assert.Equal(T0.AddMinutes(5), r.RecordingStoppedAt);
        Assert.Equal(0, r.RecordingMaskedSeconds);
        Assert.Equal(2, r.RecordingEvents.Count);
    }

    [Fact]
    public void AppendRecordingEvent_OutOfOrder_AggregatesFromSortedList()
    {
        var r = NewCallRecord();
        // stop arrives before start (independent ESL callbacks)
        r.AppendRecordingEvent(RecordingEvent.Stop(T0.AddMinutes(3), RecordingEventSource.Disconnect));
        r.AppendRecordingEvent(RecordingEvent.Start(T0, RecordingEventSource.FlowNode, "n1", "/x.wav"));

        Assert.Equal(T0, r.RecordingStartedAt);
        Assert.Equal(T0.AddMinutes(3), r.RecordingStoppedAt);
    }

    [Fact]
    public void AppendRecordingEvent_SingleMaskWindow_CountsMaskedSeconds()
    {
        var r = NewCallRecord();
        r.AppendRecordingEvent(RecordingEvent.Start(T0, RecordingEventSource.FlowNode, "n1", "/x.wav"));
        r.AppendRecordingEvent(RecordingEvent.Mask(T0.AddSeconds(30), RecordingEventSource.CustomEvent, MaskFillKind.Silence));
        r.AppendRecordingEvent(RecordingEvent.Unmask(T0.AddSeconds(75), RecordingEventSource.CustomEvent));
        r.AppendRecordingEvent(RecordingEvent.Stop(T0.AddSeconds(120), RecordingEventSource.Disconnect));

        Assert.Equal(45, r.RecordingMaskedSeconds);
    }

    [Fact]
    public void AppendRecordingEvent_NestedMasks_CollapseToOutermostWindow()
    {
        var r = NewCallRecord();
        r.AppendRecordingEvent(RecordingEvent.Start(T0, RecordingEventSource.FlowNode, "n1", "/x.wav"));
        // extension field-focus mask starts, then the flow custom-event masks again before either unmasks
        r.AppendRecordingEvent(RecordingEvent.Mask(T0.AddSeconds(10), RecordingEventSource.FieldFocus, MaskFillKind.Silence));
        r.AppendRecordingEvent(RecordingEvent.Mask(T0.AddSeconds(15), RecordingEventSource.CustomEvent, MaskFillKind.Silence));
        r.AppendRecordingEvent(RecordingEvent.Unmask(T0.AddSeconds(20), RecordingEventSource.FieldFocus));
        r.AppendRecordingEvent(RecordingEvent.Unmask(T0.AddSeconds(40), RecordingEventSource.CustomEvent));
        r.AppendRecordingEvent(RecordingEvent.Stop(T0.AddSeconds(60), RecordingEventSource.Disconnect));

        // one continuous masked window from t+10 to t+40 = 30s (not double-counted)
        Assert.Equal(30, r.RecordingMaskedSeconds);
    }

    [Fact]
    public void AppendRecordingEvent_MaskLeftOpen_ClosesAtStop()
    {
        var r = NewCallRecord();
        r.AppendRecordingEvent(RecordingEvent.Start(T0, RecordingEventSource.FlowNode, "n1", "/x.wav"));
        r.AppendRecordingEvent(RecordingEvent.Mask(T0.AddSeconds(30), RecordingEventSource.Manual, MaskFillKind.Silence));
        r.AppendRecordingEvent(RecordingEvent.Stop(T0.AddSeconds(90), RecordingEventSource.Disconnect));

        Assert.Equal(60, r.RecordingMaskedSeconds);
    }

    [Fact]
    public void AppendRecordingEvent_MaskLeftOpen_NoStop_ClosesAtLastEvent()
    {
        var r = NewCallRecord();
        r.AppendRecordingEvent(RecordingEvent.Start(T0, RecordingEventSource.FlowNode, "n1", "/x.wav"));
        r.AppendRecordingEvent(RecordingEvent.Mask(T0.AddSeconds(30), RecordingEventSource.Manual, MaskFillKind.Silence));
        // some later non-stop event (e.g. a stray unmask that under-pairs is still the last timestamp)
        r.AppendRecordingEvent(RecordingEvent.Mask(T0.AddSeconds(50), RecordingEventSource.Manual, MaskFillKind.Silence));

        // window opened at t+30, still open, last event t+50 => 20s
        Assert.Equal(20, r.RecordingMaskedSeconds);
    }

    [Fact]
    public void AppendRecordingEvent_ExtraUnmask_IsIgnored()
    {
        var r = NewCallRecord();
        r.AppendRecordingEvent(RecordingEvent.Start(T0, RecordingEventSource.FlowNode, "n1", "/x.wav"));
        r.AppendRecordingEvent(RecordingEvent.Unmask(T0.AddSeconds(10), RecordingEventSource.Manual));
        r.AppendRecordingEvent(RecordingEvent.Mask(T0.AddSeconds(20), RecordingEventSource.Manual, MaskFillKind.Silence));
        r.AppendRecordingEvent(RecordingEvent.Unmask(T0.AddSeconds(35), RecordingEventSource.Manual));
        r.AppendRecordingEvent(RecordingEvent.Stop(T0.AddSeconds(60), RecordingEventSource.Disconnect));

        Assert.Equal(15, r.RecordingMaskedSeconds);
    }

    [Fact]
    public void MarkRecordingPurged_ClearsUrlAndStampsReason()
    {
        var r = NewCallRecord();
        r.SetRecording("https://blob.example/rec/call.mp3");
        r.AppendRecordingEvent(RecordingEvent.Start(T0, RecordingEventSource.FlowNode, "n1", "/x.wav"));

        r.MarkRecordingPurged("retention_expired");

        Assert.False(r.RecordingRetained);
        Assert.Equal("retention_expired", r.RecordingDeleteReason);
        Assert.NotNull(r.RecordingDeletedAt);
        Assert.Null(r.RecordingUrl);
        // the event trail is preserved for audit even after the media is gone
        Assert.Single(r.RecordingEvents);
    }

    [Fact]
    public void NewCallRecord_RecordingRetained_DefaultsTrue()
        => Assert.True(NewCallRecord().RecordingRetained);
}
