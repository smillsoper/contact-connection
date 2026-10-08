using System.Text;
using System.Text.Json.Serialization;
using ContactConnection.Api.Endpoints;
using ContactConnection.Api.Hubs;
using ContactConnection.Api.Middleware;
using ContactConnection.Api.Telephony;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Extensions;
using System.Threading.RateLimiting;
using ContactConnection.Infrastructure.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Azure Key Vault â€” when configured, secrets named "Section--Key" here become
// configuration["Section:Key"] automatically, so every existing configuration[...]/
// GetConnectionString(...) call below and in AddInfrastructure keeps working unchanged.
// No-op locally where KeyVault:VaultUri isn't set â€” local dev keeps using User Secrets.
// ConfigurationManager connects eagerly the moment a source is added (unlike the old
// lazy DI-factory SecretClient registration), so a stale/unreachable credential must not
// crash startup â€” fall back to whatever's already in User Secrets/appsettings/env instead.
var vaultUri = builder.Configuration["KeyVault:VaultUri"];
if (!string.IsNullOrWhiteSpace(vaultUri))
{
    try
    {
        builder.Configuration.AddAzureKeyVault(new Uri(vaultUri), AzureCredentialFactory.Resolve(builder.Configuration));
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(
            $"WARNING: KeyVault:VaultUri is set ({vaultUri}) but Key Vault could not be reached â€” " +
            $"continuing without it, using existing configuration sources instead. Error: {ex.Message}");
    }
}

builder.Services.AddOpenApi();
builder.Services.AddInfrastructure(builder.Configuration);

