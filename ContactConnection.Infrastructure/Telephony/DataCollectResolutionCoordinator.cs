using System.Linq;
using System.Text.RegularExpressions;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Telephony;

/// <summary>See IIvrVoiceResolutionCoordinator — same claim primitive and shape, kept as its own
/// type because tf_data_collect resolves into a variable + a fixed collected/timeout pair
/// instead of an arbitrary target. Registered as a singleton for the same reason: the claim
/// itself is a Redis SETNX, so process lifetime doesn't matter for correctness.</summary>
public sealed class DataCollectResolutionCoordinator : IDataCollectResolutionCoordinator
{
    private static readonly string[] SessionVarsToClear =
    [
        "_dc_in_progress", "_dc_node_id", "_dc_variable_name", "_dc_next_node", "_dc_timeout_node",
        "_dc_voice_active", "_dc_numeric_only",
    ];

    // Single-digit spoken words only (S150-adjacent live testing) — a caller reading a phone
    // number digit-by-digit, not full compound-number parsing ("sixteen", "seventy"), which is a
    // much bigger, more error-prone problem than this node needs to solve. "oh" is the common
    // alternate reading of "0". Applied before stripping non-digits so a spelled-out word becomes
    // its digit first, then survives the strip.
    private static readonly Dictionary<string, string> DigitWords = new()
    {
        ["zero"] = "0", ["oh"] = "0", ["one"] = "1", ["two"] = "2", ["three"] = "3", ["four"] = "4",
        ["five"] = "5", ["six"] = "6", ["seven"] = "7", ["eight"] = "8", ["nine"] = "9",
    };

    private readonly ITelephonyCallSessionStore _sessionStore;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DataCollectResolutionCoordinator> _logger;

    public DataCollectResolutionCoordinator(
        ITelephonyCallSessionStore sessionStore, IServiceScopeFactory scopeFactory,
        ILogger<DataCollectResolutionCoordinator> logger)
    {
        _sessionStore = sessionStore;
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    public async Task<bool> TryResolveAsync(
        string channelUuid, string? capturedValue, IEslCommander esl, string? resolutionDetail = null,
        CancellationToken ct = default)
    {
        var claimKey = $"dc_claim:{channelUuid}";
        var claimed = await _sessionStore.TrySetKeyAsync(claimKey, "1", TimeSpan.FromSeconds(30), ct);
        if (!claimed)
        {
            _logger.LogInformation(
                "DataCollectResolutionCoordinator [{Uuid}]: lost the race — another path already resolved this capture", channelUuid);
            return false;
        }

        // Deleted immediately (not left to its 30s TTL) — a flow that loops tf_data_collect back
        // on itself (e.g. timeout → retry) re-enters the SAME channel uuid for a brand new
        // capture attempt. A lingering claim from the PREVIOUS attempt would otherwise make that
        // new attempt's own legitimate resolution spuriously "lose the race" against stale state
        // that has nothing to do with it (found live: a retry loop's second attempt intermittently
        // swallowed its own real completion this way).
        try { await _sessionStore.DeleteKeyAsync(claimKey, ct); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DataCollectResolutionCoordinator [{Uuid}]: failed to clear claim key — continuing", channelUuid);
        }

        var session = await _sessionStore.GetAsync(channelUuid, ct);
        var nodeId       = session?.Vars.GetValueOrDefault("_dc_node_id");
        var variableName = session?.Vars.GetValueOrDefault("_dc_variable_name");
        var nextNode     = session?.Vars.GetValueOrDefault("_dc_next_node");
        var timeoutNode  = session?.Vars.GetValueOrDefault("_dc_timeout_node");
        var numericOnly  = session?.Vars.GetValueOrDefault("_dc_numeric_only") == "true";

        var hasValue = !string.IsNullOrEmpty(capturedValue);
        var storedValue = hasValue && numericOnly ? NormalizeToDigits(capturedValue!) : capturedValue;
        // A numeric-only field where every character was punctuation (nothing left after
        // normalizing) has effectively captured nothing — route to timeout, same as an empty
        // transcript, rather than storing an empty string as "collected".
        hasValue = !string.IsNullOrEmpty(storedValue);
        var target = hasValue ? nextNode : timeoutNode;

        if (session is not null)
        {
            if (hasValue && !string.IsNullOrEmpty(variableName))
                session.Vars[variableName] = storedValue!;

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
            _logger.LogWarning(ex, "DataCollectResolutionCoordinator [{Uuid}]: uuid_audio_stream stop failed — continuing", channelUuid);
        }

        _logger.LogInformation(
            "DataCollectResolutionCoordinator [{Uuid}]: resolved value='{Value}' (raw='{Raw}') → {Target}",
            channelUuid, storedValue ?? "(none)", capturedValue ?? "(none)", target ?? "(dead-end)");

        using var scope = _scopeFactory.CreateScope();

        // Recorded here (not by TelephonyFlowEngine's normal per-node loop) for the same reason
        // IvrVoiceResolutionCoordinator does — a DTMF-completion event and a voice transcript both
        // arrive off the flow's own execution loop, so nothing else ever shows what was actually
        // captured.
        if (session is not null && !string.IsNullOrEmpty(nodeId))
        {
            await scope.ServiceProvider.GetRequiredService<ICallTraceRecorder>().RecordStepAsync(
                session.TenantId, session.TenantSchemaName, session.CallRecordId, TraceEngine.Telephony,
                nodeId, "tf_data_collect", label: null, detail: resolutionDetail, transitionTaken: hasValue ? "collected" : "timeout",
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

    /// <summary>Maps spelled-out single-digit words to numerals, then strips everything that isn't
    /// a digit. Word substitution runs first so e.g. "five four one" survives into "541" rather
    /// than losing its spaces before the words are ever matched.</summary>
    private static string NormalizeToDigits(string value)
    {
        var withWordsMapped = Regex.Replace(
            value, @"[a-zA-Z]+",
            m => DigitWords.TryGetValue(m.Value.ToLowerInvariant(), out var digit) ? digit : m.Value);

        return new string(withWordsMapped.Where(char.IsAsciiDigit).ToArray());
    }
}
