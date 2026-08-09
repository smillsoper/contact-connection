using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ContactConnection.Api.Endpoints;

public record TestAuthRequest(string AuthConfig);

internal static class AuthTestHelper
{
    public static async Task<IResult> RunAuthTest(
        string authConfigJson,
        Func<string, CancellationToken, Task<string?>> getCredential,
        IHttpClientFactory httpFactory,
        CancellationToken ct)
    {
        JsonElement root;
        try { root = JsonDocument.Parse(authConfigJson).RootElement; }
        catch { return Results.BadRequest(new { error = "Invalid auth config JSON." }); }

        var type = root.TryGetProperty("type", out var typeProp)
            ? typeProp.GetString() ?? "none"
            : "none";

        return type switch
        {
            "oauth2" => await TestOAuth2(root, getCredential, httpFactory, ct),
            "api_key" => await TestCredentialKeys(type, getCredential, ct,
                Str(root, "credentialKey")),
            "basic" => await TestCredentialKeys(type, getCredential, ct,
                Str(root, "usernameKey"), Str(root, "passwordKey")),
            "bearer" => await TestCredentialKeys(type, getCredential, ct,
                Str(root, "tokenKey")),
            "hmac" => await TestCredentialKeys(type, getCredential, ct,
                Str(root, "secretKey")),
            _ => Results.Ok(new { type, success = true, message = "No authentication configured." })
        };
    }

    private static string Str(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) ? v.GetString() ?? "" : "";

    /// <summary>Shows just enough of a token to confirm it's the right one during debugging,
    /// never enough to be usable. Applied unconditionally (not just for long tokens) — a short
    /// token deserves the same treatment as a long one.</summary>
    private static string RedactToken(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        var visible = Math.Min(6, raw.Length);
        return raw.Length <= visible ? "***redacted***" : $"{raw[..visible]}…***redacted***";
    }

    private static async Task<IResult> TestCredentialKeys(
        string type,
        Func<string, CancellationToken, Task<string?>> getCredential,
        CancellationToken ct,
        params string[] keys)
    {
        var results = new List<object>();
        var allFound = true;

        foreach (var key in keys.Where(k => !string.IsNullOrWhiteSpace(k)))
        {
            var val = await getCredential(key, ct);
            var found = val is not null;
            if (!found) allFound = false;
            results.Add(new { key, found });
        }

        return Results.Ok(new
        {
            type,
            success = allFound,
            credentials = results,
            message = allFound
                ? "All credentials found in Key Vault."
                : "One or more credentials not found in Key Vault."
        });
    }

