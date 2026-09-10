namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// Symmetric authenticated encryption for the time-bounded PCI sensitive-data blob stored on
/// <c>call_records.sensitive_data</c> (captured card number / CVV / SSN from a tf_secure_collect
/// node — see ARCHITECTURE.md §24). AES-256-GCM with a single platform master key held outside
/// source/config (Key Vault in prod, user-secrets locally, same as <c>Jwt:SigningKey</c>).
///
/// This is the only component that ever holds the master key; callers move ciphertext strings
/// around and decrypt only in the narrow window they need the plaintext (e.g. merging a second
/// tf_secure_collect capture into an existing blob, or a tokenization api_call).
/// </summary>
public interface ISensitiveDataProtector
{
    /// <summary>True when a usable master key is configured. When false, <see cref="Protect"/> /
    /// <see cref="Unprotect"/> throw — a tf_secure_collect flow can check this up front and take
    /// its <c>failed</c> path rather than losing a capture.</summary>
    bool IsConfigured { get; }

    /// <summary>AES-256-GCM encrypts <paramref name="plaintext"/>; returns a self-describing
    /// base64 token (version + nonce + tag + ciphertext). Throws if no master key is configured.</summary>
    string Protect(string plaintext);

    /// <summary>Reverses <see cref="Protect"/>. Throws on a tampered/corrupt token or a wrong key.</summary>
    string Unprotect(string token);
}
