using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

/// <summary>
/// tf_record — starts, stops, masks, or unmasks the call recording. All the mechanics live in
/// <see cref="ICallRecordingController"/>; this node is just the flow-graph entry point and the
/// place where the campaign's <see cref="RecordingMode"/> ceiling is applied.
///
/// Placement recipes (mode = ceiling, node = control, where you drop it decides coverage):
///   • IVR / full coverage  — campaign RecordingMode = full. Put a tf_record(start) as the
///     first action in the inbound flow, before tf_answer/tf_route_to_queue. Recording spans
///     IVR, hold, queue, whisper, bridge → disconnect.
///   • Conversation only     — campaign RecordingMode = conversation. Put tf_record(start) in
///     the tf_on_agent_answer branch. Recording spans the agent conversation only.
///   • Sensitive-data mask   — from the CRM script flow, fire a custom event whose
///     tf_on_custom_event branch holds tf_record(mask) / tf_record(unmask). The browser
///     extension's field-focus path does the same thing over SignalR.
///   • Final stop            — optional: tf_record(stop) in the tf_on_call_disconnected branch.
///     Not required — EslBackgroundService closes the recording event trail on hangup anyway.
///
/// Consent: when the campaign's <see cref="ConsentModel"/> requires an announcement
/// (two_party_announce / two_party_announce_optout), a start plays the consent audio (node's
/// consentAudioFileId, or a TTS fallback) on the caller leg immediately before uuid_record
/// start — so the announcement is captured at the very top of the recording as proof it
/// played. The DTMF opt-out itself is flow design: put a tf_ivr_menu before this node and
/// route opt-out callers around it.
/// </summary>
public class RecordNodeHandler : ITelephonyNodeHandler
{
    public string NodeType => "tf_record";

    private const string DefaultConsentText =
        "This call may be recorded for quality assurance and training purposes.";

    private readonly ICallRecordingController _recording;
    private readonly ITenantDbContextFactory _dbFactory;
    private readonly ITelephonyCallSessionStore _sessionStore;
    private readonly IConfiguration _config;
    private readonly ILogger<RecordNodeHandler> _logger;

    public RecordNodeHandler(
        ICallRecordingController recording,
        ITenantDbContextFactory dbFactory,
        ITelephonyCallSessionStore sessionStore,
        IConfiguration config,
        ILogger<RecordNodeHandler> logger)
    {
        _recording    = recording;
        _dbFactory    = dbFactory;
        _sessionStore = sessionStore;
        _config       = config;
        _logger       = logger;
    }

    public async Task<TelephonyNodeResult> ExecuteAsync(
        JsonObject node, TelephonyFlowContext ctx, CancellationToken ct = default)
    {
        var action = (node["action"]?.GetValue<string>() ?? RecordingEventAction.Start).Trim().ToLowerInvariant();
        if (!RecordingEventAction.IsValid(action))
        {
            _logger.LogWarning("RecordNodeHandler [{Uuid}]: unknown action '{Action}' — skipping", ctx.ChannelUuid, action);
            return FollowDefault(node);
        }

        // An event branch (tf_on_custom_event, tf_on_agent_answer, …) runs with no ESL handle;
        // the pre-answer engine always has one. Use that to attribute the audit event and to
        // tell whether the call is already past the pre-bridge stage.
        var source = ctx.Esl is null ? RecordingEventSource.CustomEvent : RecordingEventSource.FlowNode;

        var command = new RecordingCommand
        {
            ChannelUuid      = ctx.ChannelUuid,
            CallRecordId     = ctx.CallRecordId,
            TenantSchemaName = ctx.TenantSchemaName,
            Source           = source,
            NodeId           = node["nodeId"]?.GetValue<string>(),
        };

        if (action == RecordingEventAction.Start)
        {
            var campaign = await LoadCampaignAsync(ctx, ct);
            var mode = campaign?.RecordingMode ?? RecordingMode.Disabled;

            if (mode == RecordingMode.Disabled)
            {
                _logger.LogInformation(
                    "RecordNodeHandler [{Uuid}]: campaign RecordingMode=disabled — tf_record(start) is a no-op", ctx.ChannelUuid);
                return FollowDefault(node);
            }

            if (!RecordingMode.AllowsPreBridge(mode) && !await IsBridgedAsync(ctx, ct))
                _logger.LogWarning(
                    "RecordNodeHandler [{Uuid}]: campaign RecordingMode=conversation but recording started before the agent bridge — " +
                    "place tf_record(start) in the tf_on_agent_answer branch. Proceeding anyway.", ctx.ChannelUuid);

            if (ConsentModel.RequiresAnnouncement(campaign?.ConsentModel ?? ConsentModel.OneParty))
                await PlayConsentAnnouncementAsync(node, ctx, ct);

            var options = new RecordingStartOptions
            {
                Stereo       = campaign?.RecordStereo ?? true,
                LimitSeconds = node["recordLimitSeconds"]?.GetValue<int>() ?? 0,
            };

            var outcome = await _recording.StartAsync(command, options, ctx.Esl, ct);
            LogOutcome(ctx, action, outcome);
            return FollowDefault(node);
        }

        if (action == RecordingEventAction.Mask)
        {
            var maskCommand = new RecordingMaskCommand
            {
                ChannelUuid      = command.ChannelUuid,
                CallRecordId     = command.CallRecordId,
                TenantSchemaName = command.TenantSchemaName,
                Source           = source,
                NodeId           = command.NodeId,
                Reason           = node["reason"]?.GetValue<string>(),
                MaskFill         = node["maskFill"]?.GetValue<string>() ?? MaskFillKind.Silence,
                MaxMaskSeconds   = node["maxMaskSeconds"]?.GetValue<int>(),
            };
            LogOutcome(ctx, action, await _recording.MaskAsync(maskCommand, ctx.Esl, ct));
            return FollowDefault(node);
        }

        // stop | unmask
        var result = action == RecordingEventAction.Stop
            ? await _recording.StopAsync(command, ctx.Esl, ct)
            : await _recording.UnmaskAsync(command, ctx.Esl, ct);
        LogOutcome(ctx, action, result);
        return FollowDefault(node);
    }

