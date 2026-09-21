using System.Text.Json;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Stt;

/// <summary>Shared implementation of ISttStreamingService — mirrors TtsStreamingService exactly,
/// swapping ApiSubType.TtsStreaming for ApiSubType.SttStreaming.</summary>
public sealed class SttStreamingService : ISttStreamingService
{
    private static readonly JsonSerializerOptions RelayJsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ITenantDbContextFactory _factory;
    private readonly IPortalApiEndpointRepository _portalEndpointRepo;
    private readonly IPortalApiDefinitionRepository _portalDefRepo;
    private readonly ITelephonyCallSessionStore _sessionStore;
    private readonly ILogger<SttStreamingService> _logger;

    public SttStreamingService(
        ITenantDbContextFactory factory,
        IPortalApiEndpointRepository portalEndpointRepo,
        IPortalApiDefinitionRepository portalDefRepo,
        ITelephonyCallSessionStore sessionStore,
        ILogger<SttStreamingService> logger)
    {
        _factory            = factory;
        _portalEndpointRepo = portalEndpointRepo;
        _portalDefRepo      = portalDefRepo;
        _sessionStore       = sessionStore;
        _logger             = logger;
    }

    /// <summary>Queried directly against TenantDbContext rather than the tenant-preference
    /// repositories — those resolve via ambient TenantContext, which doesn't exist here (this
    /// runs from IvrMenuNodeHandler, off a telephony flow with no HTTP request). Same reasoning
    /// as TtsStreamingService.ResolveProviderAsync.</summary>
    public async Task<SttStreamingProviderInfo?> ResolveProviderAsync(string tenantSchemaName, CancellationToken ct = default)
    {
        await using var db = _factory.Create(tenantSchemaName);
        var preference = await db.TenantApiPreferences
            .FirstOrDefaultAsync(p => p.ApiSubType == ApiSubType.SttStreaming, ct);
        if (preference is null) return null;

        string? provider;
        if (preference.Source == ApiPreferenceSource.Tenant)
        {
            var endpoint = await db.TenantApiEndpoints.FirstOrDefaultAsync(e => e.Id == preference.EndpointId, ct);
            var definition = endpoint is null ? null
                : await db.TenantApiDefinitions.FirstOrDefaultAsync(d => d.Id == endpoint.DefinitionId, ct);
            provider = definition?.Provider;
        }
        else
        {
            var endpoint = await _portalEndpointRepo.GetByIdAsync(preference.EndpointId, ct);
            var definition = endpoint is null ? null
                : await _portalDefRepo.GetByIdAsync(endpoint.DefinitionId, ct);
            provider = definition?.Provider;
        }

        if (string.IsNullOrWhiteSpace(provider))
        {
            _logger.LogWarning(
                "Tenant {Schema}'s stt_streaming preference (source={Source}) has no resolvable Provider — falling back to DTMF-only",
                tenantSchemaName, preference.Source);
            return null;
        }

        return new SttStreamingProviderInfo(provider, preference.SettingsJson);
    }

    public async Task<string> PrepareCaptureAsync(
        string channelUuid, string tenantSubdomain, SttStreamingProviderInfo provider,
        IReadOnlyDictionary<string, string> phraseIndex, IReadOnlyDictionary<string, string> optionMap,
        string? noMatchTarget, int timeoutMs, int sampleRateHz, bool freeForm = false, CancellationToken ct = default)
    {
        Dictionary<string, string>? providerSettings = null;
        if (!string.IsNullOrWhiteSpace(provider.SettingsJson))
        {
            try
            {
                providerSettings = JsonSerializer.Deserialize<Dictionary<string, string>>(provider.SettingsJson);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex,
                    "SttStreamingService [{Uuid}]: malformed STT settings JSON for provider {Provider} — ignoring",
                    channelUuid, provider.ProviderKey);
            }
        }

        var relayRequest = new SttStreamRelayRequest(
            channelUuid,
            tenantSubdomain,
            provider.ProviderKey,
            phraseIndex,
            optionMap,
            noMatchTarget,
            timeoutMs,
            providerSettings,
            sampleRateHz,
            freeForm);

        var token = Guid.NewGuid().ToString("N");
        await _sessionStore.SetKeyAsync(
            $"stt_relay:{token}", JsonSerializer.Serialize(relayRequest, RelayJsonOpts), TimeSpan.FromSeconds(60), ct);

        _logger.LogInformation(
            "SttStreamingService [{Uuid}]: prepared voice-recognition capture via provider={Provider} token={Token}",
            channelUuid, provider.ProviderKey, token);

        return token;
    }
}
