using System.Text.Json;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Tts;

/// <summary>
/// Shared implementation of <see cref="ITtsStreamingService"/> — extracted from PlayNodeHandler
/// (the original, single-node home of this logic) so tf_transfer's live-broadcast destinations
/// can offer the same vendor-streaming voice without duplicating the provider lookup / relay
/// wiring a second time.
/// </summary>
public sealed class TtsStreamingService : ITtsStreamingService
{
    private static readonly JsonSerializerOptions RelayJsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ITenantDbContextFactory _factory;
    private readonly IPortalApiEndpointRepository _portalEndpointRepo;
    private readonly IPortalApiDefinitionRepository _portalDefRepo;
    private readonly ITelephonyCallSessionStore _sessionStore;
    private readonly IConfiguration _config;
    private readonly ILogger<TtsStreamingService> _logger;

    public TtsStreamingService(
        ITenantDbContextFactory factory,
        IPortalApiEndpointRepository portalEndpointRepo,
        IPortalApiDefinitionRepository portalDefRepo,
        ITelephonyCallSessionStore sessionStore,
        IConfiguration config,
        ILogger<TtsStreamingService> logger)
    {
        _factory            = factory;
        _portalEndpointRepo = portalEndpointRepo;
        _portalDefRepo      = portalDefRepo;
        _sessionStore       = sessionStore;
        _config             = config;
        _logger             = logger;
    }

    /// <summary>
    /// Queried directly against TenantDbContext rather than ITenantApiPreferenceRepository/
    /// ITenantApiEndpointRepository/ITenantApiDefinitionRepository — those resolve the tenant via
    /// ambient TenantContext, which doesn't exist here (this runs from EslBackgroundService, a
    /// background service with no HTTP request). Portal-side lookups still go through the
    /// injected repositories since those are public-schema and have no such ambient-context
    /// dependency.
    /// </summary>
    public async Task<TtsStreamingProviderInfo?> ResolveProviderAsync(string tenantSchemaName, CancellationToken ct = default)
    {
        await using var db = _factory.Create(tenantSchemaName);
        var preference = await db.TenantApiPreferences
            .FirstOrDefaultAsync(p => p.ApiSubType == ApiSubType.TtsStreaming, ct);
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
                "Tenant {Schema}'s tts_streaming preference (source={Source}) has no resolvable Provider — falling back to flite",
                tenantSchemaName, preference.Source);
            return null;
        }

        return new TtsStreamingProviderInfo(provider, preference.SettingsJson);
    }

    public async Task StartStreamAsync(
        TelephonyFlowContext ctx, TtsStreamingProviderInfo provider, string text, string voiceId,
        CancellationToken ct = default)
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
                    "TtsStreamingService [{Uuid}]: malformed TTS settings JSON for provider {Provider} — ignoring",
                    ctx.ChannelUuid, provider.ProviderKey);
            }
        }

        var relayRequest = new TtsStreamRelayRequest(
            ctx.ChannelUuid,
            ctx.TenantSubdomain,
            provider.ProviderKey,
            voiceId,
            text.Replace("\n", " "),
            PreferredSampleRateHz: 8000,
            providerSettings);

        var token = Guid.NewGuid().ToString("N");
        await _sessionStore.SetKeyAsync(
            $"tts_relay:{token}", JsonSerializer.Serialize(relayRequest, RelayJsonOpts), TimeSpan.FromSeconds(30), ct);

        var wssUrl = _config["FreeSWITCH:TtsRelayWsUrl"] ?? "ws://host.docker.internal:5135/relay/tts-stream";

        _logger.LogInformation(
            "TtsStreamingService [{Uuid}]: streaming TTS via provider={Provider} voice={Voice}",
            ctx.ChannelUuid, provider.ProviderKey, voiceId);

        await ctx.Esl!.StartAudioStreamAsync(ctx.ChannelUuid, wssUrl, "mono", "8k", token, ct);
    }
}
