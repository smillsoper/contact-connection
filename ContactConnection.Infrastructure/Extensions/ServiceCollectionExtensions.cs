using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Infrastructure.ApiExecution;
using ContactConnection.Infrastructure.Auth;
using ContactConnection.Infrastructure.CallTrace;
using ContactConnection.Infrastructure.Commerce;
using ContactConnection.Infrastructure.Credentials;
using ContactConnection.Infrastructure.CustomFields;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Email;
using ContactConnection.Infrastructure.FlowEngine;
using ContactConnection.Infrastructure.FlowEngine.NodeHandlers;
using ContactConnection.Infrastructure.FlowEngine.Services;
using ContactConnection.Infrastructure.Repositories;
using ContactConnection.Infrastructure.Telephony;
using ContactConnection.Infrastructure.Telephony.NodeHandlers;
using ContactConnection.Infrastructure.Tenants;
using ContactConnection.Infrastructure.Tts;
using ContactConnection.Infrastructure.Versioning;
using DnsClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace ContactConnection.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Platform-level DbContext — public schema, tenant table, migrations history
        services.AddDbContext<ContactConnectionDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));

        // Tenant-scoped DbContext — created lazily per request via factory using search_path.
        // NOT registered directly as TenantDbContext in DI — repositories receive
        // ScopedTenantDbContextFactory and call .Create() on first use, after
        // TenantResolutionMiddleware has populated TenantContext.
        services.AddSingleton<ITenantDbContextFactory, TenantDbContextFactory>();
        services.AddScoped<ScopedTenantDbContextFactory>();

        // Tenant context (scoped — holds current request's resolved Tenant)
        services.AddScoped<TenantContext>();

        // Repositories
        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<ICallRecordRepository, CallRecordRepository>();
        services.AddScoped<IAgentRepository, AgentRepository>();
        services.AddScoped<IFlowRepository, FlowRepository>();
        services.AddScoped<IDashboardRepository, DashboardRepository>();
        services.AddScoped<IFlowSessionRepository, FlowSessionRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IProductCategoryRepository, ProductCategoryRepository>();
        services.AddScoped<IProductAttributeRepository, ProductAttributeRepository>();
        services.AddScoped<IOfferRepository, OfferRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
        services.AddScoped<ICustomFieldDefinitionRepository, CustomFieldDefinitionRepository>();
        services.AddScoped<ICustomFieldValueRepository, CustomFieldValueRepository>();
        services.AddScoped<IDataTypeRepository, DataTypeRepository>();
        services.AddScoped<IPortalApiDefinitionRepository, PortalApiDefinitionRepository>();
        services.AddScoped<ITenantApiDefinitionRepository, TenantApiDefinitionRepository>();
        services.AddScoped<IPortalApiEndpointRepository, PortalApiEndpointRepository>();
        services.AddScoped<ITenantApiEndpointRepository, TenantApiEndpointRepository>();
        services.AddScoped<ITenantApiPreferenceRepository, TenantApiPreferenceRepository>();
        services.AddScoped<IWebhookEndpointRepository, WebhookEndpointRepository>();
        services.AddScoped<IWebhookEventRepository, WebhookEventRepository>();

        services.AddScoped<ITenantInviteRepository, TenantInviteRepository>();
        services.AddScoped<ITenantAdminInviteRepository, TenantAdminInviteRepository>();
        services.AddScoped<ISipGatewayRepository, SipGatewayRepository>();
        services.AddScoped<IClientRepository, ClientRepository>();
        services.AddScoped<ICampaignRepository, CampaignRepository>();
        services.AddScoped<IPhoneNumberRepository, PhoneNumberRepository>();
        services.AddScoped<IAgentGroupRepository, AgentGroupRepository>();
        services.AddScoped<IPhoneNumberRoutingRepository, PhoneNumberRoutingRepository>();

        // Platform auth
        services.AddScoped<IPlatformTokenService, PlatformJwtTokenService>();
        services.AddSingleton<IEntraIdTokenValidator, EntraIdTokenValidator>();
        services.AddSingleton<IMfaService, MfaService>();

        // Services
        services.AddScoped<ITenantProvisioningService, TenantProvisioningService>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddScoped<IPricingService, PricingService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<ISubscriptionOrderCreator, SubscriptionOrderCreator>();
        services.AddScoped<ICustomFieldService, CustomFieldService>();

        // Tax providers — each ITaxProvider is enumerated by TaxProviderFactory to build its dispatch table.
        // Register FlatRateTaxProvider first (it is the default/fallback).
        // Future: services.AddSingleton<ITaxProvider, AvalaraTaxProvider>();
        services.AddSingleton<ITaxProvider, FlatRateTaxProvider>();
        services.AddSingleton<ITaxProviderFactory, TaxProviderFactory>();

        // TTS streaming providers — each ITtsStreamProvider is enumerated by
        // TtsStreamProviderFactory to build its dispatch table. No default/fallback here
        // (unlike tax): a tenant with no TtsStreaming preference uses PlayNodeHandler's
        // built-in flite path instead, never reaching the factory at all.
        services.AddSingleton<ITtsStreamProvider, AzureTtsStreamProvider>();
        services.AddSingleton<ITtsStreamProvider, ElevenLabsTtsStreamProvider>();
        services.AddSingleton<ITtsStreamProviderFactory, TtsStreamProviderFactory>();

        // Variable resolver (singleton — stateless, thread-safe regex engine)
        services.AddSingleton<IVariableResolver, VariableResolver>();

        // Executes "general" API Definition calls for both flow engines' api_call nodes
        // (uses IHttpClientFactory, no per-tenant state — safe as scoped or singleton).
        services.AddScoped<IApiDefinitionExecutor, ApiDefinitionExecutor>();

        // Singleton — circuit breaker state must persist across calls for the process lifetime,
        // not be recreated per request/scope. Keyed internally per API Definition.
        services.AddSingleton<IVendorResilienceExecutor, VendorResilienceExecutor>();

        // Outbound rate limiting — Redis-backed (not in-memory, unlike the circuit breaker above)
        // because the whole point is a shared quota across every API instance, not just this one
        // process. See API_HARDENING_CHECKLIST.md Tier 2.
        services.AddSingleton<IOutboundRateLimiter, RedisOutboundRateLimiter>();

        // mTLS client-certificate HttpClient cache — singleton so the cached clients' TLS
        // connection pools persist for the process lifetime. See API_HARDENING_CHECKLIST.md Tier 3.
        services.AddSingleton<IMtlsHttpClientProvider, MtlsHttpClientProvider>();

        // Version history — one IVersionHistoryService implementation per scope (tenant/portal
        // persist to different DbContexts), resolved via keyed DI at each call site.
        services.AddKeyedScoped<IVersionHistoryService, TenantVersionHistoryService>("tenant");
        services.AddKeyedScoped<IVersionHistoryService, PortalVersionHistoryService>("portal");

        // Credential audit trail — same split, records THAT a credential Set/Delete happened
        // (actor/timestamp/key/action), never the secret value. Independent of whether Key Vault
        // is configured (it only ever runs after a store call already succeeded).
        services.AddKeyedScoped<ICredentialAuditService, TenantCredentialAuditService>("tenant");
        services.AddKeyedScoped<ICredentialAuditService, PortalCredentialAuditService>("portal");

        // DNS client for email validation (singleton — thread-safe, connection-pooled)
        services.AddSingleton<ILookupClient>(_ => new LookupClient(new LookupClientOptions
        {
            UseCache = true,
            CacheFailedResults = false,
            Timeout = TimeSpan.FromSeconds(5),
        }));
        services.AddSingleton<IEmailValidationService, EmailValidationService>();

        // Flow engine node handlers — each registered as INodeHandler so engine
        // receives IEnumerable<INodeHandler> and builds its dispatch dictionary
        services.AddScoped<INodeHandler, ScriptNodeHandler>();
        services.AddScoped<INodeHandler, InputNodeHandler>();
        services.AddScoped<INodeHandler, EmailNodeHandler>();
        services.AddScoped<INodeHandler, PhoneNodeHandler>();
        services.AddScoped<INodeHandler, AddressNodeHandler>();
        services.AddScoped<INodeHandler, SectionNodeHandler>();
        services.AddScoped<INodeHandler, ExecuteFlowNodeHandler>();
        services.AddScoped<INodeHandler, TransitionToFlowNodeHandler>();
        services.AddScoped<INodeHandler, BranchNodeHandler>();
        services.AddScoped<INodeHandler, SetVariableNodeHandler>();
        services.AddScoped<INodeHandler, ApiCallNodeHandler>();
        services.AddScoped<INodeHandler, EndNodeHandler>();

        // Flow engine (scoped — uses scoped repositories and tenant context)
        services.AddScoped<IFlowEngine, FlowEngine.FlowEngine>();

        // Telephony flow engine node handlers — registered as ITelephonyNodeHandler
        // (scoped; handlers that need DB use ITenantDbContextFactory directly)
        services.AddScoped<ITelephonyNodeHandler, CheckBlockListNodeHandler>();
        services.AddScoped<ITelephonyNodeHandler, CheckAgentAvailabilityNodeHandler>();
        services.AddScoped<ITelephonyNodeHandler, RejectNodeHandler>();
        services.AddScoped<ITelephonyNodeHandler, AnswerNodeHandler>();
        services.AddScoped<ITelephonyNodeHandler, HangupNodeHandler>();
        services.AddScoped<ITelephonyNodeHandler, RouteToQueueNodeHandler>();
        services.AddScoped<ITelephonyNodeHandler, TimeOfDayNodeHandler>();
        services.AddScoped<ITelephonyNodeHandler, TelBranchNodeHandler>();
        services.AddScoped<ITelephonyNodeHandler, TelEndNodeHandler>();
        services.AddScoped<ITelephonyNodeHandler, TelSetVariableNodeHandler>();
        services.AddScoped<ITelephonyNodeHandler, GetSipHeaderNodeHandler>();
        services.AddScoped<ITelephonyNodeHandler, SetSipHeaderNodeHandler>();
        services.AddScoped<ITelephonyNodeHandler, SetCallerIdNodeHandler>();
        services.AddScoped<ITelephonyNodeHandler, CancelDialNodeHandler>();
        services.AddScoped<ITelephonyNodeHandler, OnAgentSelectedNodeHandler>();
        services.AddScoped<ITelephonyNodeHandler, OnAgentAnswerNodeHandler>();
        services.AddScoped<ITelephonyNodeHandler, OnCallDisconnectedNodeHandler>();
        services.AddScoped<ITelephonyNodeHandler, OnCustomEventNodeHandler>();
        services.AddScoped<ITelephonyNodeHandler, ScriptPopNodeHandler>();
        services.AddScoped<ITelephonyNodeHandler, PlayNodeHandler>();
        services.AddScoped<ITelephonyNodeHandler, DtmfNodeHandler>();
        services.AddScoped<ITelephonyNodeHandler, WhisperNodeHandler>();
        services.AddScoped<ITelephonyNodeHandler, GeneralApiCallNodeHandler>();

        // Call session store (singleton — Redis operations are inherently stateless)
        services.AddSingleton<ITelephonyCallSessionStore, RedisCallSessionStore>();

        // Telephony flow engine (scoped — used by EslBackgroundService per call via IServiceScope)
        services.AddScoped<ITelephonyFlowEngine, TelephonyFlowEngine>();

        // Block list + roles repositories (scoped — for API endpoints with HTTP context)
        services.AddScoped<IBlockListRepository, BlockListRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<ICustomUnavailableCodeRepository, CustomUnavailableCodeRepository>();

        // Agent state store — Redis-backed, singleton (stateless Redis ops). It also persists
        // every transition to agent_state_history, so its repository must be singleton too
        // (a singleton cannot depend on a scoped service).
        services.AddSingleton<IAgentStateHistoryRepository, AgentStateHistoryRepository>();
        services.AddSingleton<IAgentStateStore, AgentStateStore>();

        // Call trace — persistence (scoped, EF) + subscription matching (singleton, Redis-backed
        // so state is consistent across API instances)
        services.AddScoped<ICallTraceEventRepository, CallTraceEventRepository>();
        services.AddScoped<ICallTraceRecorder, CallTraceRecorder>();
        services.AddSingleton<ICallTraceSubscriptionRegistry, RedisCallTraceSubscriptionRegistry>();

        // Call queue/routing state history — mirrors the call trace registration above
        services.AddScoped<ICallStateHistoryRepository, CallStateHistoryRepository>();
        services.AddScoped<ICallStateHistoryRecorder, CallStateHistoryRecorder>();

        // Email
        services.AddSingleton<IEmailService, ResendEmailService>();
        services.AddHttpClient("Resend");

        // HTTP client for ApiCallNodeHandler
        services.AddHttpClient("FlowEngine");

        // Redis — singleton connection multiplexer shared across all requests
        var redisConnection = configuration.GetConnectionString("Redis")
            ?? "localhost:6379";
        services.AddSingleton<IConnectionMultiplexer>(
            ConnectionMultiplexer.Connect(redisConnection));

        // Azure Key Vault — credential store for API authentication secrets.
        // See AzureCredentialFactory: prefers EntraId:ClientSecret (local dev) over
        // DefaultAzureCredential/Managed Identity (production). Same credential
        // Program.cs uses for AddAzureKeyVault.
        var vaultUri = configuration["KeyVault:VaultUri"];
        if (!string.IsNullOrWhiteSpace(vaultUri))
        {
            var azureCredential = AzureCredentialFactory.Resolve(configuration);

            services.AddSingleton<SecretClient>(_ =>
                new SecretClient(new Uri(vaultUri), azureCredential));

            // Registered under the "keyvault" key so the caching decorators below can reach
            // straight through to the real store without a circular self-resolution.
            services.AddKeyedSingleton<IPortalCredentialStore, KeyVaultPortalCredentialStore>("keyvault");
            services.AddKeyedScoped<ITenantCredentialStore, KeyVaultTenantCredentialStore>("keyvault");

            // Redis-backed caching in front of Key Vault — every api_key/bearer/basic auth call
            // and every oauth2 client-credential lookup goes through these, and a busy campaign
            // calling the same API definition repeatedly shouldn't hit Key Vault on every single
            // call. See CredentialCacheSupport for TTL + invalidate-on-write semantics.
            services.AddSingleton<IPortalCredentialStore, CachedPortalCredentialStore>();
            services.AddScoped<ITenantCredentialStore, CachedTenantCredentialStore>();
        }
        else
        {
            // No Key Vault configured — register a no-op so credential endpoints start up.
            // Write operations will return 500 until KeyVault:VaultUri is set in secrets.
            // Nothing worth caching in front of this — it already never touches the network.
            var nullStore = new NullCredentialStore();
            services.AddSingleton<IPortalCredentialStore>(nullStore);
            services.AddSingleton<ITenantCredentialStore>(nullStore);
        }

        // OAuth2 access token cache (Redis-backed) — see ApiDefinitionExecutor's "oauth2" auth
        // case. Registered unconditionally: harmless (just unused) when no oauth2-configured API
        // definition exists yet, and doesn't depend on Key Vault being configured.
        services.AddSingleton<IOAuth2TokenCache, RedisOAuth2TokenCache>();

        return services;
    }
}
