namespace ContactConnection.Application.Interfaces.Services;

/// <summary>One credential a vendor integration needs (S169 — campaign settings credential sections). Stored
/// in the tenant credential store as {CredentialVendor}:{campaignId|clientId}:{Name}, or
/// {CredentialVendor}:{Name} tenant-wide (see ScopedCredentials).</summary>
public record CredentialField(
    string Name, string Label, bool Secret, IReadOnlyList<string>? Options = null, string? Default = null, string? Help = null);

/// <summary>How a vendor's credentials are named and entered — drives the campaign settings sections, so a new
/// payment gateway / tax provider shows up there by registering its implementation.</summary>
public record CredentialDescriptor(
    string ProviderKey, string DisplayName, string CredentialVendor, IReadOnlyList<CredentialField> Fields, string Instructions);

/// <summary>Result of checking a campaign's credentials with the vendor — nothing is charged or recorded.</summary>
public record CredentialTestResult(bool Succeeded, string Message, string? Environment);

/// <summary>
/// A vendor integration whose credentials are entered per campaign (campaign → client → tenant cascade).
/// Implemented by payment gateways (IPaymentGatewayClient) and credentialed tax providers (e.g. Avalara).
/// </summary>
public interface ICampaignCredentialSet
{
    CredentialDescriptor Descriptor { get; }

    /// <summary>Checks the credentials that would be used for this campaign against the vendor, without
    /// charging or recording anything.</summary>
    Task<CredentialTestResult> TestCredentialsAsync(Guid campaignId, Guid clientId, CancellationToken ct = default);
}
