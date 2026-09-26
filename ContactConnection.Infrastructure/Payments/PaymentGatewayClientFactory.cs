using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.Payments;

/// <summary>
/// Resolves the correct IPaymentGatewayClient by ProviderKey. Mirrors TaxProviderFactory's
/// dispatch-dictionary shape, except this throws on an unrecognized/unconfigured key rather than
/// silently falling back to a default — a wrong gateway silently used for a real charge is a much
/// worse failure mode than tax defaulting to flat-rate.
/// </summary>
public class PaymentGatewayClientFactory : IPaymentGatewayClientFactory
{
    private readonly Dictionary<string, IPaymentGatewayClient> _clients;

    public PaymentGatewayClientFactory(IEnumerable<IPaymentGatewayClient> clients)
    {
        _clients = clients.ToDictionary(c => c.ProviderKey, StringComparer.OrdinalIgnoreCase);
    }

    public IPaymentGatewayClient Resolve(string providerKey)
    {
        if (_clients.TryGetValue(providerKey, out var client))
            return client;

        throw new InvalidOperationException(
            $"No payment gateway client registered for provider '{providerKey}'.");
    }
}
