using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Infrastructure.Auth;
using ContactConnection.Infrastructure.Commerce;
using ContactConnection.Infrastructure.Credentials;
using ContactConnection.Infrastructure.CustomFields;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Email;
using ContactConnection.Infrastructure.FlowEngine;
using ContactConnection.Infrastructure.FlowEngine.NodeHandlers;
using ContactConnection.Infrastructure.FlowEngine.Services;
using ContactConnection.Infrastructure.Repositories;
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
        // Uses ClientSecretCredential with the same app registration as portal auth.
        // For production on Azure, swap ClientSecretCredential for ManagedIdentityCredential.
        var vaultUri = configuration["KeyVault:VaultUri"];
        if (!string.IsNullOrWhiteSpace(vaultUri))
        {
            var azureCredential = new ClientSecretCredential(
                configuration["EntraId:TenantId"]
                    ?? throw new InvalidOperationException("EntraId:TenantId is not configured."),
                configuration["EntraId:ClientId"]
                    ?? throw new InvalidOperationException("EntraId:ClientId is not configured."),
                configuration["EntraId:ClientSecret"]
                    ?? throw new InvalidOperationException("EntraId:ClientSecret is not configured."));

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