    private static async Task<IResult> TestOAuth2(
        JsonElement root,
        Func<string, CancellationToken, Task<string?>> getCredential,
        IHttpClientFactory httpFactory,
        CancellationToken ct)
    {
        var tokenUrl = Str(root, "tokenUrl");
        var method = Str(root, "method").ToUpperInvariant();
        var placement = Str(root, "credentialPlacement");
        var clientIdKey = Str(root, "clientIdKey");
        var clientSecretKey = Str(root, "clientSecretKey");
        var contentTypeHeader = Str(root, "tokenRequestContentType");
        var bodyTemplate = Str(root, "tokenRequestTemplate");
        var tokenField = Str(root, "tokenField") is { Length: > 0 } tf ? tf : "access_token";
        var tokenTypeField = Str(root, "tokenTypeField") is { Length: > 0 } ttf ? ttf : "token_type";
        var expiresInField = Str(root, "expiresInField") is { Length: > 0 } ef ? ef : "expires_in";
        var scopes = Str(root, "scopes");

        if (string.IsNullOrWhiteSpace(tokenUrl))
            return Results.Ok(new { type = "oauth2", success = false, error = "Token URL is not configured." });

        // Resolve credentials from Key Vault
        var clientId = await getCredential(clientIdKey, ct);
        var clientSecret = await getCredential(clientSecretKey, ct);

        var credCheck = new object[]
        {
            new { key = clientIdKey, found = clientId is not null },
            new { key = clientSecretKey, found = clientSecret is not null },
        };

        if (clientId is null || clientSecret is null)
        {
            return Results.Ok(new
            {
                type = "oauth2",
                success = false,
                error = "One or more credentials not found in Key Vault.",
                credentials = credCheck,
            });
        }

        // Build the token request
        var httpMethod = method == "GET" ? HttpMethod.Get : HttpMethod.Post;
        var request = new HttpRequestMessage(httpMethod, tokenUrl);

        if (placement == "header")
        {
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", encoded);

            var formFields = new List<KeyValuePair<string, string>>
            {
                new("grant_type", "client_credentials"),
            };
            if (!string.IsNullOrEmpty(scopes))
                formFields.Add(new("scope", scopes));

            request.Content = new FormUrlEncodedContent(formFields);
        }
        else // body — substitute {{key_name}} placeholders
        {
            var body = bodyTemplate
                .Replace($"{{{{{clientIdKey}}}}}", clientId)
                .Replace($"{{{{{clientSecretKey}}}}}", clientSecret);

            var ct2 = string.IsNullOrEmpty(contentTypeHeader) ? "application/json" : contentTypeHeader;
            request.Content = new StringContent(body, Encoding.UTF8, ct2);
        }

        try
        {
            var http = httpFactory.CreateClient("FlowEngine");
            using var response = await http.SendAsync(request, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            // Attempt to parse and pretty-print the response
            string prettyResponse = responseBody;
            string? tokenPreview = null;
            string? tokenTypeValue = null;
            string? expiresValue = null;
            bool tokenFound = false, tokenTypeFound = false, expiresFound = false;

            try
            {
                var responseEl = JsonDocument.Parse(responseBody).RootElement;

                if (responseEl.TryGetProperty(tokenField, out var tokenEl))
                {
                    tokenFound = true;
                    var raw = tokenEl.GetString() ?? tokenEl.ToString();
                    tokenPreview = RedactToken(raw);
                }

                if (responseEl.TryGetProperty(tokenTypeField, out var tokenTypeEl))
                {
                    tokenTypeFound = true;
                    tokenTypeValue = tokenTypeEl.GetString() ?? tokenTypeEl.ToString();
                }

                if (responseEl.TryGetProperty(expiresInField, out var expiresEl))
                {
                    expiresFound = true;
                    expiresValue = expiresEl.ToString();
                }

                // Redact sensitive token values before echoing the raw response back to the
                // browser — this previously returned the vendor's complete, unredacted access
                // token (and any refresh_token) verbatim. Field names/structure stay visible for
                // debugging; values don't. Non-JSON responses fall through to the unredacted
                // fallback below since there's no reliable way to locate the token in them.
                var redactedNode = JsonNode.Parse(responseBody);
                if (redactedNode is JsonObject redactedObj)
                {
                    if (redactedObj.ContainsKey(tokenField))
                        redactedObj[tokenField] = "***redacted***";
                    if (tokenField != "refresh_token" && redactedObj.ContainsKey("refresh_token"))
                        redactedObj["refresh_token"] = "***redacted***";
                    prettyResponse = redactedObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                }
            }
            catch { /* non-JSON response — surface rawResponse as-is */ }

            return Results.Ok(new
            {
                type = "oauth2",
                success = response.IsSuccessStatusCode && tokenFound,
                statusCode = (int)response.StatusCode,
                rawResponse = prettyResponse,
                credentials = credCheck,
                fieldMapping = new
                {
                    token = new { name = tokenField, found = tokenFound, preview = tokenPreview },
                    tokenType = new { name = tokenTypeField, found = tokenTypeFound, value = tokenTypeValue },
                    expiresIn = new { name = expiresInField, found = expiresFound, value = expiresValue },
                },
            });
        }
        catch (Exception ex)
        {
            return Results.Ok(new
            {
                type = "oauth2",
                success = false,
                error = $"Request failed: {ex.Message}",
                credentials = credCheck,
            });
        }
    }
}
