using System.Text.Json;
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
    private readonly ITelephonyCallSessionStore _sessionStore;

    public EslBackgroundService(
        IHubContext<FlowHub, IFlowHubClient> hub,
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<EslBackgroundService> logger,
        ITelephonyCallSessionStore sessionStore)
    {
        _hub          = hub;
        _scopeFactory = scopeFactory;
        _config       = config;
        _logger       = logger;
        _sessionStore = sessionStore;
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
        await esl.SubscribeAsync("CHANNEL_PARK CHANNEL_HANGUP CHANNEL_HANGUP_COMPLETE CHANNEL_BRIDGE CHANNEL_UNBRIDGE", ct);

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
                case "CHANNEL_HANGUP_COMPLETE":
                    await HandleChannelHangupAsync(vars, ct);
                    break;
                case "CHANNEL_BRIDGE":
                {
                    var uuid  = vars.GetValueOrDefault("Unique-ID") ?? "";
                    var other = vars.GetValueOrDefault("Bridge-B-Unique-ID") ?? vars.GetValueOrDefault("Other-Leg-Unique-ID") ?? "";
                    _logger.LogInformation("CHANNEL_BRIDGE {Uuid} ↔ {Other}", uuid, other);
                    break;
                }
                case "CHANNEL_UNBRIDGE":
                {
                    var uuid  = vars.GetValueOrDefault("Unique-ID") ?? "";
                    var cause = vars.GetValueOrDefault("Hangup-Cause") ?? "";
                    _logger.LogInformation("CHANNEL_UNBRIDGE {Uuid} cause={Cause}", uuid, cause);
                    break;
                }
            }
        }
    }

    private async Task HandleChannelParkAsync(
        Dictionary<string, string> vars, EslClient esl, CancellationToken ct)
    {
        var destination  = vars.GetValueOrDefault("Caller-Destination-Number") ?? "";
        var channelUuid  = vars.GetValueOrDefault("Unique-ID") ?? "";

        if (string.IsNullOrEmpty(destination) || string.IsNullOrEmpty(channelUuid)) return;

        var callerNumber = vars.GetValueOrDefault("Caller-Caller-ID-Number") ?? "";
        var callerName   = vars.GetValueOrDefault("Caller-Caller-ID-Name") ?? "";

        // ESL originate test calls set origination_caller_id_number but FreeSWITCH copies
        // the dialed number into Caller-Caller-ID-Number on the outbound channel. Fall back
        // to the origination variable so the correct ANI is shown even during testing.
        if (string.IsNullOrEmpty(callerNumber) || callerNumber == destination)
        {
            callerNumber = vars.GetValueOrDefault("variable_origination_caller_id_number")
                        ?? vars.GetValueOrDefault("variable_sip_from_user")
                        ?? "Unknown";
        }

        // Prevent loopback bowout: when the parked channel bridges to an agent, FreeSWITCH's
        // loopback module would otherwise tear down the loopback pair and send BYE to the agent.
        // This is a no-op on real SIP channels (variable is unknown and ignored).
        await esl.SetChannelVarAsync(channelUuid, "loopback_bowout", "false", ct);

        using var scope      = _scopeFactory.CreateScope();
        var platformDb       = scope.ServiceProvider.GetRequiredService<ContactConnectionDbContext>();
        var dbFactory        = scope.ServiceProvider.GetRequiredService<ITenantDbContextFactory>();
        var telephonyEngine  = scope.ServiceProvider.GetRequiredService<ITelephonyFlowEngine>();

        // ── DID routing: check if destination matches a provisioned phone number ──
        // Normalize: strip leading + so "+18005551234" matches "18005551234" or "8005551234"
        var destNorm = destination.TrimStart('+');
        var routing = await platformDb.PhoneNumberRoutings
            .FirstOrDefaultAsync(r => r.IsActive && (
                r.Number == destination ||
                r.Number == destNorm ||
                "+" + r.Number == destination ||
                "1" + r.Number == destNorm), ct);

        if (routing is not null)
        {
            await HandleDidCallAsync(
                routing, callerNumber, callerName, channelUuid, destination, vars,
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
        string destinationNumber,
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
            TenantTimezone    = tenant.Timezone,
            Esl               = esl,
            ChannelVars       = channelVars,
        };

        await telephonyEngine.ExecuteAsync(ctx, ct);

        // Persist the flow execution trace to the call record
        if (ctx.Trace is not null)
        {
            var traceJson = JsonSerializer.Serialize(ctx.Trace, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false,
            });
            record.SetTelephonyTrace(traceJson);
            await db.SaveChangesAsync(ct);
        }

        // If the flow queued the call, broadcast screen pop to eligible agents
        if (ctx.Vars.TryGetValue("_queued", out _) && ctx.Vars.TryGetValue("_eligible_agents", out var agentList))
        {
            foreach (var agentIdStr in agentList.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!Guid.TryParse(agentIdStr.Trim(), out var agentId)) continue;
                await _hub.Clients
                    .Group($"agent:{agentId}")
                    .ReceiveIncomingCall(record.Id.ToString(), callerNumber, callerName, destinationNumber);
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
                .ReceiveIncomingCall(record.Id.ToString(), callerNumber, callerName, agentExtension);

            _logger.LogInformation(
                "CHANNEL_PARK {Uuid}: agent {Ext} → CallRecord {RecordId}",
                channelUuid, agentExtension, record.Id);
            return;
        }

        _logger.LogWarning("CHANNEL_PARK {Uuid}: no agent found for extension {Ext}", channelUuid, agentExtension);
    }

    /// <summary>
    /// Channel hung up — fire the call_disconnected event branch (if configured), mark the
    /// CallRecord complete, and delete the live call session from Redis.
    /// </summary>
    private async Task HandleChannelHangupAsync(Dictionary<string, string> vars, CancellationToken ct)
    {
        var channelUuid = vars.GetValueOrDefault("Unique-ID");
        if (string.IsNullOrEmpty(channelUuid)) return;

        var cause = vars.GetValueOrDefault("Hangup-Cause") ?? "unknown";
        _logger.LogDebug("CHANNEL_HANGUP {Uuid} cause={Cause}", channelUuid, cause);

        // Check if there is a live session for this call (DID calls always have one)
        var session = await _sessionStore.GetAsync(channelUuid, ct);
        if (session is not null)
        {
            using var scope         = _scopeFactory.CreateScope();
            var telephonyEngine     = scope.ServiceProvider.GetRequiredService<ITelephonyFlowEngine>();
            var dbFactory           = scope.ServiceProvider.GetRequiredService<ITenantDbContextFactory>();

            // Fire the call_disconnected event so the designer branch can run post-call actions
            await telephonyEngine.FireEventAsync(
                channelUuid, "call_disconnected",
                new FireEventContext { AdditionalVars = new() { ["hangup_cause"] = cause } },
                ct);

            // Mark the call record complete
            await using var db = dbFactory.Create(session.TenantSchemaName);
            var record = await db.CallRecords.FirstOrDefaultAsync(
                r => r.ContactIdExternal == channelUuid, ct);
            if (record is not null)
            {
                record.Complete();
                await db.SaveChangesAsync(ct);
                _logger.LogInformation(
                    "CHANNEL_HANGUP {Uuid} cause={Cause} → CallRecord {RecordId} completed",
                    channelUuid, cause, record.Id);
            }

            // Delete session — call is over
            await _sessionStore.DeleteAsync(channelUuid, ct);
            return;
        }

        // No session: direct extension call or session already expired — fall back to tenant scan
        await HandleHangupByTenantScanAsync(channelUuid, cause, ct);
    }

    private async Task HandleHangupByTenantScanAsync(string channelUuid, string cause, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbFactory   = scope.ServiceProvider.GetRequiredService<ITenantDbContextFactory>();
        var platformDb  = scope.ServiceProvider.GetRequiredService<ContactConnectionDbContext>();

        var tenants = await platformDb.Tenants.Where(t => t.IsActive).ToListAsync(ct);
        foreach (var tenant in tenants)
        {
            await using var db = dbFactory.Create(tenant.SchemaName);
            var record = await db.CallRecords.FirstOrDefaultAsync(
                r => r.ContactIdExternal == channelUuid, ct);
            if (record is null) continue;

            record.Complete();
            await db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "CHANNEL_HANGUP {Uuid} cause={Cause} → CallRecord {RecordId} completed (tenant scan)",
                channelUuid, cause, record.Id);
            return;
        }
    }
}
