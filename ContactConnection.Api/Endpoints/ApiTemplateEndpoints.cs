using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.ApiExecution;
using ContactConnection.Infrastructure.FlowEngine.NodeHandlers;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// Authoring support for Liquid request bodies in the API Definition builder (tenant and portal):
/// the sample data a template can reference, and a render-only preview that sends nothing.
/// </summary>
public static class ApiTemplateEndpoints
{
    public static IEndpointRouteBuilder MapApiTemplateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/api-templates").RequireAuthorization();
        group.MapGet("sample-model", GetSampleModel);
        group.MapPost("preview", Preview);
        return app;
    }

    // ── GET /api/v1/api-templates/sample-model ──────────────────────────────
    // Same shape a live api_call builds (ApiTemplateModelBuilder), filled with sample values.
    private static IResult GetSampleModel() => Results.Ok(ApiTemplateModelBuilder.Sample());

    // ── POST /api/v1/api-templates/preview ──────────────────────────────────
    private static async Task<IResult> Preview(
        PreviewApiTemplateRequest req, ILiquidTemplateRenderer liquid, CancellationToken ct)
    {
        var model = req.Model ?? ApiTemplateModelBuilder.Sample();
        var rendered = await liquid.RenderAsync(req.Template ?? "", model, ct);
        if (!rendered.Success)
            return Results.Ok(new PreviewApiTemplateResponse(false, null, rendered.Error));

        var jsonError = req.ExpectJson
            ? ApiCallNodeHandler.LiquidJsonCheck(rendered.Output, new() { ["Content-Type"] = "application/json" })
            : null;
        var pretty = rendered.Output;
        if (jsonError is null && req.ExpectJson && !string.IsNullOrWhiteSpace(pretty))
            pretty = JsonNode.Parse(pretty)!.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        return Results.Ok(new PreviewApiTemplateResponse(jsonError is null, pretty, jsonError));
    }
}

public record PreviewApiTemplateRequest(string? Template, JsonObject? Model = null, bool ExpectJson = true);
public record PreviewApiTemplateResponse(bool Success, string? Output, string? Error);
