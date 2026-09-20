using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.Stt;

/// <summary>Resolves the correct ISpeechRecognitionProvider by ProviderKey. Mirrors
/// TtsStreamProviderFactory exactly.</summary>
public class SpeechRecognitionProviderFactory : ISpeechRecognitionProviderFactory
{
    private readonly Dictionary<string, ISpeechRecognitionProvider> _providers;

    public SpeechRecognitionProviderFactory(IEnumerable<ISpeechRecognitionProvider> providers) =>
        _providers = providers.ToDictionary(p => p.ProviderKey, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> RegisteredProviderKeys => _providers.Keys;

    public ISpeechRecognitionProvider Resolve(string providerKey)
    {
        if (_providers.TryGetValue(providerKey, out var provider))
            return provider;

        throw new InvalidOperationException(
            $"No ISpeechRecognitionProvider registered for key '{providerKey}'. " +
            $"Registered: {string.Join(", ", _providers.Keys)}");
    }
}
