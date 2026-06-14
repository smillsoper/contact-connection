using System.Collections.Concurrent;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.FreeSwitchEsl;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Worker;

/// <summary>
/// Maintains a persistent ESL connection to FreeSWITCH and translates telephony events
/// into CallRecord lifecycle operations.
///
/// Tenant resolution is a three-step cascade — first match wins:
///
///   1. Subdomain routing  — ${sip_to_host} = "tms.contactconnection.cc"
///                           → extract "tms" → GetBySubdomainAsync
///                           Used when ContactConnection is the carrier (Telnyx).
///
///   2. SIP gateway name   — variable_sip_gateway_name = "twilio-tms"
///                           → GetByNameAsync → gateway.TenantId
///                           Used for BYOC tenants with SIP credentials.
///
///   3. DNIS lookup        — variable_cc_did = "+15035551234"
///                           → public.phone_number_routing → tenant_id + campaign_id
///                           Used for IP-routing carriers (Commio, CLECs) where no
///                           SIP auth or subdomain is present. E.164 numbers are globally
///                           unique so the called number unambiguously identifies the tenant.
/// </summary>
public sealed class FreeSwitchEslService : BackgroundService
{
    private readonly ILogger<FreeSwitchEslService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _host;
    private readonly int _port;
    private readonly string _password;
    private readonly int _reconnectDelaySecs;

    // channel UUID → CallRecord ID (populated on CHANNEL_PARK, removed on CHANNEL_HANGUP)
    private readonly ConcurrentDictionary<string, Guid> _activeChannels = new();
    // channel UUID → tenant subdomain (preserved for hangup scope recreation)
    private readonly ConcurrentDictionary<string, string> _channelTenants = new();

