using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.ValueObjects;
using ContactConnection.Infrastructure.Telephony;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony;

/// <summary>
/// Step 2 of the call-recording build: CallRecordingController — the chokepoint that issues
/// uuid_record, stamps a server-time RecordingEvent, and runs the auto-unmask watchdog.
/// </summary>
public class CallRecordingControllerTests
{
    private const string Schema = "tenant_test_tenant";
    private static readonly Guid CallRecordId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string Uuid = "chan-uuid-1";
    private const string ExpectedPath = "/var/lib/freeswitch/recordings/11111111-1111-1111-1111-111111111111.wav";

    private sealed class Harness
    {
        public Mock<IEslCommander> Esl { get; } = new(MockBehavior.Loose);
        public Mock<IEslCommanderFactory> Factory { get; } = new();
        public Mock<IOwnedEslCommander> OwnedEsl { get; } = new();
        public List<RecordingEvent> Persisted { get; } = [];
        public Mock<ICallRecordingRepository> Repo { get; } = new();
        public CallRecordingController Controller { get; }

        public Harness(int maxMaskSeconds = 180)
        {
            Factory.Setup(f => f.CreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(OwnedEsl.Object);
            OwnedEsl.Setup(o => o.DisposeAsync()).Returns(ValueTask.CompletedTask);

            Repo.Setup(r => r.AppendEventAsync(Schema, CallRecordId, It.IsAny<RecordingEvent>(), It.IsAny<CancellationToken>()))
                .Callback<string, Guid, RecordingEvent, CancellationToken>((_, _, e, _) => Persisted.Add(e))
                .ReturnsAsync(true);

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["FreeSWITCH:RecordingsContainerPath"] = "/var/lib/freeswitch/recordings",
                    ["Recording:MaxMaskSeconds"] = maxMaskSeconds.ToString(),
                })
                .Build();

