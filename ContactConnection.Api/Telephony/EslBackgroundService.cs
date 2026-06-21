using ContactConnection.Api.Hubs;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Api.Telephony;

/// <summary>
/// Hosted service that maintains a persistent ESL connection to FreeSWITCH.
///
/// CHANNEL_PARK routing:
///   1. Look up Caller-Destination-Number in PhoneNumberRouting (global DID table).
///      If found → run the tenant's telephony call flow (pre-answer routing).
///   2. If not found → treat as a direct agent extension call (screen pop).
///
/// CHANNEL_HANGUP → mark CallRecord complete.
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
            if (msg is null) break;

            if (msg.ContentType != "text/event-plain") continue;

            var vars = msg.ParseBody();
            if (!vars.TryGetValue("Event-Name", out var eventName)) continue;

            switch (eventName)
            {
                case "CHANNEL_PARK":
                    await HandleChannelParkAsync(vars, esl, ct);
                    break;
                case "CHANNEL_HANGUP":
                    await HandleChannelHangupAsync(vars, ct);
                    break;
            }
        }
    }

    private async Task HandleChannelParkAsync(
        Dictionary<string, string> vars, EslClient esl, CancellationToken ct)
    {
        var destination  = vars.GetValueOrDefault("Caller-Destination-Number") ?? "";
        var callerNumber = vars.GetValueOrDefault("Caller-Caller-ID-Number") ?? "Unknown";
        var callerName   = vars.GetValueOrDefault("Caller-Caller-ID-Name") ?? "";
        var channelUuid  = vars.GetValueOrDefault("Unique-ID") ?? "";

        if (string.IsNullOrEmpty(destination) || string.IsNullOrEmpty(channelUuid)) return;

        using var scope      = _scopeFactory.CreateScope();
        var platformDb       = scope.ServiceProvider.GetRequiredService<ContactConnectionDbContext>();
        var dbFactory        = scope.ServiceProvider.GetRequiredService<ITenantDbContextFactory>();
        var telephonyEngine  = scope.ServiceProvider.GetRequiredService<ITelephonyFlowEngine>();

        // ── DID routing: check if destination matches a provisioned phone number ──
        var routing = await platformDb.PhoneNumberRoutings
            .FirstOrDefaultAsync(r => r.Number == destination && r.IsActive, ct);

        if (routing is not null)
        {
            await HandleDidCallAsync(
                routing, callerNumber, callerName, channelUuid, vars,
                esl, telephonyEngine, platformDb, dbFactory, ct);
            return;
        }

        // ── Agent extension: fall through to screen pop (existing behavior) ──
        await HandleAgentExtensionCallAsync(
            destination, callerNumber, callerName, channelUuid,
            platformDb, dbFactory, ct);
    }

    /// <summary>
    /// Inbound DID call — look up tenant + campaign, create CallRecord, run telephony flow.
    /// </summary>
    private async Task HandleDidCallAsync(
        PhoneNumberRouting routing,
        string callerNumber,
        string callerName,
        string channelUuid,
        Dictionary<string, string> eventVars,
        EslClient esl,
        ITelephonyFlowEngine telephonyEngine,
        ContactConnectionDbContext platformDb,
        ITenantDbContextFactory dbFactory,
        CancellationToken ct)
    {
        var tenant = await platformDb.Tenants.FirstOrDefaultAsync(t => t.Id == routing.TenantId, ct);
        if (tenant is null)
        {
            _logger.LogWarning("CHANNEL_PARK DID {Uuid}: tenant {TenantId} not found", channelUuid, routing.TenantId);
            return;
        }

        await using var db = dbFactory.Create(tenant.SchemaName);

        var record = CallRecord.CreateInbound(
            tenantId: tenant.Id,
            callerId: callerNumber,
            agentId: null,
            contactIdExternal: channelUuid);

        // Stamp the campaign so the CallRecord knows where it belongs
        record.SetCampaign(routing.CampaignId);

        db.CallRecords.Add(record);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "CHANNEL_PARK DID {Uuid}: tenant={Tenant} campaign={Campaign} → CallRecord {RecordId}",
            channelUuid, tenant.Subdomain, routing.CampaignId, record.Id);

        // Extract SIP headers from event vars (variable_sip_h_* → sip_h_*) for flow handlers
        var channelVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in eventVars)
        {
            if (key.StartsWith("variable_sip_h_", StringComparison.OrdinalIgnoreCase))
                channelVars[key["variable_sip_h_".Length..]] = value;
            else if (key.StartsWith("variable_", StringComparison.OrdinalIgnoreCase))
                channelVars[key["variable_".Length..]] = value;
        }

        var ctx = new TelephonyFlowContext
        {
            ChannelUuid       = channelUuid,
            CallerNumber      = callerNumber,
            DestinationNumber = routing.Number,
            TenantId          = tenant.Id,
            CampaignId        = routing.CampaignId,
            CallRecordId      = record.Id,
            TenantSubdomain   = tenant.Subdomain,
            TenantSchemaName  = tenant.SchemaName,
            Esl               = esl,
            ChannelVars       = channelVars,
        };

        await telephonyEngine.ExecuteAsync(ctx, ct);

        // If the flow queued the call, broadcast screen pop to eligible agents
        if (ctx.Vars.TryGetValue("_queued", out _) && ctx.Vars.TryGetValue("_eligible_agents", out var agentList))
        {
            foreach (var agentIdStr in agentList.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!Guid.TryParse(agentIdStr.Trim(), out var agentId)) continue;
                await _hub.Clients
                    .Group($"agent:{agentId}")
                    .ReceiveIncomingCall(record.Id.ToString(), callerNumber, callerName);
            }
        }
    }

    /// <summary>
    /// Direct agent extension call (agent-to-agent or originate test) — create CallRecord and push screen pop.
    /// </summary>
    private async Task HandleAgentExtensionCallAsync(
        string agentExtension,
        string callerNumber,
        string callerName,
        string channelUuid,
        ContactConnectionDbContext platformDb,
        ITenantDbContextFactory dbFactory,
        CancellationToken ct)
    {
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

        using var scope    = _scopeFactory.CreateScope();
        var dbFactory      = scope.ServiceProvider.GetRequiredService<ITenantDbContextFactory>();
        var platformDb     = scope.ServiceProvider.GetRequiredService<ContactConnectionDbContext>();

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
