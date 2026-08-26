namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// Resolves a client-certificate-configured HttpClient for the "mtls" auth type. Unlike every
/// other auth type here, mTLS identity is a property of the transport (the TLS handshake itself),
/// not something that can be attached to an individual HttpRequestMessage the way a header or
/// query param can — so a distinct certificate needs its own HttpClient (and, underneath, its own
/// connection pool), separate from the shared "FlowEngine" named client every other auth type
/// sends through. Implementations cache per (definitionId, certificate content) so a call doesn't
/// pay for rebuilding the TLS handshake machinery on every request, and so rotating the stored
/// certificate naturally produces a fresh client instead of silently reusing a stale one.
/// See API_HARDENING_CHECKLIST.md Tier 3.
/// </summary>
public interface IMtlsHttpClientProvider
{
    /// <summary>
    /// <paramref name="certificateBytes"/> is a PKCS#12 (.pfx) blob containing both the client
    /// certificate and its private key — the standard way to store a client cert as a single
    /// opaque credential-store value. Returns null if the bytes can't be loaded as a valid
    /// PKCS#12 certificate (wrong password, corrupt data); callers should treat that the same as
    /// any other "credential present but unusable" case and proceed on the default (non-mTLS)
    /// client — the vendor will simply reject the TLS handshake, surfacing as a normal connection
    /// failure through the existing error handling rather than a special case.
    /// </summary>
    HttpClient? GetClient(Guid definitionId, byte[] certificateBytes, string? certificatePassword);
}
