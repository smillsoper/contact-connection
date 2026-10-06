using System.Text.RegularExpressions;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Exports;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// Key management for export vendors (S180). Generate an SSH key pair (we sign in to the vendor's SFTP with it — they
/// authorize the public key) or a PGP key pair (vendors encrypt files to us). The private key — and a PGP key's
/// passphrase — go straight into the tenant credential store (Key Vault, credential-audited); only the public key and
/// fingerprint are ever returned. Revoking deletes the private key from the store.
/// </summary>
public static partial class ExportKeysEndpoints
{
    public static IEndpointRouteBuilder MapExportKeysEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/export-keys").RequireAuthorization("ReportsManage");
        g.MapGet("", List);
        g.MapPost("", Generate);
        g.MapPost("{id:guid}/revoke", Revoke);
        return app;
    }

    private static async Task<IResult> List(ScopedTenantDbContextFactory dbf, TenantContext tc, CancellationToken ct)
    {
        if (!tc.HasTenant) return Results.Unauthorized();
        await using var db = dbf.Create();
        var keys = await db.ExportKeys.AsNoTracking().OrderBy(k => k.RevokedAt != null).ThenBy(k => k.Name).ToListAsync(ct);
        return Results.Ok(keys.Select(ToResponse));
    }

    private static async Task<IResult> Generate(
        GenerateKeyRequest req, ScopedTenantDbContextFactory dbf, ITenantCredentialStore store,
        [FromKeyedServices("tenant")] ICredentialAuditService audit, TenantContext tc, HttpContext http, CancellationToken ct)
    {
        if (tc.Current is not { } tenant) return Results.Unauthorized();
        if (ActorResolver.Resolve(http.User) is not { } actor) return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest(new { error = "Name the key (e.g. Cannella SFTP)." });
        if (!ExportKeyType.IsValid(req.Type)) return Results.BadRequest(new { error = "The key type must be ssh or pgp." });

        await using var db = dbf.Create();
        var name = req.Name.Trim();
        if (await db.ExportKeys.AnyAsync(k => k.Name == name && k.RevokedAt == null, ct))
            return Results.Conflict(new { error = $"There's already a key named '{name}'." });

        var slug = Slug().Replace(name.ToLowerInvariant(), "-").Trim('-');
        if (slug.Length > 40) slug = slug[..40].Trim('-');
        var credential = $"export-key-{req.Type}-{slug}-{Guid.NewGuid().ToString("N")[..6]}";
        var generated = req.Type == ExportKeyType.Ssh
            ? ExportKeyGenerator.Ssh($"contactconnection-{tenant.Subdomain}-{slug}")
            : ExportKeyGenerator.Pgp($"{tenant.Name} ({name}) <exports@{tenant.Subdomain}.contactconnection>");

        try
        {
            await store.SetAsync(credential, generated.PrivateKey, null, ct);
            await audit.RecordAsync(credential, CredentialAuditAction.Set, actor.Id, actor.Name, ct);
            if (generated.Passphrase is { } pass)
            {
                await store.SetAsync(credential + "-passphrase", pass, null, ct);
                await audit.RecordAsync(credential + "-passphrase", CredentialAuditAction.Set, actor.Id, actor.Name, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Results.Json(new { error = $"Couldn't store the private key in the credential store: {ex.Message}" }, statusCode: 502);
        }

        var key = ExportKey.Create(tenant.Id, name, req.Type, generated.PublicKey, generated.Fingerprint, credential,
            generated.Passphrase is null ? null : credential + "-passphrase", actor.Name);
        db.ExportKeys.Add(key);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/export-keys/{key.Id}", ToResponse(key));
    }

    private static async Task<IResult> Revoke(
        Guid id, ScopedTenantDbContextFactory dbf, ITenantCredentialStore store,
        [FromKeyedServices("tenant")] ICredentialAuditService audit, TenantContext tc, HttpContext http, CancellationToken ct)
    {
        if (!tc.HasTenant) return Results.Unauthorized();
        if (ActorResolver.Resolve(http.User) is not { } actor) return Results.Unauthorized();
        await using var db = dbf.Create();
        var key = await db.ExportKeys.FirstOrDefaultAsync(k => k.Id == id, ct);
        if (key is null) return Results.NotFound();
        if (key.RevokedAt is not null) return Results.Ok(ToResponse(key));

        // Don't pull a key out from under a delivery target that signs in with it.
        var inUse = (await db.ExportDefinitions.AsNoTracking().ToListAsync(ct))
            .Where(d => d.DeliveryTargets.Any(t => t.PrivateKeyCredential == key.PrivateKeyCredential))
            .Select(d => d.Name).ToList();
        if (inUse.Count > 0)
            return Results.Conflict(new { error = $"Still used by: {string.Join(", ", inUse)}. Change those delivery targets first." });

        foreach (var cred in new[] { key.PrivateKeyCredential, key.PassphraseCredential }.OfType<string>())
        {
            await store.DeleteAsync(cred, ct);
            await audit.RecordAsync(cred, CredentialAuditAction.Delete, actor.Id, actor.Name, ct);
        }
        key.Revoke(actor.Name);
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(key));
    }

    private static object ToResponse(ExportKey k) => new
    {
        k.Id, k.Name, k.Type, k.PublicKey, k.Fingerprint, k.PrivateKeyCredential, k.CreatedByName, k.CreatedAt, k.RevokedAt, k.RevokedByName,
    };

    public sealed record GenerateKeyRequest(string Name, string Type);

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex Slug();
}
