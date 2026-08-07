using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.Tts;

/// <summary>
/// Resolves the correct ITtsStreamProvider by ProviderKey.
/// Receives all registered ITtsStreamProvider implementations via DI enumeration.
/// </summary>
public class TtsStreamProviderFactory : ITtsStreamProviderFactory
{
    private readonly Dictionary<string, ITtsStreamProvider> _providers;

    public TtsStreamProviderFactory(IEnumerable<ITtsStreamProvider> providers) =>
        _providers = providers.ToDictionary(p => p.ProviderKey, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> RegisteredProviderKeys => _providers.Keys;

    public ITtsStreamProvider Resolve(string providerKey)
    {
        if (_providers.TryGetValue(providerKey, out var provider))
            return provider;

        throw new InvalidOperationException(
            $"No ITtsStreamProvider registered for key '{providerKey}'. " +
            $"Registered: {string.Join(", ", _providers.Keys)}");
    }
}
