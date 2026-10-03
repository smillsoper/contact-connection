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
        return app;
    }

    private static bool Has(HttpContext http, string permission) =>
        (http.User.FindFirst("permissions")?.Value ?? "").Split(',').Contains(permission);

    private static async Task<IResult> PreviewContext(
        Guid id, HttpContext http, TenantContext tenant, ScopedTenantDbContextFactory dbFactory, CancellationToken ct)
    {
        if (!tenant.HasTenant) return Results.Unauthorized();
        if (!Has(http, Permission.CallsView)) return Results.Forbid();

        await using var db = dbFactory.Create();
        var context = await CallSummaryContextBuilder.BuildAsync(db, id, ct);
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
        Guid id, HttpContext http, TenantContext tenant, ScopedTenantDbContextFactory dbFactory,
        CallSummarizer summarizer, AnthropicClient client, CancellationToken ct)
    {
        if (!tenant.HasTenant) return Results.Unauthorized();
        if (!Has(http, Permission.CallsView)) return Results.Forbid();
        if (!client.IsConfigured) return Results.Json(new { error = "AI isn't configured on this server." }, statusCode: 503);

        await using var db = dbFactory.Create();
        try
        {
            var result = await summarizer.SummarizeAsync(db, id, ct);
            if (result is null) return Results.NotFound();

            var s = result.Summary;
            var u = result.Usage;
            var row = CallSummary.Suggest(tenant.Current!.Id, id, s.Text, s.ReasonForCall, s.Outcome, s.SuggestedDisposition,
                s.DispositionValid, s.Confidence, s.FollowUp, s.IsTestCall, result.PossibleTestCall,
                u.Model, u.InputTokens, u.OutputTokens, u.EstimatedCostUsd, u.ElapsedMs, ActorResolver.Resolve(http.User)?.Name);
            db.CallSummaries.Add(row);
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { summaryId = row.Id, result.Summary, result.Usage, result.AllowedDispositions, result.PossibleTestCall,
                recordedDisposition = RecordedDisposition(await db.CallRecords.AsNoTracking().Where(r => r.Id == id).Select(r => r.CustomFields).FirstOrDefaultAsync(ct)) });
        }
        catch (AnthropicClient.AiUnavailableException ex)
        {
            return Results.Json(new { error = $"Summary unavailable — {ex.Message}" }, statusCode: 503);
        }
    }

    /// <summary>The call's confirmed summary (if any), plus counts for the acceptance / edit picture.</summary>
    private static async Task<IResult> Summaries(
        Guid id, HttpContext http, TenantContext tenant, ScopedTenantDbContextFactory dbFactory, CancellationToken ct)
    {
        if (!tenant.HasTenant) return Results.Unauthorized();
        if (!Has(http, Permission.CallsView)) return Results.Forbid();
        await using var db = dbFactory.Create();
        var all = await db.CallSummaries.AsNoTracking().Where(s => s.CallRecordId == id).OrderByDescending(s => s.CreatedAt).ToListAsync(ct);
        var confirmed = all.FirstOrDefault(s => s.Status == CallSummaryStatus.Confirmed);
        return Results.Ok(new
        {
            confirmed = confirmed is null ? null : new
            {
                confirmed.Id, confirmed.Summary, confirmed.ReasonForCall, confirmed.Outcome, confirmed.Disposition, confirmed.FollowUp,
                confirmed.Edited, confirmed.ReviewedByName, confirmed.ReviewedAt,
                ai = new { confirmed.AiSummary, confirmed.AiReasonForCall, confirmed.AiOutcome, confirmed.AiDisposition, confirmed.AiFollowUp, confirmed.AiConfidence, confirmed.Model },
            },
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
        if (!Has(http, Permission.CallsManage) || ActorResolver.Resolve(http.User) is not { } actor) return Results.Forbid();

        await using var db = dbFactory.Create();
        var row = await db.CallSummaries.FirstOrDefaultAsync(s => s.Id == summaryId && s.CallRecordId == id, ct);
        if (row is null) return Results.NotFound();
        if (!CallSummarizer.Outcomes.Contains(req.Outcome ?? "")) return Results.BadRequest(new { error = "Choose an outcome." });

        // The confirmed disposition must be one the script can record — the same rule the AI was held to.
        var disposition = string.IsNullOrWhiteSpace(req.Disposition) ? null : req.Disposition.Trim();
        var allowed = await DispositionCatalog.ForCallAsync(db, id, ct);
        if (disposition is not null && allowed.Count > 0 && !allowed.Contains(disposition, StringComparer.OrdinalIgnoreCase))
            return Results.BadRequest(new { error = $"'{disposition}' isn't one of this script's dispositions." });

        try
        {
            foreach (var previous in await db.CallSummaries.Where(s => s.CallRecordId == id && s.Status == CallSummaryStatus.Confirmed).ToListAsync(ct))
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
        var recorded = RecordedDisposition(await db.CallRecords.AsNoTracking().Where(r => r.Id == id).Select(r => r.CustomFields).FirstOrDefaultAsync(ct));
        if (disposition is not null && !string.Equals(disposition, recorded, StringComparison.Ordinal))
        {
            var field = (await customFields.GetFieldsForCallAsync(id, ct)).FirstOrDefault(f => f.Definition.FieldName == DispositionCatalog.FieldName);
            if (field is not null)
            {
                await customFields.SetValueAsync(id, field.Definition.Id, disposition, ct);
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
        if (!Has(http, Permission.CallsManage) || ActorResolver.Resolve(http.User) is not { } actor) return Results.Forbid();
        await using var db = dbFactory.Create();
        var row = await db.CallSummaries.FirstOrDefaultAsync(s => s.Id == summaryId && s.CallRecordId == id, ct);
        if (row is null) return Results.NotFound();
        try { row.Discard(actor.Id, actor.Name); }
        catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static string? RecordedDisposition(string? customFieldsJson) =>
        CommissionLedger.CustomFieldValues(customFieldsJson).GetValueOrDefault(DispositionCatalog.FieldName) is { Length: > 0 } d ? d : null;

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}

public record ConfirmSummaryRequest(string? Summary, string? ReasonForCall, string? Outcome, string? Disposition, string? FollowUp);
