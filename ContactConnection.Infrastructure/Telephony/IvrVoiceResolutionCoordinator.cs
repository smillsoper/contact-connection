using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Telephony;

/// <summary>See IIvrVoiceResolutionCoordinator. Registered as a singleton — the claim itself is
/// a Redis SETNX (ITelephonyCallSessionStore.TrySetKeyAsync), so process lifetime doesn't
/// matter for correctness, but one shared instance avoids re-resolving IServiceScopeFactory
/// dependencies per call.</summary>
public sealed class IvrVoiceResolutionCoordinator : IIvrVoiceResolutionCoordinator
{
    private static readonly string[] SessionVarsToClear =
    [
        "_ivr_voice_digit_options", "_ivr_voice_node_id",
    ];

    private readonly ITelephonyCallSessionStore _sessionStore;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IvrVoiceResolutionCoordinator> _logger;

    public IvrVoiceResolutionCoordinator(
        ITelephonyCallSessionStore sessionStore, IServiceScopeFactory scopeFactory,
        ILogger<IvrVoiceResolutionCoordinator> logger)
    {
        _sessionStore = sessionStore;
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    public async Task<bool> TryResolveAsync(
        string channelUuid, string? target, IEslCommander esl, string? resolutionDetail = null,
        CancellationToken ct = default)
    {
        var claimed = await _sessionStore.TrySetKeyAsync(
            $"ivr_voice_claim:{channelUuid}", "1", TimeSpan.FromSeconds(30), ct);
        if (!claimed)
        {
            _logger.LogInformation(
                "IvrVoiceResolutionCoordinator [{Uuid}]: lost the race — another path already resolved this menu", channelUuid);
            return false;
        }

        var session = await _sessionStore.GetAsync(channelUuid, ct);
        var nodeId = session?.Vars.GetValueOrDefault("_ivr_voice_node_id");
        if (session is not null)
        {
            foreach (var key in SessionVarsToClear)
                session.Vars.Remove(key);
            await _sessionStore.SaveAsync(session, ct);
        }

        try
        {
            await esl.StopAudioStreamAsync(channelUuid, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IvrVoiceResolutionCoordinator [{Uuid}]: uuid_audio_stream stop failed — continuing", channelUuid);
        }

        _logger.LogInformation(
            "IvrVoiceResolutionCoordinator [{Uuid}]: resolved → {Target}", channelUuid, target ?? "(dead-end)");

        using var scope = _scopeFactory.CreateScope();

        // Recorded here (not by TelephonyFlowEngine's normal per-node loop) because this
        // resolution happens asynchronously, off the flow's own execution loop — the node's
        // synchronous handler already exited with a "collecting" step before either racing
        // path (DTMF or a matched phrase) ever fires. Without this, nothing in the trace ever
        // shows what STT actually transcribed — exactly what tenants need to diagnose a
        // misrecognized phrase.
        if (session is not null && !string.IsNullOrEmpty(nodeId))
        {
            await scope.ServiceProvider.GetRequiredService<ICallTraceRecorder>().RecordStepAsync(
                session.TenantId, session.TenantSchemaName, session.CallRecordId, TraceEngine.Telephony,
                nodeId, "tf_ivr_menu", label: null, detail: resolutionDetail, transitionTaken: null,
                nextNodeId: target, exitReason: null, session.CampaignId, session.FlowId,
                session.DestinationNumber, session.CallerNumber, stateSnapshot: null, ct);
        }

        if (!string.IsNullOrEmpty(target))
        {
            await scope.ServiceProvider
                .GetRequiredService<ITelephonyFlowEngine>()
                .ResumeFromNodeAsync(channelUuid, target, esl, ct);
        }

        return true;
    }
}
