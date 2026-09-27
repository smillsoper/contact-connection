using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.ApiExecution;

namespace ContactConnection.Api.Endpoints;

/// <summary>Save-time checks for an endpoint's body mode, Liquid template and success criteria —
/// shared by the tenant and portal endpoint APIs so a broken template is rejected when it's saved
/// (with the parser's line/column), not discovered mid-call.</summary>
internal static class EndpointBodyValidation
{
    public static string? Validate(ILiquidTemplateRenderer liquid, string bodyTemplateType, string? template, string? successCriteria)
    {
        if (!BodyTemplateType.IsValid(bodyTemplateType))
            return $"Unknown body template type '{bodyTemplateType}' (use 'simple' or 'liquid').";
        if (bodyTemplateType == BodyTemplateType.Liquid && !string.IsNullOrWhiteSpace(template)
            && liquid.Validate(template) is { } parseError)
            return $"Liquid template error: {parseError}";
        return ResponseSuccessEvaluator.Validate(successCriteria);
    }
}
