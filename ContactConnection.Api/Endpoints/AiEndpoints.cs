using System.Text.Json;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Ai;
using ContactConnection.Infrastructure.Commerce;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// AI call summaries (AI learning track, S171).
/// <list type="bullet">
/// <item>Step 1 — preview the exact redacted context the model would receive (calls.view).</item>
/// <item>Step 2 — generate a summary; every generation is saved as a <i>suggestion</i> with its model,
/// tokens and cost (calls.view).</item>
/// <item>Step (b) — a person confirms (as-is or edited) or discards it (calls.manage). A changed disposition
/// goes through the normal custom-field path: change history + commission recalculation. Nothing the AI
/// produces is saved to the call until a person confirms it.</item>
/// </list>
/// </summary>
public static class AiEndpoints
{
    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapAiEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/call-review/calls/{id:guid}/ai/context", PreviewContext).RequireAuthorization();
        app.MapPost("/api/v1/call-review/calls/{id:guid}/ai/summary", GenerateSummary).RequireAuthorization();
        app.MapGet("/api/v1/call-review/calls/{id:guid}/ai/summaries", Summaries).RequireAuthorization();
        app.MapPost("/api/v1/call-review/calls/{id:guid}/ai/summaries/{summaryId:guid}/confirm", Confirm).RequireAuthorization();
        app.MapPost("/api/v1/call-review/calls/{id:guid}/ai/summaries/{summaryId:guid}/discard", Discard).RequireAuthorization();
        app.MapGet("/api/v1/ai/summaries/mine/pending", MyPending).RequireAuthorization();
        return app;
    }

    private static bool Has(HttpContext http, string permission) =>
        (http.User.FindFirst("permissions")?.Value ?? "").Split(',').Contains(permission);

    /// <summary>Reviewers with the permission, or the agent who handled the call (their own wrap-up).</summary>
    private static async Task<bool> CanAccessAsync(HttpContext http, TenantDbContext db, Guid callId, string permission, CancellationToken ct)
    {
        if (Has(http, permission)) return true;
        if (ActorResolver.Resolve(http.User) is not { } actor) return false;
        // The call's agent, or the agent of one of its interactions (S178: a CS agent's own wrap-up on a transferred call),
        // or whoever has the call's script now — a supervisor who took the call over finishes its wrap-up (S184).
        return await db.CallRecords.AsNoTracking().AnyAsync(r => r.Id == callId
            && (r.AgentId == actor.Id || r.Interactions.Any(i => i.AgentId == actor.Id)), ct)
            || await db.FlowSessions.AsNoTracking().AnyAsync(f => f.CallRecordId == callId && f.AgentId == actor.Id, ct);
    }

    /// <summary>The disposition currently recorded for an interaction: a transferred one's own field, else the record's.</summary>
    private static async Task<string?> RecordedDispositionAsync(TenantDbContext db, Guid callId, Guid? interactionId, CancellationToken ct)
    {
        var record = await db.CallRecords.AsNoTracking().Include(r => r.Interactions).FirstOrDefaultAsync(r => r.Id == callId, ct);
        if (record is null) return null;
        var ix = interactionId is { } want ? record.Interactions.FirstOrDefault(i => i.Id == want) : null;
        var transferred = ix?.CampaignId is { } c && c != record.CampaignId;
        return RecordedDisposition(transferred ? ix!.CustomFields : record.CustomFields);
    }

    private static async Task<IResult> PreviewContext(
        Guid id, Guid? interactionId, HttpContext http, TenantContext tenant, ScopedTenantDbContextFactory dbFactory, CancellationToken ct)
    {
        if (!tenant.HasTenant) return Results.Unauthorized();
        if (!Has(http, Permission.CallsView)) return Results.Forbid();

        await using var db = dbFactory.Create();
        var context = await CallSummaryContextBuilder.BuildAsync(db, id, ct, interactionId);
        if (context is null) return Results.NotFound();
        return Results.Ok(new
        {
            context.Text,
            context.Redactions,
            context.ScriptSteps,
            characters = context.Text.Length,
            // Rule of thumb: ~4 characters per token for English text. The API reports the exact count.
            estimatedTokens = (int)Math.Ceiling(context.Text.Length / 4.0),
        });
    }

    // Generate: ask the model, save the answer as a suggestion (provenance + cost), return it for review.
    // A failed AI call is a 503 with a plain message, never a broken page.
    private static async Task<IResult> GenerateSummary(
        Guid id, Guid? interactionId, HttpContext http, TenantContext tenant, ScopedTenantDbContextFactory dbFactory,
        CallSummarizer summarizer, AnthropicClient client, CancellationToken ct)
    {
        if (!tenant.HasTenant) return Results.Unauthorized();
        if (!client.IsConfigured) return Results.Json(new { error = "AI isn't configured on this server." }, statusCode: 503);

        await using var db = dbFactory.Create();
        if (!await CanAccessAsync(http, db, id, Permission.CallsView, ct)) return Results.Forbid();
        try
        {
            var result = await summarizer.SummarizeAsync(db, id, ct, interactionId);
            if (result is null) return Results.NotFound();

            var s = result.Summary;
            var u = result.Usage;
            var row = CallSummary.Suggest(tenant.Current!.Id, id, s.Text, s.ReasonForCall, s.Outcome, s.SuggestedDisposition,
                s.DispositionValid, s.Confidence, s.FollowUp, s.IsTestCall, result.PossibleTestCall,
                u.Model, u.InputTokens, u.OutputTokens, u.EstimatedCostUsd, u.ElapsedMs, ActorResolver.Resolve(http.User)?.Name,
                interactionId);
            db.CallSummaries.Add(row);
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { summaryId = row.Id, result.Summary, result.Usage, result.AllowedDispositions, result.PossibleTestCall,
                recordedDisposition = await RecordedDispositionAsync(db, id, interactionId, ct) });
        }
        catch (AnthropicClient.AiUnavailableException ex)
        {
            return Results.Json(new { error = $"Summary unavailable — {ex.Message}" }, statusCode: 503);
        }
    }

    /// <summary>The call's confirmed summary (if any), plus counts for the acceptance / edit picture.</summary>
    private static async Task<IResult> Summaries(
        Guid id, Guid? interactionId, HttpContext http, TenantContext tenant, ScopedTenantDbContextFactory dbFactory, CancellationToken ct)
    {
        if (!tenant.HasTenant) return Results.Unauthorized();
        await using var db = dbFactory.Create();
        if (!await CanAccessAsync(http, db, id, Permission.CallsView, ct)) return Results.Forbid();
        // One interaction's summaries when asked (S178 per-interaction view); otherwise the whole call's.
        var all = await db.CallSummaries.AsNoTracking()
            .Where(s => s.CallRecordId == id && (interactionId == null || s.InteractionId == interactionId))
            .OrderByDescending(s => s.CreatedAt).ToListAsync(ct);
        var confirmed = all.FirstOrDefault(s => s.Status == CallSummaryStatus.Confirmed);
        // The newest unreviewed suggestion (e.g. the automatic wrap-up one), in the same shape Generate returns.
        var pending = all.FirstOrDefault(s => s.Status == CallSummaryStatus.Suggested);
        object? pendingJson = null;
        if (pending is not null)
        {
            var allowed = await DispositionCatalog.ForCallAsync(db, id, ct, pending.InteractionId);
            pendingJson = new
            {
                summaryId = pending.Id,
                summary = new
                {
                    text = pending.AiSummary, reasonForCall = pending.AiReasonForCall, outcome = pending.AiOutcome,
                    suggestedDisposition = pending.AiDisposition, dispositionValid = pending.AiDispositionValid,
                    confidence = pending.AiConfidence, followUp = pending.AiFollowUp, isTestCall = pending.AiIsTestCall,
                },
                usage = new { model = pending.Model, inputTokens = pending.InputTokens, outputTokens = pending.OutputTokens,
                    estimatedCostUsd = pending.CostUsd, elapsedMs = pending.ElapsedMs, attempts = 1 },
                allowedDispositions = allowed,
                possibleTestCall = pending.PossibleTestCall,
                recordedDisposition = await RecordedDispositionAsync(db, id, pending.InteractionId, ct),
            };
        }
        return Results.Ok(new
        {
            confirmed = confirmed is null ? null : new
            {
                confirmed.Id, confirmed.Summary, confirmed.ReasonForCall, confirmed.Outcome, confirmed.Disposition, confirmed.FollowUp,
                confirmed.Edited, confirmed.ReviewedByName, confirmed.ReviewedAt,
                ai = new { confirmed.AiSummary, confirmed.AiReasonForCall, confirmed.AiOutcome, confirmed.AiDisposition, confirmed.AiFollowUp, confirmed.AiConfidence, confirmed.Model },
            },
            pending = pendingJson,
            generated = all.Count,
            totalCostUsd = all.Sum(s => s.CostUsd),
        });
    }

    private static async Task<IResult> Confirm(
        Guid id, Guid summaryId, ConfirmSummaryRequest req, HttpContext http, TenantContext tenant,
        ScopedTenantDbContextFactory dbFactory, ICustomFieldService customFields, ICallRecordAuditRepository audit,
        ICommissionService commissions, CancellationToken ct)
    {
        if (!tenant.HasTenant) return Results.Unauthorized();
        if (ActorResolver.Resolve(http.User) is not { } actor) return Results.Forbid();

        await using var db = dbFactory.Create();
        if (!await CanAccessAsync(http, db, id, Permission.CallsManage, ct)) return Results.Forbid();
        var row = await db.CallSummaries.FirstOrDefaultAsync(s => s.Id == summaryId && s.CallRecordId == id, ct);
        if (row is null) return Results.NotFound();
        if (!CallSummarizer.Outcomes.Contains(req.Outcome ?? "")) return Results.BadRequest(new { error = "Choose an outcome." });

        // The confirmed disposition must be one the script can record — the same rule the AI was held to.
        var disposition = string.IsNullOrWhiteSpace(req.Disposition) ? null : req.Disposition.Trim();
        var allowed = await DispositionCatalog.ForCallAsync(db, id, ct, row.InteractionId);
        if (disposition is not null && allowed.Count > 0 && !allowed.Contains(disposition, StringComparer.OrdinalIgnoreCase))
            return Results.BadRequest(new { error = $"'{disposition}' isn't one of this script's dispositions." });

        try
        {
            foreach (var previous in await db.CallSummaries.Where(s => s.CallRecordId == id && s.InteractionId == row.InteractionId
                         && s.Status == CallSummaryStatus.Confirmed).ToListAsync(ct))
                previous.Supersede();
            row.Confirm(req.Summary ?? "", req.ReasonForCall ?? "", req.Outcome!, disposition, req.FollowUp, actor.Id, actor.Name);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        await db.SaveChangesAsync(ct);

        await audit.AddAsync(CallRecordAuditEntry.Create(id, CallAuditAction.AiSummaryConfirmed,
            $"AI summary confirmed{(row.Edited ? " (edited)" : "")}: {Truncate(row.Summary!, 300)}",
            JsonSerializer.Serialize(new { summaryId = row.Id, row.Edited, row.Model, ai = row.AiSummary, final = row.Summary, row.Disposition }, AuditJson),
            actor.Id, actor.Name), ct);

        // A changed disposition goes through the same path as a manual edit — no AI shortcut around the rules.
        var recorded = await RecordedDispositionAsync(db, id, row.InteractionId, ct);
        if (disposition is not null && !string.Equals(disposition, recorded, StringComparison.Ordinal))
        {
            var field = (await customFields.GetFieldsForCallAsync(id, ct)).FirstOrDefault(f => f.Definition.FieldName == DispositionCatalog.FieldName);
            if (field is not null)
            {
                // Same routing as a script write: a transferred interaction's disposition lands on that interaction.
                await customFields.SetValueFromScriptAsync(id, row.InteractionId ?? Guid.Empty, field.Definition.Id, disposition, ct);
                await audit.AddAsync(CallRecordAuditEntry.Create(id, CallAuditAction.CustomFieldEdited,
                    $"Custom field “{field.Definition.DisplayLabel}”: {recorded ?? "(blank)"} → {disposition} (confirmed from AI summary)",
                    JsonSerializer.Serialize(new { definitionId = field.Definition.Id, field.Definition.FieldName, before = recorded, after = disposition, aiSummaryId = row.Id }, AuditJson),
                    actor.Id, actor.Name), ct);
                await commissions.RecalculateAsync(id, CommissionTrigger.CustomFieldEdited, ct);
            }
        }
        return Results.Ok(new { row.Id, row.Edited });
    }

    private static async Task<IResult> Discard(
        Guid id, Guid summaryId, HttpContext http, TenantContext tenant, ScopedTenantDbContextFactory dbFactory, CancellationToken ct)
    {
        if (!tenant.HasTenant) return Results.Unauthorized();
        if (ActorResolver.Resolve(http.User) is not { } actor) return Results.Forbid();
        await using var db = dbFactory.Create();
        if (!await CanAccessAsync(http, db, id, Permission.CallsManage, ct)) return Results.Forbid();
        var row = await db.CallSummaries.FirstOrDefaultAsync(s => s.Id == summaryId && s.CallRecordId == id, ct);
        if (row is null) return Results.NotFound();
        try { row.Discard(actor.Id, actor.Name); }
        catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    /// <summary>The signed-in agent's calls with an AI summary awaiting their review (last 12 hours) — what the
    /// agent portal's wrap-up card shows, so it survives a page refresh.</summary>
    private static async Task<IResult> MyPending(HttpContext http, TenantContext tenant, ScopedTenantDbContextFactory dbFactory, CancellationToken ct)
    {
        if (!tenant.HasTenant) return Results.Unauthorized();
        if (ActorResolver.Resolve(http.User) is not { } actor) return Results.Unauthorized();
        await using var db = dbFactory.Create();
        var since = DateTimeOffset.UtcNow.AddHours(-12);
        // Per interaction (S178): the agent who worked the interaction reviews its summary — on a transferred call the CS
        // agent gets theirs. Legacy rows (no interaction) go to the call's agent. Whoever has the interaction's script at
        // the end owns the wrap-up (S184): after a take-over that's the supervisor, not the agent who was moved off it.
        var pending = await db.CallSummaries.AsNoTracking()
            .Where(s => s.Status == CallSummaryStatus.Suggested && s.CreatedAt >= since
                && (s.InteractionId != null
                    ? (db.FlowSessions.Where(f => f.InteractionId == s.InteractionId).OrderByDescending(f => f.StartedAt)
                            .Select(f => (Guid?)f.AgentId).FirstOrDefault()
                        ?? db.CallInteractions.Where(i => i.Id == s.InteractionId).Select(i => (Guid?)i.AgentId).FirstOrDefault()) == actor.Id
                    : db.CallRecords.Any(r => r.Id == s.CallRecordId && r.AgentId == actor.Id)))
            .GroupBy(s => new { s.CallRecordId, s.InteractionId })
            .Select(g => new { g.Key.CallRecordId, g.Key.InteractionId, CreatedAt = g.Max(s => s.CreatedAt) })
            .ToListAsync(ct);
        var ixIds = pending.Select(p => p.InteractionId).OfType<Guid>().ToList();
        var ixInfo = await db.CallInteractions.AsNoTracking().Where(i => ixIds.Contains(i.Id))
            .Select(i => new { i.Id, i.CampaignId, i.OrderNumber }).ToDictionaryAsync(i => i.Id, ct);

        // Details so the agent can tell several waiting calls apart. Shown to the agent who handled the call —
        // never added to what's sent to the AI.
        var ids = pending.Select(p => p.CallRecordId).ToList();
        var calls = await db.CallRecords.AsNoTracking().Where(r => ids.Contains(r.Id))
            .Select(r => new { r.Id, r.CreatedAt, r.CampaignId, r.FirstName, r.LastName, r.CallerId, r.HandleTimeSeconds })
            .ToDictionaryAsync(r => r.Id, ct);
        var campaignIds = calls.Values.Select(c => c.CampaignId)
            .Concat(ixInfo.Values.Select(i => i.CampaignId).OfType<Guid>()).Distinct().ToList();
        var campaigns = await db.Campaigns.AsNoTracking().Where(c => campaignIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        return Results.Ok(pending.OrderByDescending(p => p.CreatedAt).Select(p =>
        {
            var c = calls.GetValueOrDefault(p.CallRecordId);
            var ix = p.InteractionId is { } iid ? ixInfo.GetValueOrDefault(iid) : null;
            return new
            {
                callRecordId = p.CallRecordId,
                interactionId = p.InteractionId,
                createdAt = p.CreatedAt,
                callStartedAt = c?.CreatedAt,
                campaign = ix?.CampaignId is { } ic ? campaigns.GetValueOrDefault(ic) : c is null ? null : campaigns.GetValueOrDefault(c.CampaignId),
                callerName = c is null ? null : $"{c.FirstName} {c.LastName}".Trim() is { Length: > 0 } n ? n : null,
                callerNumber = c?.CallerId,
                orderNumber = ix?.OrderNumber,
                handleTimeSeconds = c?.HandleTimeSeconds,
            };
        }));
    }

    private static string? RecordedDisposition(string? customFieldsJson) =>
        CommissionLedger.CustomFieldValues(customFieldsJson).GetValueOrDefault(DispositionCatalog.FieldName) is { Length: > 0 } d ? d : null;

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}

public record ConfirmSummaryRequest(string? Summary, string? ReasonForCall, string? Outcome, string? Disposition, string? FollowUp);
