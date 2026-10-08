using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Media;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Api.Endpoints;

public static class PortalMaintenanceEndpoints
{
    public static IEndpointRouteBuilder MapPortalMaintenanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/portal/maintenance")
            .RequireAuthorization("PlatformOwner");

        group.MapPost("migrate-tenants", MigrateTenants);

        // Location data for media attribution (S171, Phase B).
        group.MapGet("location-data", LocationData);
        group.MapPost("zip-codes", ImportZipCodes)
            .DisableAntiforgery()                                     // raw file body, not a form
            .WithMetadata(new RequestSizeLimitAttribute(200_000_000)); // the full CSV is ~25 MB

        return app;
    }

    private static async Task<IResult> MigrateTenants(
        ITenantProvisioningService provisioning,
        CancellationToken ct)
    {
        var result = await provisioning.MigrateAllTenantsAsync(ct);

        return result.Errors.Count == 0
            ? Results.Ok(new { result.Migrated, result.Errors })
            : Results.Json(new { result.Migrated, result.Errors }, statusCode: 207);
    }

    /// <summary>What's loaded: FCC stations (Worker, daily) and the zip/area-code tables (uploaded).</summary>
    private static async Task<IResult> LocationData(ContactConnectionDbContext db, CancellationToken ct) => Results.Ok(new
    {
        stations = await db.BroadcastStations.CountAsync(ct),
        stationsImportedAt = await db.BroadcastStations.MaxAsync(s => (DateTimeOffset?)s.ImportedAt, ct),
        zipCodes = await db.ZipCodes.CountAsync(ct),
        areaCodes = await db.AreaCodes.CountAsync(ct),
        zipCodesImportedAt = await db.ZipCodes.MaxAsync(z => (DateTimeOffset?)z.ImportedAt, ct),
    });

    /// <summary>Replaces the zip and area-code tables from the zip-codes.com Standard database — the
    /// request body is the .csv or the download .zip; <paramref name="fileName"/> says which.</summary>
    private static async Task<IResult> ImportZipCodes(string? fileName, HttpRequest request, ZipCodeImporter importer, CancellationToken ct)
    {
        try
        {
            var result = await importer.ImportAsync(request.Body, fileName ?? "upload.csv", ct);
            return Results.Ok(new { result.Zips, result.AreaCodes });
        }
        catch (InvalidDataException ex) { return Results.BadRequest(new { error = ex.Message }); }
    }
}