            Controller = new CallRecordingController(
                Factory.Object, Repo.Object, config, NullLogger<CallRecordingController>.Instance);
        }
    }

    private static RecordingCommand Cmd(string source = RecordingEventSource.FlowNode, string? nodeId = "tf_record_1") => new()
    {
        ChannelUuid = Uuid, CallRecordId = CallRecordId, TenantSchemaName = Schema, Source = source, NodeId = nodeId,
    };

    // ── start ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task StartAsync_Stereo_SetsRecordStereoThenIssuesStart_AndPersistsEvent()
    {
        var h = new Harness();

        var outcome = await h.Controller.StartAsync(Cmd(), new RecordingStartOptions { Stereo = true }, h.Esl.Object);

        Assert.True(outcome.Ok);
        h.Esl.Verify(e => e.SetChannelVarAsync(Uuid, "RECORD_STEREO", "true", It.IsAny<CancellationToken>()), Times.Once);
        h.Esl.Verify(e => e.RecordAsync(Uuid, RecordingEventAction.Start, ExpectedPath, 0, It.IsAny<CancellationToken>()), Times.Once);

        var evt = Assert.Single(h.Persisted);
        Assert.Equal(RecordingEventAction.Start, evt.Action);
        Assert.Equal(ExpectedPath, evt.RecordingPath);
        Assert.Equal(RecordingEventSource.FlowNode, evt.Source);
        Assert.Equal("tf_record_1", evt.NodeId);
        Assert.NotEqual(default, evt.At);
    }

    [Fact]
    public async Task StartAsync_NonStereo_DoesNotSetRecordStereo()
    {
        var h = new Harness();
        await h.Controller.StartAsync(Cmd(), new RecordingStartOptions { Stereo = false }, h.Esl.Object);
        h.Esl.Verify(e => e.SetChannelVarAsync(It.IsAny<string>(), "RECORD_STEREO", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartAsync_WithLimit_PassesLimitToUuidRecord()
    {
        var h = new Harness();
        await h.Controller.StartAsync(Cmd(), new RecordingStartOptions { Stereo = false, LimitSeconds = 3600 }, h.Esl.Object);
        h.Esl.Verify(e => e.RecordAsync(Uuid, RecordingEventAction.Start, ExpectedPath, 3600, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_NullEsl_OpensAndDisposesOwnedConnection()
    {
        var h = new Harness();

        await h.Controller.StartAsync(Cmd(), new RecordingStartOptions { Stereo = false }, esl: null);

        h.Factory.Verify(f => f.CreateAsync(It.IsAny<CancellationToken>()), Times.Once);
        h.OwnedEsl.Verify(o => o.RecordAsync(Uuid, RecordingEventAction.Start, ExpectedPath, 0, It.IsAny<CancellationToken>()), Times.Once);
        h.OwnedEsl.Verify(o => o.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task StartAsync_PassedEsl_DoesNotUseFactory()
    {
        var h = new Harness();
        await h.Controller.StartAsync(Cmd(), new RecordingStartOptions(), h.Esl.Object);
        h.Factory.Verify(f => f.CreateAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── mask / unmask / stop ───────────────────────────────────────────────

    [Fact]
    public async Task MaskAsync_IssuesMask_AndPersistsMaskEventWithFill()
    {
        var h = new Harness();

        await h.Controller.MaskAsync(new RecordingMaskCommand
        {
            ChannelUuid = Uuid, CallRecordId = CallRecordId, TenantSchemaName = Schema,
            Source = RecordingEventSource.FieldFocus, MaskFill = MaskFillKind.Tone,
            Reason = "pan", FrameUrl = "https://pay.example/card",
        }, h.Esl.Object);

        h.Esl.Verify(e => e.RecordAsync(Uuid, RecordingEventAction.Mask, ExpectedPath, 0, It.IsAny<CancellationToken>()), Times.Once);
        var evt = Assert.Single(h.Persisted);
        Assert.Equal(RecordingEventAction.Mask, evt.Action);
        Assert.Equal(MaskFillKind.Tone, evt.MaskFill);
        Assert.Equal("pan", evt.Reason);
        Assert.Equal("https://pay.example/card", evt.FrameUrl);
    }

    [Fact]
    public async Task MaskAsync_InvalidFill_CoercedToSilence()
    {
        var h = new Harness();
        await h.Controller.MaskAsync(new RecordingMaskCommand
        {
            ChannelUuid = Uuid, CallRecordId = CallRecordId, TenantSchemaName = Schema,
            Source = RecordingEventSource.Manual, MaskFill = "rainbows",
        }, h.Esl.Object);
        Assert.Equal(MaskFillKind.Silence, Assert.Single(h.Persisted).MaskFill);
    }

    [Fact]
    public async Task StopAsync_IssuesStop_AndPersistsStopEvent()
    {
        var h = new Harness();
        await h.Controller.StopAsync(Cmd(source: RecordingEventSource.Disconnect, nodeId: null), h.Esl.Object);
        h.Esl.Verify(e => e.RecordAsync(Uuid, RecordingEventAction.Stop, ExpectedPath, 0, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(RecordingEventAction.Stop, Assert.Single(h.Persisted).Action);
    }

    [Fact]
    public async Task EslThrows_OutcomeIsFailure()
    {
        var h = new Harness();
        h.Esl.Setup(e => e.RecordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("channel gone"));

        var outcome = await h.Controller.StartAsync(Cmd(), new RecordingStartOptions { Stereo = false }, h.Esl.Object);

        Assert.False(outcome.Ok);
        Assert.Contains("channel gone", outcome.Error);
        Assert.Empty(h.Persisted);
    }

    // ── watchdog ───────────────────────────────────────────────────────────

    [Fact]
    public async Task MaskWatchdog_Fires_ForcesUnmaskViaOwnConnection()
    {
        var h = new Harness();

        await h.Controller.MaskAsync(new RecordingMaskCommand
        {
            ChannelUuid = Uuid, CallRecordId = CallRecordId, TenantSchemaName = Schema,
            Source = RecordingEventSource.CustomEvent, MaxMaskSeconds = 1,
        }, h.Esl.Object);

        // wait past the 1s watchdog
        await WaitForAsync(() => h.Persisted.Any(e => e.Action == RecordingEventAction.Unmask), TimeSpan.FromSeconds(4));

        var unmask = Assert.Single(h.Persisted, e => e.Action == RecordingEventAction.Unmask);
        Assert.Equal(RecordingEventSource.Watchdog, unmask.Source);
        // the deferred unmask had no ESL passed → it opened its own
        h.Factory.Verify(f => f.CreateAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        h.OwnedEsl.Verify(o => o.RecordAsync(Uuid, RecordingEventAction.Unmask, ExpectedPath, 0, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExplicitUnmask_CancelsWatchdog_NoForcedUnmask()
    {
        var h = new Harness();

        await h.Controller.MaskAsync(new RecordingMaskCommand
        {
            ChannelUuid = Uuid, CallRecordId = CallRecordId, TenantSchemaName = Schema,
            Source = RecordingEventSource.CustomEvent, MaxMaskSeconds = 1,
        }, h.Esl.Object);
        await h.Controller.UnmaskAsync(Cmd(source: RecordingEventSource.CustomEvent), h.Esl.Object);

        await Task.Delay(TimeSpan.FromSeconds(2)); // let the (cancelled) watchdog window elapse

        h.Esl.Verify(e => e.RecordAsync(Uuid, RecordingEventAction.Unmask, ExpectedPath, 0, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Single(h.Persisted, e => e.Action == RecordingEventAction.Unmask);
        h.Factory.Verify(f => f.CreateAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ForgetChannel_CancelsPendingWatchdog()
    {
        var h = new Harness();

        await h.Controller.MaskAsync(new RecordingMaskCommand
        {
            ChannelUuid = Uuid, CallRecordId = CallRecordId, TenantSchemaName = Schema,
            Source = RecordingEventSource.CustomEvent, MaxMaskSeconds = 1,
        }, h.Esl.Object);
        h.Controller.ForgetChannel(Uuid);

        await Task.Delay(TimeSpan.FromSeconds(2));

        Assert.DoesNotContain(h.Persisted, e => e.Action == RecordingEventAction.Unmask);
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(50);
        }
        Assert.True(condition(), "condition not met within timeout");
    }
}
