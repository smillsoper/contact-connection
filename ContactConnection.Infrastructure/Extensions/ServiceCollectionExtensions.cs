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

        // Variable resolver (singleton — stateless, thread-safe regex engine)
        services.AddSingleton<IVariableResolver, VariableResolver>();

        // Executes "general" API Definition calls for both flow engines' api_call nodes
        // (uses IHttpClientFactory, no per-tenant state — safe as scoped or singleton).
        services.AddScoped<IApiDefinitionExecutor, ApiDefinitionExecutor>();

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

            services.AddSingleton<IPortalCredentialStore, KeyVaultPortalCredentialStore>();
            services.AddScoped<ITenantCredentialStore, KeyVaultTenantCredentialStore>();
        }
        else
        {
            // No Key Vault configured — register a no-op so credential endpoints start up.
            // Write operations will return 500 until KeyVault:VaultUri is set in secrets.
            var nullStore = new NullCredentialStore();
            services.AddSingleton<IPortalCredentialStore>(nullStore);
            services.AddSingleton<ITenantCredentialStore>(nullStore);
        }

        return services;
    }
}