// Serialize enums as strings globally â€” makes API requests/responses human-readable
// (e.g. "Available" instead of 0 for ProductInventoryStatus)
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// SignalR â€” must be registered before IFlowNotifier which depends on IHubContext.
// Redis backplane so CallTraceHub group broadcasts reach clients regardless of which
// API instance handles a given call (matching state lives in Redis separately).
builder.Services.AddSignalR()
    .AddStackExchangeRedis(builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379");
builder.Services.AddScoped<IFlowNotifier, FlowNotifier>();
// Live screen views (S183) -- in memory on this instance; the signalling itself rides the Redis backplane.
builder.Services.AddSingleton<ContactConnection.Api.Hubs.ScreenViewRegistry>();
// Connection health (S183) -- latest softphone call stats per agent, in memory.
builder.Services.AddSingleton<ContactConnection.Api.Endpoints.AgentHealthStore>();
builder.Services.AddScoped<ICallTraceNotifier, CallTraceNotifier>();
builder.Services.AddScoped<ISecureCollectNotifier, SecureCollectNotifier>();
builder.Services.AddScoped<ITelephonyEventNotifier, TelephonyEventNotifier>();
// Singleton (not scoped like the two above) â€” AgentStateStore, its only caller, is itself a
// singleton with no HTTP request scope, so it cannot depend on a scoped service.
builder.Services.AddSingleton<IDashboardNotifier, DashboardNotifier>();

// Claims a queued call for a specific agent and delivers it â€” shared by the agent's manual
// "Pick Up" click (TelephonyEndpoints.AnswerQueuedCall) and QueuePollingService's server-
// initiated RingStrategy.AutoAnswerBestAgent delivery (no HTTP round trip).
builder.Services.AddScoped<QueuedCallDeliveryService>();

// The "virtual hold" delivery path for tf_queue_callback placeholders â€” reserves an agent,
// dials the caller back, bridges the answered leg to that agent. Used by QueuePollingService
// (reserve + dial) and EslBackgroundService (answered leg, failed leg).
builder.Services.AddScoped<QueueCallbackDeliveryService>();

// Mints short-lived ESL connections for the call-recording watchdog (ICallRecordingController,
// registered in AddInfrastructure) â€” its forced unmask fires after the triggering node is gone.
builder.Services.AddSingleton<IEslCommanderFactory, EslCommanderFactory>();
builder.Services.AddScoped<ContactConnection.Api.Telephony.SupervisorCallService>();
builder.Services.AddScoped<ContactConnection.Api.Telephony.ManualOutboundCallService>();

// ESL background service â€” connects to FreeSWITCH and handles CHANNEL_PARK / CHANNEL_HANGUP
builder.Services.AddHostedService<EslBackgroundService>();
builder.Services.AddHostedService<ContactConnection.Api.Ai.AiSummaryProcessor>();

// Queue poller â€” every 1 second, notifies newly-available agents of parked calls
builder.Services.AddHostedService<QueuePollingService>();

// Periodic hold announcements â€” every 2 seconds, interrupts looping MOH with the next tf_play
// intermittent announcement when its interval comes due (PLAYBACK_STOP can't drive this for an
// endless / very long hold source).
builder.Services.AddHostedService<PlayAnnouncementService>();

// Call trace expiry sweeper â€” every 1 second, stops traces that hit their duration cap
builder.Services.AddHostedService<ContactConnection.Api.CallTrace.CallTraceExpiryBackgroundService>();

// Startup sweep â€” closes calls a previous process left non-terminal (hard restart/crash) so they
// don't linger as phantom active calls on the supervisor dashboard. Runs once, ~20s after boot.
builder.Services.AddHostedService<OrphanedCallReconciliationService>();

// Worker â†’ API supervisor-dashboard relay â€” subscribes to the Redis channel the Worker publishes
// dashboard changes on (it has no SignalR hub) and re-emits them through this instance's real
// IDashboardNotifier.
builder.Services.AddHostedService<ContactConnection.Api.Realtime.DashboardRelaySubscriber>();

// JWT Bearer authentication
var signingKey = builder.Configuration["Jwt:SigningKey"]
    ?? throw new InvalidOperationException("Jwt:SigningKey is not configured.");

var authBuilder = builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme);
authBuilder
    .AddJwtBearer(options =>
    {
        // Keep JWT claim names as-is (don't map "sub" â†’ ClaimTypes.NameIdentifier, etc.)
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        // SignalR WebSocket connections send the JWT in the query string
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    context.Token = accessToken;
                return Task.CompletedTask;
            },
            // Sign-in lock (S166, Call Records "Finalize"): a locked agent's existing tokens stop
            // working at once instead of living out their 8-hour lifetime. Portal tokens carry no
            // tenant_schema and are skipped. IAgentLockReader caches, so this isn't a DB hit per request.
            OnTokenValidated = async context =>
            {
                var principal = context.Principal;
                var schema = principal?.FindFirst("tenant_schema")?.Value;
                if (string.IsNullOrEmpty(schema) || !Guid.TryParse(principal?.FindFirst("sub")?.Value, out var agentId)) return;
                var locks = context.HttpContext.RequestServices.GetRequiredService<ContactConnection.Application.Interfaces.Services.IAgentLockReader>();
                if ((await locks.GetAsync(schema, agentId, context.HttpContext.RequestAborted))?.SignInLocked == true)
                    context.Fail(ContactConnection.Api.Endpoints.AgentLockEndpoints.SignInLockedMessage);
            },
        };
    });

// Client portal (S181): client users get tokens with their own audience, validated only by this scheme — the default
// scheme above rejects them, so no agent / admin endpoint ever accepts a client user. Deactivating a client user
// (or the account vanishing) stops their existing tokens at once.
authBuilder.AddJwtBearer(ClientUserTokens.Scheme, options =>
{
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = ClientUserTokens.Audience(builder.Configuration),
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
        ClockSkew = TimeSpan.FromMinutes(1)
    };
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs/client"))
                context.Token = accessToken;
            return Task.CompletedTask;
        },
        OnTokenValidated = async context =>
        {
            var schema = context.Principal?.FindFirst("tenant_schema")?.Value;
            if (string.IsNullOrEmpty(schema) || !Guid.TryParse(context.Principal?.FindFirst("sub")?.Value, out var userId))
            {
                context.Fail("Not a client-portal token.");
                return;
            }
            var reader = context.HttpContext.RequestServices.GetRequiredService<ContactConnection.Infrastructure.ClientPortal.ClientUserStatusReader>();
            if (!await reader.IsActiveAsync(schema, userId, context.HttpContext.RequestAborted))
                context.Fail("This account is no longer active.");
        },
    };
});

