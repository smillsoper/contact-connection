using System.Text.Json;
using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.ValueObjects;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

/// <summary>
/// tf_secure_collect — PCI-safe guided DTMF capture (card number / expiry / CVV / SSN / …).
///
/// Walks the caller through an ordered list of <c>fields</c>, each a play_and_get_digits step run
/// in the <c>secure_collect</c> dialplan extension (this build has no uuid_execute — same pattern
/// as tf_ivr_menu). This node only kicks off field 0; EslBackgroundService.HandleSecureCollectDoneAsync
/// validates each captured value and drives the next field, then finalises.
///
/// PCI handling, all set up here:
///   • Recording — if a recording is live, it's masked with silence for the whole capture and
///     unmasked on every exit (EslBackgroundService, watchdog as backstop).
///   • Mid-call bridge — when the caller is bridged to an agent (<c>_bridged_peer_uuid</c>), the
///     agent leg is parked on hold music (<c>park_with_moh</c>) for the capture and re-bridged
///     after. Pre-agent (caller alone) skips that.
///   • Plaintext — never touches session vars or the call trace here. Collected digits accumulate
///     in a Redis-only <c>sc:{uuid}</c> blob; on completion they're AES-encrypted into
///     <c>call_records.sensitive_data</c> and also exposed as <c>{{secure.&lt;key&gt;}}</c> flow vars
///     (redacted from the trace snapshot by prefix).
///
/// Transitions: collected | failed (bad config / validation / retries exhausted) | timeout.
/// </summary>
public class SecureCollectNodeHandler : ITelephonyNodeHandler
{
    public string NodeType => "tf_secure_collect";

    private const string DefaultInvalidPrompt = "silence_stream://250";

    private readonly ITenantDbContextFactory _dbFactory;
    private readonly ITelephonyCallSessionStore _sessionStore;
    private readonly ICallRecordingController _recording;
    private readonly ISensitiveDataProtector _protector;
    private readonly IEslCommanderFactory _eslFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<SecureCollectNodeHandler> _logger;

    public SecureCollectNodeHandler(
        ITenantDbContextFactory dbFactory,
        ITelephonyCallSessionStore sessionStore,
        ICallRecordingController recording,
        ISensitiveDataProtector protector,
        IEslCommanderFactory eslFactory,
        IConfiguration config,
        ILogger<SecureCollectNodeHandler> logger)
    {
        _dbFactory    = dbFactory;
        _sessionStore = sessionStore;
        _recording    = recording;
        _protector    = protector;
        _eslFactory   = eslFactory;
        _config       = config;
        _logger       = logger;
    }

    public async Task<TelephonyNodeResult> ExecuteAsync(
        JsonObject node, TelephonyFlowContext ctx, CancellationToken ct = default)
    {
        var transitions = node["transitions"]?.AsObject();
        var nodeId      = node["nodeId"]?.GetValue<string>() ?? "tf_secure_collect";

        var fields = SecureCollect.ParseFields(node["fields"]);
        if (fields.Count == 0)
        {
            _logger.LogWarning("SecureCollectNodeHandler [{Uuid}]: no usable fields configured — 'failed'", ctx.ChannelUuid);
            return Follow(transitions, "failed");
        }

        if (!_protector.IsConfigured)
        {
            _logger.LogError(
                "SecureCollectNodeHandler [{Uuid}]: SensitiveData:MasterKey not configured — refusing to capture card data — 'failed'",
                ctx.ChannelUuid);
            return Follow(transitions, "failed");
        }

        // A fresh ESL for the transfer / setvar commands — ctx.Esl is null on an event branch
        // (tf_on_custom_event → tf_secure_collect mid-call).
        await using var owned = ctx.Esl is null ? await _eslFactory.CreateAsync(ct) : null;
        var esl = ctx.Esl ?? owned!;

        // ── Resolve each field's prompt now; stash a compact spec for the done-handler ──
        var specs = new List<JsonObject>();
        foreach (var f in fields)
        {
            var promptArg = await TelephonyAudioResolver.ResolveFileArgAsync(
                _dbFactory, _config, f.PromptFileId, ctx.TenantSchemaName, ct)
                ?? "silence_stream://500";
            specs.Add(new JsonObject
            {
                ["k"]     = f.Key,
                ["min"]   = f.MinDigits,
                ["max"]   = f.MaxDigits,
                ["term"]  = f.Terminator,
                ["val"]   = f.Validation,
                ["prompt"] = promptArg,
            });
        }
        var invalidArg = await TelephonyAudioResolver.ResolveFileArgAsync(
            _dbFactory, _config, node["invalidAudioFileId"]?.GetValue<string>(), ctx.TenantSchemaName, ct)
            ?? DefaultInvalidPrompt;
        var maxTries = Math.Max(1, node["maxTries"]?.GetValue<int>() ?? 3);
        var timeoutMs = Math.Max(1000, node["timeoutMs"]?.GetValue<int>() ?? 12000);
        var interDigitMs = Math.Max(1000, node["interDigitTimeoutMs"]?.GetValue<int>() ?? 5000);

        // ── Mid-call bridge: park the agent on hold music for the capture ──────────
        var peerUuid = ctx.Vars.GetValueOrDefault("_bridged_peer_uuid");
        if (string.IsNullOrEmpty(peerUuid)) peerUuid = ctx.Vars.GetValueOrDefault("_agent_uuid");
        var bridged = !string.IsNullOrEmpty(peerUuid);
        if (bridged)
        {
            _logger.LogInformation(
                "SecureCollectNodeHandler [{Uuid}]: bridged to agent leg {Peer} — parking agent on hold for capture",
                ctx.ChannelUuid, peerUuid);
            try { await esl.TransferAsync(peerUuid!, "park_with_moh", "XML", "default", ct); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "SecureCollectNodeHandler [{Uuid}]: could not park agent leg {Peer} — continuing, agent may hear the prompts",
                    ctx.ChannelUuid, peerUuid);
            }
        }