    public FreeSwitchEslService(
        ILogger<FreeSwitchEslService> logger,
        IServiceScopeFactory scopeFactory,
        IConfiguration config)
    {
        _logger       = logger;
        _scopeFactory = scopeFactory;

        var section = config.GetSection("FreeSwitchEsl");
        _host               = section["Host"]               ?? "localhost";
        _port               = section.GetValue<int>("Port", 8021);
        _password           = section["Password"]           ?? "ClueCon";
        _reconnectDelaySecs = section.GetValue<int>("ReconnectDelaySeconds", 5);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAndRunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "FreeSWITCH ESL connection lost. Reconnecting in {Delay}s…", _reconnectDelaySecs);
                await Task.Delay(TimeSpan.FromSeconds(_reconnectDelaySecs), stoppingToken);
            }
        }

        _logger.LogInformation("FreeSWITCH ESL service stopped.");
    }

    private async Task ConnectAndRunAsync(CancellationToken ct)
    {
        await using var client = new FreeSwitchEslClient(_host, _port, _password, _logger);
        await client.ConnectAsync(ct);
        await client.SubscribeAsync(["CHANNEL_PARK", "CHANNEL_ANSWER", "CHANNEL_HANGUP"], ct);

        while (!ct.IsCancellationRequested)
        {
            var evt = await client.ReadEventAsync(ct);
            if (evt is null) return;

            if (evt.ContentType != "text/event-plain") continue;

            try   { await DispatchAsync(evt, ct); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling {Event} for channel {Channel}",
                    evt.EventName, evt.UniqueId);
            }
        }
    }

    private Task DispatchAsync(EslEvent evt, CancellationToken ct) => evt.EventName switch
    {
        "CHANNEL_PARK"   => HandleChannelParkAsync(evt, ct),
        "CHANNEL_ANSWER" => HandleChannelAnswerAsync(evt, ct),
        "CHANNEL_HANGUP" => HandleChannelHangupAsync(evt, ct),
        _                => Task.CompletedTask
    };

    // ── Event handlers ────────────────────────────────────────────────────────

    private async Task HandleChannelParkAsync(EslEvent evt, CancellationToken ct)
    {
        if (evt.GetVariable("cc_inbound") != "true") return;

        var channelId   = evt.UniqueId;
        var sipToHost   = evt.GetVariable("sip_to_host");
        var gatewayName = evt.GetVariable("sip_gateway_name");
        var did         = evt.GetVariable("cc_did")       ?? string.Empty;
        var callerId    = evt.GetVariable("cc_caller_id") ?? string.Empty;

        if (string.IsNullOrEmpty(channelId)) return;

        _logger.LogInformation(
            "Inbound call parked — channel {Channel} host {Host} gateway {Gateway} DID {Did} from {Caller}",
            channelId, sipToHost, gatewayName, did, callerId);

        using var scope      = _scopeFactory.CreateScope();
        var tenantRepo       = scope.ServiceProvider.GetRequiredService<ITenantRepository>();

        Tenant? tenant     = null;
        var     campaignId = Guid.Empty;

        // ── Step 1: subdomain routing (ContactConnection-as-carrier) ──────────
        if (!string.IsNullOrEmpty(sipToHost))
        {
            var subdomain = sipToHost.Split('.')[0];
            if (!string.IsNullOrEmpty(subdomain))
                tenant = await tenantRepo.GetBySubdomainAsync(subdomain, ct);
        }

        // ── Step 2: SIP gateway name (BYOC with credentials) ──────────────────
        if (tenant is null && !string.IsNullOrEmpty(gatewayName))
        {
            var gatewayRepo = scope.ServiceProvider.GetRequiredService<ISipGatewayRepository>();
            var gateway     = await gatewayRepo.GetByNameAsync(gatewayName, ct);
            if (gateway is not null)
                tenant = await tenantRepo.GetByIdAsync(gateway.TenantId, ct);
        }

        // ── Step 3: DNIS lookup (IP-routing carriers — Commio, CLECs) ────────
        if (tenant is null && !string.IsNullOrEmpty(did))
        {
            var routingRepo = scope.ServiceProvider.GetRequiredService<IPhoneNumberRoutingRepository>();
            var routing     = await routingRepo.GetByNumberAsync(did, ct);
            if (routing is { IsActive: true })
            {
                tenant     = await tenantRepo.GetByIdAsync(routing.TenantId, ct);
                campaignId = routing.CampaignId;
            }
        }

        if (tenant is null)
        {
            _logger.LogWarning(
                "No tenant resolved for channel {Channel} (host={Host} gateway={Gateway} did={Did}) — dropping",
                channelId, sipToHost, gatewayName, did);
            return;
        }

        // Set tenant context so tenant-scoped repos can create their DbContext
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.Current = tenant;

        // Resolve campaign from DID if not already known from step 3
        var clientId = Guid.Empty;
        if (!string.IsNullOrEmpty(did) && campaignId == Guid.Empty)
        {
            var phoneRepo = scope.ServiceProvider.GetRequiredService<IPhoneNumberRepository>();
            var phone     = await phoneRepo.GetByNumberAsync(did, ct);
            if (phone is not null)
                campaignId = phone.CampaignId;
        }

        if (campaignId != Guid.Empty)
        {
            var campaignRepo = scope.ServiceProvider.GetRequiredService<ICampaignRepository>();
            var campaign     = await campaignRepo.GetByIdAsync(campaignId, ct);
            if (campaign is not null)
                clientId = campaign.ClientId;
        }
        else
        {
            _logger.LogWarning(
                "DID {Did} not mapped to a campaign in tenant {Tenant} — CallRecord will have empty ids",
                did, tenant.Subdomain);
        }

        var callRepo = scope.ServiceProvider.GetRequiredService<ICallRecordRepository>();
        var record   = CallRecord.Create(tenant.Id, clientId, campaignId, CallSource.Inbound, callerId);
        record.SetExternalContactId(channelId);

        await callRepo.AddAsync(record, ct);
        await callRepo.SaveChangesAsync(ct);

        _activeChannels[channelId] = record.Id;
        _channelTenants[channelId] = tenant.Subdomain;

        _logger.LogInformation(
            "Created CallRecord {Record} for channel {Channel} — tenant {Tenant} campaign {Campaign}",
            record.Id, channelId, tenant.Subdomain, campaignId);

        // TODO: screen pop — push record.Id to available agents in campaign via SignalR
    }

    private Task HandleChannelAnswerAsync(EslEvent evt, CancellationToken ct)
    {
        var channelId = evt.UniqueId;
        if (channelId is not null && _activeChannels.TryGetValue(channelId, out var recordId))
            _logger.LogInformation("Channel {Channel} answered (CallRecord {Record})", channelId, recordId);

        return Task.CompletedTask;
    }

    private async Task HandleChannelHangupAsync(EslEvent evt, CancellationToken ct)
    {
        var channelId = evt.UniqueId;
        if (string.IsNullOrEmpty(channelId)) return;

        if (!_activeChannels.TryRemove(channelId, out var recordId))
            return;

        _channelTenants.TryRemove(channelId, out var subdomain);

        var cause = evt.GetVariable("hangup_cause")
                    ?? evt.Headers.GetValueOrDefault("Hangup-Cause")
                    ?? "UNKNOWN";

        _logger.LogInformation(
            "Channel {Channel} hung up — cause {Cause} (CallRecord {Record})",
            channelId, cause, recordId);

        using var scope = _scopeFactory.CreateScope();

        Tenant? tenant = null;
        if (!string.IsNullOrEmpty(subdomain))
        {
            var tenantRepo = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
            tenant = await tenantRepo.GetBySubdomainAsync(subdomain, ct);
        }

        if (tenant is null)
        {
            _logger.LogWarning(
                "Could not restore tenant context for channel {Channel} — CallRecord {Record} not finalized",
                channelId, recordId);
            return;
        }

        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.Current = tenant;

        var repo   = scope.ServiceProvider.GetRequiredService<ICallRecordRepository>();
        var record = await repo.GetByIdAsync(recordId, ct);
        if (record is null) return;

        record.Complete();
        await repo.SaveChangesAsync(ct);

        _logger.LogInformation("Finalized CallRecord {Record}", record.Id);
    }
}
