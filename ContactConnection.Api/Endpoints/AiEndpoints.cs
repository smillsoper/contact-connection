using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Ai;
using ContactConnection.Infrastructure.Data;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// AI features (AI learning track, S171). Step 1: preview exactly what an AI call summary would send
/// to the model — the redacted context — without calling any AI. Seeing what the model sees is the
/// first thing to get right.
/// </summary>
public static class AiEndpoints
{
    public static IEndpointRouteBuilder MapAiEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/call-review/calls/{id:guid}/ai/context", PreviewContext).RequireAuthorization();
        app.MapPost("/api/v1/call-review/calls/{id:guid}/ai/summary", GenerateSummary).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> PreviewContext(
        Guid id, HttpContext http, TenantContext tenant, ScopedTenantDbContextFactory dbFactory, CancellationToken ct)
    {
        if (!tenant.HasTenant) return Results.Unauthorized();
        if (!(http.User.FindFirst("permissions")?.Value ?? "").Split(',').Contains(Permission.CallsView)) return Results.Forbid();

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

    // Step 2: ask the model for a summary. Suggest-only — nothing is saved; the result and its real token
    // usage come back for review. A failed AI call is a 503 with a plain message, never a broken page.
    private static async Task<IResult> GenerateSummary(
        Guid id, HttpContext http, TenantContext tenant, ScopedTenantDbContextFactory dbFactory,
        CallSummarizer summarizer, AnthropicClient client, CancellationToken ct)
    {
        if (!tenant.HasTenant) return Results.Unauthorized();
        if (!(http.User.FindFirst("permissions")?.Value ?? "").Split(',').Contains(Permission.CallsView)) return Results.Forbid();
        if (!client.IsConfigured) return Results.Json(new { error = "AI isn't configured on this server." }, statusCode: 503);

        await using var db = dbFactory.Create();
        try
        {
            var result = await summarizer.SummarizeAsync(db, id, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }
        catch (AnthropicClient.AiUnavailableException ex)
        {
            return Results.Json(new { error = $"Summary unavailable — {ex.Message}" }, statusCode: 503);
        }
    }
}