        // ── Mask a live recording for the whole capture ──────────────────────────
        var maskedRecording = false;
        await using (var db = _dbFactory.Create(ctx.TenantSchemaName))
        {
            var record = await db.CallRecords
                .FirstOrDefaultAsync(r => r.Id == ctx.CallRecordId, ct);
            if (record is { RecordingStartedAt: not null, RecordingStoppedAt: null })
            {
                var outcome = await _recording.MaskAsync(new RecordingMaskCommand
                {
                    ChannelUuid      = ctx.ChannelUuid,
                    CallRecordId     = ctx.CallRecordId,
                    TenantSchemaName = ctx.TenantSchemaName,
                    Source           = RecordingEventSource.SecureCollect,
                    NodeId           = nodeId,
                    Reason           = "secure_collect",
                    MaskFill         = MaskFillKind.Silence,
                }, ctx.Esl, ct);
                maskedRecording = outcome.Ok;
                _logger.LogInformation(
                    "SecureCollectNodeHandler [{Uuid}]: recording mask {Result}", ctx.ChannelUuid, outcome.Ok ? "armed" : $"failed: {outcome.Error}");
            }
        }

        // ── Persist capture state (metadata only — no plaintext) ─────────────────
        ctx.Vars["_sc_in_progress"]       = "true";
        ctx.Vars["_sc_node_id"]           = nodeId;
        ctx.Vars["_sc_fields_json"]       = new JsonArray(specs.Select(s => (JsonNode)s).ToArray()).ToJsonString();
        ctx.Vars["_sc_field_index"]       = "0";
        ctx.Vars["_sc_max_tries"]         = maxTries.ToString();
        ctx.Vars["_sc_timeout_ms"]        = timeoutMs.ToString();
        ctx.Vars["_sc_interdigit_ms"]     = interDigitMs.ToString();
        ctx.Vars["_sc_invalid_arg"]       = invalidArg;
        ctx.Vars["_sc_recording_masked"]  = maskedRecording ? "true" : "false";
        ctx.Vars["_sc_rebridge"]          = bridged ? "true" : "false";
        if (bridged) ctx.Vars["_sc_peer_uuid"] = peerUuid!;
        ctx.Vars["_sc_next_collected"]    = transitions?["collected"]?.GetValue<string>() ?? "";
        ctx.Vars["_sc_next_failed"]       = transitions?["failed"]?.GetValue<string>() ?? transitions?["default"]?.GetValue<string>() ?? "";
        ctx.Vars["_sc_next_timeout"]      = transitions?["timeout"]?.GetValue<string>() ?? transitions?["failed"]?.GetValue<string>() ?? "";

        // Also write the session directly — this node can run from an ivr_done/event resume path
        // where the engine's var-sync races the caller hanging up (mirrors ScheduledCallbackNodeHandler).
        var session = await _sessionStore.GetAsync(ctx.ChannelUuid, ct);
        if (session is not null)
        {
            foreach (var k in new[]
            {
                "_sc_in_progress", "_sc_node_id", "_sc_fields_json", "_sc_field_index", "_sc_max_tries",
                "_sc_timeout_ms", "_sc_interdigit_ms", "_sc_invalid_arg", "_sc_recording_masked",
                "_sc_rebridge", "_sc_peer_uuid", "_sc_next_collected", "_sc_next_failed", "_sc_next_timeout",
            })
                if (ctx.Vars.TryGetValue(k, out var v)) session.Vars[k] = v;
            await _sessionStore.SaveAsync(session, ct);
        }

        await SecureCollect.ApplyFieldVarsAsync(
            esl, ctx.ChannelUuid, specs[0], maxTries, timeoutMs, interDigitMs, invalidArg, ct);
        await esl.TransferAsync(ctx.ChannelUuid, "secure_collect", "XML", "default", ct);

        _logger.LogInformation(
            "SecureCollectNodeHandler [{Uuid}]: capture started — {Count} field(s), first='{Key}' masked={Masked} bridged={Bridged}",
            ctx.ChannelUuid, specs.Count, (string?)specs[0]["k"], maskedRecording, bridged);

        return new TelephonyNodeResult(null, "collecting");
    }

    private static TelephonyNodeResult Follow(JsonObject? transitions, string preferredKey)
    {
        var target = transitions?[preferredKey]?.GetValue<string>()
                     ?? transitions?["default"]?.GetValue<string>();
        return new TelephonyNodeResult(target, preferredKey);
    }
}
