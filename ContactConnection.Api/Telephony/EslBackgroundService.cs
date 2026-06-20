using ContactConnection.Api.Hubs;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Api.Telephony;

/// <summary>
/// Hosted service that maintains a persistent ESL connection to FreeSWITCH.
/// Handles CHANNEL_PARK (inbound call → create CallRecord + SignalR screen pop)
/// and CHANNEL_HANGUP (call ended → mark CallRecord complete).
/// Reconnects automatically on disconnect.
/// </summary>
public sealed class EslBackgroundService : BackgroundService
{
    private readonly IHubContext<FlowHub, IFlowHubClient> _hub;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<EslBackgroundService> _logger;

    public EslBackgroundService(
        IHubContext<FlowHub, IFlowHubClient> hub,
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<EslBackgroundService> logger)
    {
        _hub = hub;
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunLoopAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ESL connection lost. Reconnecting in 5s.");
                await Task.Delay(5_000, stoppingToken);
            }
        }
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        var host = _config["FreeSWITCH:Host"] ?? "127.0.0.1";
        var port = int.Parse(_config["FreeSWITCH:EslPort"] ?? "8021");
        var pass = _config["FreeSWITCH:EslPassword"] ?? "ClueCon";

        await using var esl = new EslClient();
        await esl.ConnectAsync(host, port, pass, ct);
        await esl.SubscribeAsync("CHANNEL_PARK CHANNEL_HANGUP", ct);

        _logger.LogInformation("ESL connected to FreeSWITCH at {Host}:{Port}", host, port);

        while (!ct.IsCancellationRequested)
        {
            var msg = await esl.ReadMessageAsync(ct);
            if (msg is null) break; // clean disconnect

            if (msg.ContentType != "text/event-plain") continue;

            var vars = msg.ParseBody();
            if (!vars.TryGetValue("Event-Name", out var eventName)) continue;

            switch (eventName)
            {
                case "CHANNEL_PARK":
                    await HandleChannelParkAsync(vars, ct);
                    break;
                case "CHANNEL_HANGUP":
                    await HandleChannelHangupAsync(vars, ct);
                    break;
            }
        }
    }

    /// <summary>
    /// Inbound call parked — find the target agent, create a CallRecord, push screen pop.
    /// </summary>
    private async Task HandleChannelParkAsync(Dictionary<string, string> vars, CancellationToken ct)
    {
        var agentExtension = vars.GetValueOrDefault("Caller-Destination-Number");
        var callerNumber   = vars.GetValueOrDefault("Caller-Caller-ID-Number") ?? "Unknown";
        var callerName     = vars.GetValueOrDefault("Caller-Caller-ID-Name") ?? "";
        var channelUuid    = vars.GetValueOrDefault("Unique-ID");

        if (string.IsNullOrEmpty(agentExtension) || string.IsNullOrEmpty(channelUuid)) return;

        using var scope = _scopeFactory.CreateScope();
        var dbFactory  = scope.ServiceProvider.GetRequiredService<ITenantDbContextFactory>();
        var platformDb = scope.ServiceProvider.GetRequiredService<ContactConnectionDbContext>();

        var tenants = await platformDb.Tenants.Where(t => t.IsActive).ToListAsync(ct);

        foreach (var tenant in tenants)
        {
            await using var db = dbFactory.Create(tenant.SchemaName);

            var agent = await db.Agents.FirstOrDefaultAsync(
                a => a.SipExtension == agentExtension && a.IsActive, ct);

            if (agent is null) continue;

            var record = CallRecord.CreateInbound(
                tenantId: tenant.Id,
                callerId: callerNumber,
                agentId: agent.Id,
                contactIdExternal: channelUuid);

            db.CallRecords.Add(record);
            await db.SaveChangesAsync(ct);

            await _hub.Clients
                .Group($"agent:{agent.Id}")
                .ReceiveIncomingCall(record.Id.ToString(), callerNumber, callerName);

            _logger.LogInformation(
                "CHANNEL_PARK {Uuid}: agent {Ext} → CallRecord {RecordId}",
                channelUuid, agentExtension, record.Id);
            return;
        }

        _logger.LogWarning("CHANNEL_PARK {Uuid}: no agent found for extension {Ext}", channelUuid, agentExtension);
    }

    /// <summary>
    /// Channel hung up — find the matching CallRecord by FreeSWITCH UUID and mark it complete.
    /// </summary>
    private async Task HandleChannelHangupAsync(Dictionary<string, string> vars, CancellationToken ct)
    {
        var channelUuid = vars.GetValueOrDefault("Unique-ID");
        if (string.IsNullOrEmpty(channelUuid)) return;

        using var scope = _scopeFactory.CreateScope();
        var dbFactory  = scope.ServiceProvider.GetRequiredService<ITenantDbContextFactory>();
        var platformDb = scope.ServiceProvider.GetRequiredService<ContactConnectionDbContext>();

        var tenants = await platformDb.Tenants.Where(t => t.IsActive).ToListAsync(ct);

        foreach (var tenant in tenants)
        {
            await using var db = dbFactory.Create(tenant.SchemaName);

            var record = await db.CallRecords.FirstOrDefaultAsync(
                r => r.ContactIdExternal == channelUuid, ct);

            if (record is null) continue;

            record.Complete();
            await db.SaveChangesAsync(ct);

            _logger.LogInformation("CHANNEL_HANGUP {Uuid} → CallRecord {RecordId} completed", channelUuid, record.Id);
            return;
        }
    }
}
