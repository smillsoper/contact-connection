using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ContactConnection.Application.Interfaces.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Telephony;

/// <summary>
/// One SignalWire subproject per tenant (S185). SignalWire lets the main project create subprojects through its
/// Compatibility API (POST /api/laml/2010-04-01/Accounts, FriendlyName) — a subproject is a full project with its own
/// Project ID, numbers and credentials, which is how SignalWire recommends isolating a tenant. Authenticates with the
/// main project's ID + an API token with the Management scope, both kept in the platform credential store
/// ("signalwire-projectid", "signalwire-apitoken") — never in config.
/// </summary>
public sealed class SignalWireProjects(IPortalCredentialStore credentials, IHttpClientFactory http, IConfiguration config,
    ILogger<SignalWireProjects> logger)
{
    public const string ProjectIdKey = "signalwire-projectid";
    public const string ApiTokenKey = "signalwire-apitoken";

    public sealed record Result(string? ProjectId, string? Error);

    /// <summary>Creates a subproject named after the tenant; returns its Project ID, or why it couldn't.</summary>
    public async Task<Result> CreateSubprojectAsync(string friendlyName, CancellationToken ct)
    {
        var space = config["SignalWire:SpaceName"];
        if (string.IsNullOrWhiteSpace(space)) return new(null, "SignalWire:SpaceName isn't configured.");
        var projectId = await credentials.GetAsync(ProjectIdKey, ct);
        var token = await credentials.GetAsync(ApiTokenKey, ct);
        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(token))
            return new(null, $"Add the SignalWire credentials ({ProjectIdKey}, {ApiTokenKey}) on the Credentials page first.");

        var name = friendlyName.Trim();
        if (name.Length > 250) name = name[..250];
        using var req = new HttpRequestMessage(HttpMethod.Post, $"https://{space}.signalwire.com/api/laml/2010-04-01/Accounts")
        {
            Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("FriendlyName", name)]),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{projectId.Trim()}:{token.Trim()}")));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var res = await http.CreateClient("SignalWire").SendAsync(req, timeout.Token);
            var body = await res.Content.ReadAsStringAsync(timeout.Token);
            if (!res.IsSuccessStatusCode)
            {
                logger.LogWarning("SignalWire subproject create failed: {Status}", (int)res.StatusCode);
                return new(null, (int)res.StatusCode switch
                {
                    401 => "SignalWire refused the credentials — check the Project ID and API token.",
                    403 => "The SignalWire API token needs the Management scope.",
                    _ => $"SignalWire returned HTTP {(int)res.StatusCode}.",
                });
            }
            using var doc = JsonDocument.Parse(body);
            var sid = doc.RootElement.TryGetProperty("sid", out var s) ? s.GetString() : null;
            return string.IsNullOrWhiteSpace(sid) ? new(null, "SignalWire didn't return a project ID.") : new(sid, null);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "SignalWire subproject create failed");
            return new(null, $"Couldn't reach SignalWire: {ex.Message}");
        }
    }
}