    /// <summary>
    /// Plays the two-party consent announcement on the caller leg (fire-and-forget) right before
    /// recording starts, so it's captured at the head of the file. Configured audio file first;
    /// otherwise a TTS fallback so a two-party campaign can never start recording with no
    /// announcement at all.
    /// </summary>
    private async Task PlayConsentAnnouncementAsync(JsonObject node, TelephonyFlowContext ctx, CancellationToken ct)
    {
        if (ctx.Esl is null)
        {
            _logger.LogWarning(
                "RecordNodeHandler [{Uuid}]: consent announcement required but no ESL handle (event branch) — recording without it",
                ctx.ChannelUuid);
            return;
        }

        var fileArg = await TelephonyAudioResolver.ResolveFileArgAsync(
            _dbFactory, _config, node["consentAudioFileId"]?.GetValue<string>(), ctx.TenantSchemaName, ct);

        if (fileArg is not null)
        {
            await ctx.Esl.BroadcastAsync(ctx.ChannelUuid, fileArg, ct);
            _logger.LogInformation("RecordNodeHandler [{Uuid}]: consent announcement (file) → {Arg}", ctx.ChannelUuid, fileArg);
            return;
        }

        var text  = node["consentTtsText"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(text)) text = DefaultConsentText;
        var voice = node["consentTtsVoice"]?.GetValue<string>() ?? "kal";

        // Route the text through a channel var so the uuid_broadcast command line stays space-free
        // (same reason as PlayNodeHandler's flite path).
        await ctx.Esl.SetChannelVarAsync(ctx.ChannelUuid, "cc_consent_text", text.Replace("\n", " ").Trim(), ct);
        await ctx.Esl.BroadcastAsync(ctx.ChannelUuid, $"tts://flite|{voice}|${{cc_consent_text}}", ct);
        _logger.LogInformation(
            "RecordNodeHandler [{Uuid}]: consent announcement (TTS fallback, no consentAudioFileId configured)", ctx.ChannelUuid);
    }

    private async Task<Campaign?> LoadCampaignAsync(TelephonyFlowContext ctx, CancellationToken ct)
    {
        if (ctx.CampaignId == Guid.Empty) return null;
        await using var db = _dbFactory.Create(ctx.TenantSchemaName);
        return await db.Campaigns.FirstOrDefaultAsync(c => c.Id == ctx.CampaignId, ct);
    }

    private async Task<bool> IsBridgedAsync(TelephonyFlowContext ctx, CancellationToken ct)
    {
        if (ctx.AnsweringAgentId is not null) return true;
        var session = await _sessionStore.GetAsync(ctx.ChannelUuid, ct);
        return session?.Vars.ContainsKey("_assigned_agent_id") == true;
    }

    private void LogOutcome(TelephonyFlowContext ctx, string action, RecordingActionOutcome outcome)
    {
        if (!outcome.Ok)
            _logger.LogWarning(
                "RecordNodeHandler [{Uuid}]: recording {Action} failed: {Error}", ctx.ChannelUuid, action, outcome.Error);
    }

    private static TelephonyNodeResult FollowDefault(JsonObject node)
    {
        var next = node["transitions"]?.AsObject()?["default"]?.GetValue<string>();
        return new TelephonyNodeResult(next, "default");
    }
}