// Client-portal sign-in is reachable by people outside the tenant — throttle it per IP.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("client-auth", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ClientUser", policy => policy
        .AddAuthenticationSchemes(ClientUserTokens.Scheme)
        .RequireAuthenticatedUser()
        .RequireClaim("role", ClientUserTokens.Role));
    options.AddPolicy("ClientMfaPending", policy => policy
        .AddAuthenticationSchemes(ClientUserTokens.Scheme)
        .RequireAuthenticatedUser()
        .RequireClaim("role", ClientUserTokens.MfaPendingRole));
    options.AddPolicy("PlatformAdmin", policy =>
        policy.RequireClaim("role", "platform_admin"));
    options.AddPolicy("TenantAdmin", policy =>
        policy.RequireAssertion(ctx =>
        {
            var perms = (ctx.User.FindFirst("permissions")?.Value ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries);
            return perms.Any(p => p is
                Permission.AgentsManage   or Permission.RolesManage    or
                Permission.FlowsManage    or Permission.TelephonyManage or
                Permission.IntegrationsManage);
        }));
    options.AddPolicy("AgentsView", policy =>
        policy.RequireAssertion(ctx =>
            (ctx.User.FindFirst("permissions")?.Value ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Contains(Permission.AgentsView)));
    options.AddPolicy("BlocklistView", policy =>
        policy.RequireAssertion(ctx =>
            (ctx.User.FindFirst("permissions")?.Value ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Contains(Permission.BlocklistView)));
    options.AddPolicy("BlocklistManage", policy =>
        policy.RequireAssertion(ctx =>
            (ctx.User.FindFirst("permissions")?.Value ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Contains(Permission.BlocklistManage)));
    // Supervisor Dashboards (Session 92) â€” the "reports.*" permissions already existed in the
    // catalog and were already granted to the built-in Supervisor role, but were never actually
    // enforced anywhere. Wiring them here closes that gap now that the feature has its own
    // admin-dashboard entry point.
    options.AddPolicy("ReportsView", policy =>
        policy.RequireAssertion(ctx =>
            (ctx.User.FindFirst("permissions")?.Value ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Contains(Permission.ReportsView)));
    options.AddPolicy("ChatManage", policy =>
        policy.RequireAssertion(ctx =>
            (ctx.User.FindFirst("permissions")?.Value ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Contains(Permission.ChatManage)));
    options.AddPolicy("HelpdeskManage", policy =>
        policy.RequireAssertion(ctx =>
            (ctx.User.FindFirst("permissions")?.Value ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Contains(Permission.HelpdeskManage)));
    options.AddPolicy("ReportsManage", policy =>
        policy.RequireAssertion(ctx =>
            (ctx.User.FindFirst("permissions")?.Value ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Contains(Permission.ReportsManage)));
    options.AddPolicy("MfaPending", policy =>
        policy.RequireClaim("role", "mfa_pending"));
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();
app.UseWebSockets();
app.UseAuthentication();
app.UseAuthorization();
app.UseTenantResolution();
app.UseRateLimiter();

app.MapAuthEndpoints();
app.MapAgentsEndpoints();
app.MapTenantsEndpoints();
app.MapCallRecordsEndpoints();
app.MapCallReviewEndpoints();
app.MapAgentLockEndpoints();
app.MapSupervisorEndpoints();
app.MapCampaignCredentialsEndpoints();
app.MapSoftphoneOutboundEndpoints();
app.MapSoftphoneEndpoints();
app.MapMediaEndpoints();
app.MapCommissionsEndpoints();
app.MapAiEndpoints();
app.MapCallRecordingsEndpoints();
app.MapScreenRecordingsEndpoints();
app.MapVoicemailsEndpoints();
app.MapScheduledCallbacksEndpoints();
app.MapProductsEndpoints();
app.MapCategoriesEndpoints();
app.MapAttributesEndpoints();
app.MapOffersEndpoints();
app.MapOrdersEndpoints();
app.MapSubscriptionsEndpoints();
app.MapFlowsEndpoints();
app.MapFlowSessionsEndpoints();
app.MapCustomFieldsEndpoints();
app.MapSipGatewaysEndpoints();
app.MapClientsEndpoints();
app.MapCampaignsEndpoints();
app.MapCampaignExternalNumbersEndpoints();
app.MapPhoneNumbersEndpoints();
app.MapPhoneNumbersBulkEndpoints();
app.MapChatEndpoints();
app.MapAssignmentMatrixEndpoints();
app.MapNumberProvidersEndpoints();
app.MapExternalRoutingEndpoints();
app.MapAgentGroupsEndpoints();
app.MapBlockListEndpoints();
app.MapRolesEndpoints();
app.MapTelephonyEndpoints();
app.MapAgentStateEndpoints();
app.MapAudioFilesEndpoints();
app.MapTtsServiceStatusEndpoints();
app.MapCallTracesEndpoints();
app.MapDashboardsEndpoints();
app.MapDashboardWidgetsEndpoints();
app.MapActiveCallsWidget();
app.MapExportsEndpoints();
app.MapExportKeysEndpoints();
app.MapDispositionsEndpoints();
app.MapKpiEndpoints();
app.MapRecordsWidgetEndpoints();
app.MapScriptIntegrationsEndpoints();
app.MapCallerHistoryEndpoints();
app.MapAgentDedicationsEndpoints();
app.MapFlowDocumentEndpoints();
app.MapCoachingNotesEndpoints();
app.MapAgentHealthEndpoints();
app.MapRemoteActionsEndpoints();
app.MapHelpdeskEndpoints();
app.MapWidgetFilterOptions();
app.MapClientPortalAuthEndpoints();
app.MapClientPortalEndpoints();
app.MapAdminClientUsersEndpoints();

// Tenant admin portal
app.MapAdminAgentsEndpoints();
app.MapAdminApiDefinitionsEndpoints();
app.MapAdminApiEndpointsEndpoints();
app.MapAdminApiPreferencesEndpoints();
app.MapAdminCredentialsEndpoints();
app.MapAdminTtsProvidersEndpoints();
app.MapAdminSttProvidersEndpoints();
app.MapAdminWebhooksEndpoints();

// Portal (platform administration)
app.MapPortalAuthEndpoints();
app.MapPortalTenantsEndpoints();
app.MapPortalInvoicesEndpoints();
app.MapBillingEndpoints();
app.MapPortalApiDefinitionsEndpoints();
app.MapPortalApiEndpointsEndpoints();
app.MapApiTemplateEndpoints();
app.MapPortalTtsProvidersEndpoints();
app.MapPortalSttProvidersEndpoints();
app.MapPortalCredentialsEndpoints();
app.MapPortalMaintenanceEndpoints();

// Tenant onboarding and agent invite acceptance (public â€” no auth required)
app.MapOnboardingEndpoints();
app.MapTenantAdminInviteEndpoints();

// FreeSWITCH internal endpoints (no bearer auth â€” internal network only)
app.MapFreeSwitchDirectoryEndpoints();
app.MapTtsStreamRelayEndpoints();
app.MapSttStreamRelayEndpoints();

// Inbound vendor webhooks (public â€” authenticated via per-endpoint HMAC signature, not bearer JWT)
app.MapWebhooksEndpoints();

// SignalR hubs
app.MapHub<FlowHub>("/hubs/flow");
app.MapHub<CallTraceHub>("/hubs/call-trace");
app.MapHub<ClientDashboardHub>("/hubs/client");
app.MapHub<ChatHub>("/hubs/chat");
app.MapHub<ScreenViewHub>("/hubs/screen-view");

app.Run();
