namespace ContactConnection.Infrastructure.Credentials;

/// <summary>
/// Which provider credential set the current operation uses (S179, script launch modes). Set around a payment, tax or
/// API call from the call record's <c>CredentialSet</c>; <see cref="ScopedCredentials"/> reads it so sandbox runs resolve
/// the campaign's sandbox keys (<c>{vendor}.sandbox:…</c>) and never the production ones. Flows across awaits
/// (AsyncLocal), so nothing between the caller and the credential lookup needs to pass it along.
/// </summary>
public static class CredentialSetScope
{
    private static readonly AsyncLocal<string?> Current = new();

    /// <summary>True inside a sandbox scope: resolve sandbox keys, and simulate a provider that has none.</summary>
    public static bool IsSandbox => Current.Value == "sandbox";

    public static IDisposable Use(string? credentialSet)
    {
        var previous = Current.Value;
        Current.Value = credentialSet;
        return new Restore(previous);
    }

    /// <summary>The vendor key for the current set: "AuthorizeNet" or, in a sandbox scope, "AuthorizeNet.sandbox".</summary>
    public static string VendorKey(string vendor) => IsSandbox ? $"{vendor}.sandbox" : vendor;

    private sealed class Restore(string? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
