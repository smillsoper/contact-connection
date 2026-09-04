using System.Security.Cryptography;
using System.Text;
using ContactConnection.Application.Interfaces.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Tts;

/// <summary>
/// Synthesizes text via the tenant's configured ITtsStreamProvider into a cached WAV file on the
/// shared sounds volume, for the two call sites that can't use the live mod_audio_stream path
/// (ITtsStreamingService) — tf_voicemail's greeting and tf_transfer's external_number
/// announcement — because their remaining work runs inside a single FreeSWITCH dialplan
/// uuid_transfer with no live ESL/event hook mid-execution. The result is just another file arg,
/// same shape TelephonyAudioResolver returns for an uploaded audio file.
///
/// Cached by content hash (provider + voice + text) under {SoundsHostPath}/{schema}/_tts_cache/ —
/// an unchanged greeting/announcement is synthesized once, not re-billed to the vendor on every
/// call that plays it. Host-path resolution mirrors AudioFilesEndpoints.ResolveSoundsHostPath
/// (relative to the current working directory, since this runs inside the same Api process).
/// </summary>
public sealed class TtsFileSynthesizer : ITtsFileSynthesizer
{
    private readonly ITtsStreamProviderFactory _providerFactory;
    private readonly ITenantCredentialStore _credentialStore;
    private readonly IConfiguration _config;
    private readonly ILogger<TtsFileSynthesizer> _logger;

    public TtsFileSynthesizer(
        ITtsStreamProviderFactory providerFactory,
        ITenantCredentialStore credentialStore,
        IConfiguration config,
        ILogger<TtsFileSynthesizer> logger)
    {
        _providerFactory = providerFactory;
        _credentialStore = credentialStore;
        _config          = config;
        _logger          = logger;
    }

    public async Task<string?> SynthesizeToFileAsync(
        string tenantSchemaName, string tenantSubdomain,
        string providerKey, string? providerSettingsJson, string voiceId, string text,
        CancellationToken ct = default)
    {
        ITtsStreamProvider provider;
        try
        {
            provider = _providerFactory.Resolve(providerKey);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "TtsFileSynthesizer: no provider registered for key {Provider}", providerKey);
            return null;
        }

        var hostDir = ResolveCacheHostDir(tenantSchemaName);
        var containerDir = ResolveCacheContainerDir(tenantSchemaName);
        var hash = ComputeCacheKey(providerKey, voiceId, text);
        var fileName = $"{hash}.wav";
        var hostPath = Path.Combine(hostDir, fileName);
        var containerArg = $"{containerDir}/{fileName}";

        if (File.Exists(hostPath))
            return containerArg;

        var credentials = new Dictionary<string, string>();
        foreach (var field in provider.RequiredCredentialFields)
        {
            var value = await _credentialStore.GetForTenantAsync(
                tenantSubdomain, TtsCredentialKeys.For(providerKey, field), ct);
            if (value is null)
            {
                _logger.LogWarning(
                    "TtsFileSynthesizer: tenant {Tenant} has no '{Field}' credential configured for provider {Provider}",
                    tenantSubdomain, field, providerKey);
                return null;
            }
            credentials[field] = value;
        }

        Dictionary<string, string>? providerSettings = null;
        if (!string.IsNullOrWhiteSpace(providerSettingsJson))
        {
            try
            {
                providerSettings = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(providerSettingsJson);
            }
            catch (System.Text.Json.JsonException ex)
            {
                _logger.LogWarning(ex, "TtsFileSynthesizer: malformed provider settings JSON for {Provider} — ignoring", providerKey);
            }
        }

        var request = new TtsStreamRequest(text, voiceId, credentials, PreferredSampleRateHz: 8000, providerSettings);

        using var pcm = new MemoryStream();
        int sampleRateHz = 8000;
        var gotAudio = false;
        try
        {
            await foreach (var chunk in provider.SynthesizeAsync(request, ct))
            {
                if (!gotAudio) { sampleRateHz = chunk.SampleRateHz; gotAudio = true; }
                pcm.Write(chunk.Data.Span);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "TtsFileSynthesizer: synthesis failed for tenant {Tenant} provider {Provider}",
                tenantSubdomain, providerKey);
            return null;
        }

        if (!gotAudio || pcm.Length == 0)
        {
            _logger.LogWarning("TtsFileSynthesizer: provider {Provider} returned no audio for tenant {Tenant}",
                providerKey, tenantSubdomain);
            return null;
        }

        try
        {
            Directory.CreateDirectory(hostDir);
            WriteWavFile(hostPath, pcm.GetBuffer().AsSpan(0, (int)pcm.Length), sampleRateHz);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TtsFileSynthesizer: failed to write cached WAV {Path}", hostPath);
            return null;
        }

        _logger.LogInformation(
            "TtsFileSynthesizer: synthesized + cached {Path} (provider={Provider} voice={Voice} {Bytes} bytes @ {Rate}Hz)",
            hostPath, providerKey, voiceId, pcm.Length, sampleRateHz);

        return containerArg;
    }

    private static string ComputeCacheKey(string providerKey, string voiceId, string text)
    {
        var raw = $"{providerKey}|{voiceId}|{text}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private string ResolveCacheHostDir(string tenantSchemaName)
    {
        var configured = _config["FreeSWITCH:SoundsHostPath"];
        var basePath = !string.IsNullOrEmpty(configured)
            ? configured
            : Path.Combine("..", "freeswitch", "sounds");
        return Path.Combine(Path.GetFullPath(basePath), tenantSchemaName, "_tts_cache");
    }

    private string ResolveCacheContainerDir(string tenantSchemaName)
    {
        var containerBase = _config["FreeSWITCH:SoundsContainerPath"]
            ?? "/usr/share/freeswitch/sounds/contactconnection";
        return $"{containerBase}/{tenantSchemaName}/_tts_cache";
    }

    /// <summary>Writes 16-bit mono linear PCM as a standard 44-byte-header WAV file.</summary>
    private static void WriteWavFile(string path, ReadOnlySpan<byte> pcm, int sampleRateHz)
    {
        const int channels = 1;
        const int bitsPerSample = 16;
        var byteRate = sampleRateHz * channels * bitsPerSample / 8;
        var blockAlign = (short)(channels * bitsPerSample / 8);

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var w = new BinaryWriter(fs);

        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + pcm.Length);
        w.Write(Encoding.ASCII.GetBytes("WAVE"));

        w.Write(Encoding.ASCII.GetBytes("fmt "));
        w.Write(16);                       // fmt chunk size
        w.Write((short)1);                 // PCM
        w.Write((short)channels);
        w.Write(sampleRateHz);
        w.Write(byteRate);
        w.Write(blockAlign);
        w.Write((short)bitsPerSample);

        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write(pcm.Length);
        w.Write(pcm);
    }
}
