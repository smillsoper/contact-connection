using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Telephony;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony;

/// <summary>
/// The race this coordinates (DTMF vs. a matched spoken phrase, S148) can't be exercised
/// end-to-end in a unit test — it's inherently two concurrent real-world code paths — but the
/// claim itself (exactly one caller ever proceeds past TrySetKeyAsync for the same channel) is
/// the one piece of logic worth pinning down directly.
/// </summary>
public class IvrVoiceResolutionCoordinatorTests
{
    private const string Uuid = "call-uuid-voice-1";

    private static (IvrVoiceResolutionCoordinator Coordinator, Mock<ITelephonyCallSessionStore> SessionStore, Mock<ITelephonyFlowEngine> FlowEngine, Mock<ICallTraceRecorder> TraceRecorder)
        NewCoordinator(bool firstClaimWins = true, TelephonyCallSession? session = null)
    {
        var sessionStore = new Mock<ITelephonyCallSessionStore>();
        sessionStore.SetupSequence(s => s.TrySetKeyAsync(
                $"ivr_voice_claim:{Uuid}", "1", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(firstClaimWins)
            .ReturnsAsync(false);
        sessionStore.Setup(s => s.GetAsync(Uuid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session ?? new TelephonyCallSession { ChannelUuid = Uuid });
        sessionStore.Setup(s => s.SaveAsync(It.IsAny<TelephonyCallSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var flowEngine = new Mock<ITelephonyFlowEngine>();
        flowEngine.Setup(f => f.ResumeFromNodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEslCommander>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var traceRecorder = new Mock<ICallTraceRecorder>();
        traceRecorder.Setup(r => r.RecordStepAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(flowEngine.Object);
        services.AddSingleton(traceRecorder.Object);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var coordinator = new IvrVoiceResolutionCoordinator(
            sessionStore.Object, scopeFactory, NullLogger<IvrVoiceResolutionCoordinator>.Instance);

        return (coordinator, sessionStore, flowEngine, traceRecorder);
    }

    private static Mock<IEslCommander> NewEsl()
    {
        var esl = new Mock<IEslCommander>();
        esl.Setup(e => e.StopAudioStreamAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .Returns(Task.CompletedTask);
        return esl;
    }

    [Fact]
    public async Task TryResolveAsync_FirstCaller_Wins_ResumesFlow()
    {
        var (coordinator, _, flowEngine, _) = NewCoordinator();
        var esl = NewEsl();

        var won = await coordinator.TryResolveAsync(Uuid, "node_sales", esl.Object);

        Assert.True(won);
        esl.Verify(e => e.StopAudioStreamAsync(Uuid, It.IsAny<CancellationToken>()), Times.Once);
        flowEngine.Verify(f => f.ResumeFromNodeAsync(Uuid, "node_sales", esl.Object, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TryResolveAsync_RecordsTraceStep_WithResolutionDetail()
    {
        var session = new TelephonyCallSession
        {
            ChannelUuid = Uuid,
            TenantId = Guid.NewGuid(),
            TenantSchemaName = "tenant_test",
            CallRecordId = Guid.NewGuid(),
            CampaignId = Guid.NewGuid(),
            FlowId = Guid.NewGuid(),
            CallerNumber = "+15550001111",
            DestinationNumber = "+15550002222",
            Vars = new Dictionary<string, string> { ["_ivr_voice_node_id"] = "menu_1" },
        };
        var (coordinator, _, _, traceRecorder) = NewCoordinator(session: session);
        var esl = NewEsl();

        var won = await coordinator.TryResolveAsync(
            Uuid, "node_sales", esl.Object, resolutionDetail: "voice: recognized \"yes\" — matched");

        Assert.True(won);
        traceRecorder.Verify(r => r.RecordStepAsync(
            session.TenantId, session.TenantSchemaName, session.CallRecordId, "telephony", "menu_1",
            "tf_ivr_menu", null, "voice: recognized \"yes\" — matched", null, "node_sales", null,
            session.CampaignId, session.FlowId, session.DestinationNumber, session.CallerNumber, null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TryResolveAsync_SecondCaller_LosesRace_NoSideEffects()
    {
        var sessionStore = new Mock<ITelephonyCallSessionStore>();
        sessionStore.Setup(s => s.TrySetKeyAsync(
                $"ivr_voice_claim:{Uuid}", "1", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // already claimed by the other racing path

        var flowEngine = new Mock<ITelephonyFlowEngine>();
        var services = new ServiceCollection();
        services.AddSingleton(flowEngine.Object);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        var coordinator = new IvrVoiceResolutionCoordinator(
            sessionStore.Object, scopeFactory, NullLogger<IvrVoiceResolutionCoordinator>.Instance);
        var esl = NewEsl();

        var won = await coordinator.TryResolveAsync(Uuid, "node_support", esl.Object);

        Assert.False(won);
        esl.Verify(e => e.StopAudioStreamAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        flowEngine.Verify(f => f.ResumeFromNodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEslCommander>(), It.IsAny<CancellationToken>()), Times.Never);
        sessionStore.Verify(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TryResolveAsync_NullTarget_ClaimsAndStopsCapture_ButDoesNotResumeFlow()
    {
        var (coordinator, _, flowEngine, _) = NewCoordinator();
        var esl = NewEsl();

        var won = await coordinator.TryResolveAsync(Uuid, target: null, esl.Object);

        Assert.True(won);
        esl.Verify(e => e.StopAudioStreamAsync(Uuid, It.IsAny<CancellationToken>()), Times.Once);
        flowEngine.Verify(f => f.ResumeFromNodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEslCommander>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
