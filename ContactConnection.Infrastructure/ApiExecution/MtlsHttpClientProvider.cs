using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.ApiExecution;

/// <summary>
/// Default IMtlsHttpClientProvider — caches one HttpClient per (definitionId, certificate content
/// hash), keyed by definitionId in a ConcurrentDictionary. Registered as a singleton (see
/// ServiceCollectionExtensions) so the cache — and the TLS connection pools underneath each
/// cached client — persist for the process lifetime, not rebuilt per request/scope.
/// See API_HARDENING_CHECKLIST.md Tier 3.
/// </summary>
internal class MtlsHttpClientProvider : IMtlsHttpClientProvider, IDisposable
{
    private sealed record CacheEntry(string ContentHash, HttpClient Client);

    private readonly ConcurrentDictionary<Guid, CacheEntry> _cache = new();

    public HttpClient? GetClient(Guid definitionId, byte[] certificateBytes, string? certificatePassword)
    {
        var contentHash = Convert.ToHexStringLower(SHA256.HashData(certificateBytes));

        if (_cache.TryGetValue(definitionId, out var existing) && existing.ContentHash == contentHash)
            return existing.Client;

        X509Certificate2 cert;
        try
        {
            cert = string.IsNullOrEmpty(certificatePassword)
                ? X509CertificateLoader.LoadPkcs12(certificateBytes, password: null)
                : X509CertificateLoader.LoadPkcs12(certificateBytes, certificatePassword);
        }
        catch (CryptographicException)
        {
            // Bad password or corrupt PKCS#12 — caller proceeds without the client cert, same
            // "credential present but unusable" fallback every other auth type uses.
            return null;
        }

        var handler = new SocketsHttpHandler();
        handler.SslOptions.ClientCertificates = new X509CertificateCollection { cert };
        var client = new HttpClient(handler);

        var entry = new CacheEntry(contentHash, client);
        _cache.AddOrUpdate(definitionId, entry, (_, old) =>
        {
            // Reaching here means the TryGetValue check above already found the cached entry
            // stale (content hash changed) — dispose the outgoing client and its connection pool
            // rather than leaking it once nothing references it anymore.
            old.Client.Dispose();
            return entry;
        });

        return client;
    }

    public void Dispose()
    {
        foreach (var entry in _cache.Values)
            entry.Client.Dispose();
    }
}
