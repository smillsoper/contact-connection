using System.Security.Cryptography;
using System.Text;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// ICE servers for the agent softphone (S171). FreeSWITCH (in Docker) can only offer browsers its Docker
/// address, so a softphone that can't reach it relays audio through our coturn TURN server
/// (coturn/turnserver.conf). Credentials follow the TURN REST API scheme coturn's use-auth-secret
/// expects: username "{expiry unix}:{agentId}", password = base64(HMAC-SHA1(secret, username)) — short-
/// lived and issued only to signed-in agents, so the relay can't be used by anyone else.
///
/// Config: Turn:Urls (e.g. turn:192.168.1.45:3478?transport=udp — this PC's LAN address in dev; add the
/// public address once the router forwards 3478), Turn:Secret (User Secrets / Key Vault — same value as
/// TURN_STATIC_AUTH_SECRET in .env), Turn:TtlMinutes (default 720). Not configured = no ICE servers,
/// the softphone behaves exactly as before.
/// </summary>
public static class SoftphoneEndpoints
{
    public static IEndpointRouteBuilder MapSoftphoneEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/softphone/ice-servers", (HttpContext http, IConfiguration config) =>
        {
            var agentId = http.User.FindFirst("sub")?.Value;
            if (string.IsNullOrEmpty(agentId)) return Results.Unauthorized();

            var urls = config.GetSection("Turn:Urls").Get<string[]>() ?? [];
            var secret = config["Turn:Secret"];
            if (urls.Length == 0 || string.IsNullOrEmpty(secret))
                return Results.Ok(new { iceServers = Array.Empty<object>(), expiresAt = (DateTimeOffset?)null });

            var ttl = TimeSpan.FromMinutes(config.GetValue("Turn:TtlMinutes", 720));
            var expiresAt = DateTimeOffset.UtcNow.Add(ttl);
            var (username, credential) = TurnCredentials(secret, agentId, expiresAt);
            return Results.Ok(new
            {
                iceServers = new[] { new { urls, username, credential } },
                expiresAt,
            });
        }).RequireAuthorization();
        return app;
    }

    /// <summary>TURN REST API credentials (draft-uberti-behave-turn-rest), as coturn's use-auth-secret checks them.</summary>
    public static (string Username, string Credential) TurnCredentials(string secret, string userId, DateTimeOffset expiresAt)
    {
        var username = $"{expiresAt.ToUnixTimeSeconds()}:{userId}";
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(secret));
        return (username, Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(username))));
    }
}
