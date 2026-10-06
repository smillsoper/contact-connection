using ContactConnection.Domain.ValueObjects.Exports;

namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// Sends export files (S180, Export Worker session 2): SFTP (pinned host key), FTPS (valid or pinned certificate), email,
/// each optionally PGP- or zip-encrypted first. Secrets come from the tenant credential store by name and never leave
/// this service.
/// </summary>
public interface IExportDeliveryService
{
    /// <summary>Sends the file; returns where it went (remote path / recipients). Throws <see cref="ExportDeliveryException"/>
    /// (Permanent when retrying can't help — no pinned key, missing credential).</summary>
    Task<string> DeliverAsync(ExportDeliveryRequest request, CancellationToken ct = default);

    /// <summary>Signs in (SFTP / FTPS) and reports the server's fingerprint — what to pin — without uploading anything.
    /// Email: checks the addresses only.</summary>
    Task<ExportConnectionTest> TestAsync(string tenantSubdomain, ExportDeliveryTarget target, CancellationToken ct = default);
}

/// <param name="LocalPath">The generated file on local disk.</param>
/// <param name="EmailModel">Values for the email subject's Liquid (export name, file name…).</param>
public sealed record ExportDeliveryRequest(
    string TenantSubdomain, ExportDeliveryTarget Target, string LocalPath, string FileName, string ContentType,
    IReadOnlyDictionary<string, object?> EmailModel, string TimeZone);

/// <param name="Fingerprint">SFTP: SHA-256 host-key fingerprint. FTPS: SHA-256 certificate fingerprint.</param>
/// <param name="MatchesPinned">Whether it matches the fingerprint already saved on the target (null when none is saved).</param>
public sealed record ExportConnectionTest(bool Success, string Message, string? Fingerprint, bool? MatchesPinned);

public sealed class ExportDeliveryException(string message, bool permanent = false, Exception? inner = null)
    : Exception(message, inner)
{
    public bool Permanent { get; } = permanent;
}
