using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Api.Telephony;

/// <summary>
/// Mints fresh short-lived ESL connections for components outside the persistent
/// EslBackgroundService loop — currently just the call-recording watchdog's forced unmask.
/// Mirrors the ad-hoc <c>new EslClient()</c> + <c>ConnectAsync</c> pattern already used by
/// TelephonyEndpoints / QueuePollingService / QueuedCallDeliveryService, behind an interface
/// so Infrastructure code can depend on it without referencing the Api project.
/// </summary>
public sealed class EslCommanderFactory(IConfiguration config, ILogger<EslClient> eslLogger) : IEslCommanderFactory
{
    public async Task<IOwnedEslCommander> CreateAsync(CancellationToken ct = default)
    {
        var host = config["FreeSWITCH:Host"]        ?? "127.0.0.1";
        var port = int.Parse(config["FreeSWITCH:EslPort"] ?? "8021");
        var pass = config["FreeSWITCH:EslPassword"] ?? "ClueCon";

        var client = new EslClient(eslLogger);
        await client.ConnectAsync(host, port, pass, ct);
        return client;
    }
}
