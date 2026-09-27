using System.Text.Json.Nodes;

namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// Renders Liquid templates (https://shopify.github.io/liquid/) — the opt-in request-body mode for
/// API Definition endpoints whose payloads need loops, conditionals or math (e.g. an order API's
/// line items), which the plain {{namespace.field}} substitution can't express. Sandboxed: a
/// template can only read the model it's given (no code execution, no file/network access) and
/// is bounded by a step limit.
///
/// Output is NOT HTML-encoded (bodies are JSON/XML/text, not HTML). Templates should use the
/// <c>json</c> filter for string values — <c>{{ caller.first_name | json }}</c> emits a correctly
/// quoted and escaped JSON string — and <c>money</c> for fixed two-decimal amounts.
/// </summary>
public interface ILiquidTemplateRenderer
{
    /// <summary>Parses the template without rendering it. Null when valid; otherwise the parse
    /// error (with line/column) — used to reject a broken template when an endpoint is saved.</summary>
    string? Validate(string template);

    /// <summary>Renders the template against <paramref name="model"/> (its top-level properties
    /// become the template's variables). Never throws for template problems — returns a failed
    /// result with the error instead.</summary>
    Task<LiquidRenderResult> RenderAsync(string template, JsonObject model, CancellationToken ct = default);
}

public record LiquidRenderResult(bool Success, string? Output, string? Error);
