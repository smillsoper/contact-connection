using ContactConnection.Domain.ValueObjects.Exports;

namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// Renders an export file from an <see cref="ExportSpec"/> (S180, Export Worker) — the same engine for the editor's Preview
/// (in the API, capped at a few calls) and for real / test runs (in the Worker, streamed to blob storage).
/// </summary>
public interface IExportGenerator
{
    /// <summary>First problem with the spec (bad template, missing width…), or null when it's usable.</summary>
    string? Validate(ExportSpec spec);

    /// <summary>The file name for this generation (test suffix applied for a test file).</summary>
    string RenderFileName(ExportGenerationRequest request);

    string ContentType(ExportSpec spec);

    /// <summary>Writes the file to <paramref name="output"/>. A template error stops generation and comes back in
    /// <see cref="ExportGenerationResult.Error"/> with the row it happened on.</summary>
    Task<ExportGenerationResult> GenerateAsync(ExportGenerationRequest request, Stream output, CancellationToken ct = default);
}

/// <param name="DataSource"><c>production</c> or <c>practice</c> (training + sandbox calls).</param>
/// <param name="MaxCalls">Preview: stop after this many calls.</param>
public sealed record ExportGenerationRequest(
    ExportSpec Spec, string ExportName, Guid? RunId, bool IsTest, string DataSource,
    DateTimeOffset WindowStart, DateTimeOffset WindowEnd, int? MaxCalls = null);

public sealed record ExportGenerationResult(bool Success, int RowCount, int CallCount, bool Truncated, string? Error);
