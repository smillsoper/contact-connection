using System.Text.Json.Nodes;

namespace ContactConnection.Infrastructure.ApiExecution;

/// <summary>
/// A tenant API definition's sandbox environment (S179, launch modes) — the client's test system, e.g. Life Seasons
/// approves a new campaign only after its orders run through their sandbox. Same auth type and settings as production,
/// but its own base URL (and optional OAuth2 token URL), each endpoint's optional sandbox path, and its own credential
/// values stored under a parallel key: <c>{key}.sandbox</c>. A sandbox call never falls back to a production credential
/// and has its own circuit breaker / rate-limit / mTLS identity, so sandbox failures can't trip production.
/// Shared by the api_call node and the endpoint editor's Test button.
/// </summary>
public static class SandboxEnvironment
{
    public const string CredentialSuffix = ".sandbox";

    // Auth settings that may legitimately be blank (AWS session token, an unencrypted client certificate).
    private static readonly HashSet<string> OptionalKeys = ["sessionTokenKey", "certPasswordKey"];

    public static string CredentialKey(string key) => key + CredentialSuffix;

    /// <summary>Wraps a credential lookup so every key resolves its sandbox value — never the production one.</summary>
    public static Func<string, CancellationToken, Task<string?>> Credentials(Func<string, CancellationToken, Task<string?>> get) =>
        (key, ct) => string.IsNullOrEmpty(key) ? Task.FromResult<string?>(null) : get(CredentialKey(key), ct);

    /// <summary>A stable id distinct from the definition's, for the sandbox's circuit breaker, rate limit and mTLS client.</summary>
    public static Guid DefinitionId(Guid definitionId)
    {
        var bytes = definitionId.ToByteArray();
        bytes[14] ^= 0x5A;
        bytes[15] ^= 0xA5;
        return new Guid(bytes);
    }

    /// <summary>The production auth config with the sandbox OAuth2 token URL swapped in (when one is set).</summary>
    public static string AuthConfig(string authConfigJson, string? sandboxTokenUrl)
    {
        if (string.IsNullOrWhiteSpace(sandboxTokenUrl)) return authConfigJson;
        try
        {
            if (JsonNode.Parse(authConfigJson) is not JsonObject root || !root.ContainsKey("tokenUrl")) return authConfigJson;
            root["tokenUrl"] = sandboxTokenUrl.Trim();
            return root.ToJsonString();
        }
        catch { return authConfigJson; }
    }

    /// <summary>The credential key names an auth config reads (credentialKey, tokenKey, clientIdKey, …).</summary>
    public static IReadOnlyList<(string Setting, string Key)> CredentialKeys(string authConfigJson)
    {
        var keys = new List<(string, string)>();
        try
        {
            if (JsonNode.Parse(authConfigJson) is not JsonObject root) return keys;
            foreach (var (name, value) in root)
                if (name.EndsWith("Key", StringComparison.Ordinal) && value is JsonValue v
                    && v.TryGetValue<string>(out var key) && !string.IsNullOrWhiteSpace(key))
                    keys.Add((name, key.Trim()));
        }
        catch { /* unparseable auth config — the executor reports it */ }
        return keys;
    }

    /// <summary>The first required sandbox credential that isn't set (its full key name), or null when all are.</summary>
    public static async Task<string?> FirstMissingCredentialAsync(
        string authConfigJson, Func<string, CancellationToken, Task<string?>> get, CancellationToken ct)
    {
        foreach (var (setting, key) in CredentialKeys(authConfigJson))
        {
            if (OptionalKeys.Contains(setting)) continue;
            if (await get(CredentialKey(key), ct) is null) return CredentialKey(key);
        }
        return null;
    }

    public static string MissingCredentialError(string key) =>
        $"Sandbox credential '{key}' is not set — enter it in the API definition's Sandbox environment section. " +
        "A sandbox call never uses production credentials.";
}
