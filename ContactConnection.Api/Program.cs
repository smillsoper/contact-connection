using System.Text;
using System.Text.Json.Serialization;
using ContactConnection.Api.Endpoints;
using ContactConnection.Api.Hubs;
using ContactConnection.Api.Middleware;
using ContactConnection.Api.Telephony;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddInfrastructure(builder.Configuration);

// Serialize enums as strings globally — makes API requests/responses human-readable
// (e.g. "Available" instead of 0 for ProductInventoryStatus)
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// SignalR — must be registered before IFlowNotifier which depends on IHubContext
builder.Services.AddSignalR();
builder.Services.AddScoped<IFlowNotifier, FlowNotifier>();

// ESL background service — connects to FreeSWITCH and handles CHANNEL_PARK / CHANNEL_HANGUP
builder.Services.AddHostedService<EslBackgroundService>();

// JWT Bearer authentication
var signingKey = builder.Configuration["Jwt:SigningKey"]
    ?? throw new InvalidOperationException("Jwt:SigningKey is not configured.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Keep JWT claim names as-is (don't map "sub" → ClaimTypes.NameIdentifier, etc.)
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
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("PlatformAdmin", policy =>
        policy.RequireClaim("role", "platform_admin"));
    options.AddPolicy("TenantAdmin", policy =>
        policy.RequireClaim("role", "admin", "supervisor"));
    options.AddPolicy("AgentsView", policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.HasClaim("role", "admin") ||
            ctx.User.HasClaim("role", "supervisor") ||
            (ctx.User.FindFirst("permissions")?.Value ?? "").Split(',').Contains("agents.view")));
    options.AddPolicy("MfaPending", policy =>
        policy.RequireClaim("role", "mfa_pending"));
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseTenantResolution();

app.MapAuthEndpoints();
app.MapAgentsEndpoints();
app.MapTenantsEndpoints();
app.MapCallRecordsEndpoints();
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
app.MapAgentGroupsEndpoints();
app.MapBlockListEndpoints();
app.MapRolesEndpoints();

// Tenant admin portal
app.MapAdminAgentsEndpoints();
app.MapAdminApiDefinitionsEndpoints();
app.MapAdminApiEndpointsEndpoints();
app.MapAdminApiPreferencesEndpoints();
app.MapAdminCredentialsEndpoints();

// Portal (platform administration)
app.MapPortalAuthEndpoints();
app.MapPortalTenantsEndpoints();
app.MapPortalApiDefinitionsEndpoints();
app.MapPortalApiEndpointsEndpoints();
app.MapPortalCredentialsEndpoints();
app.MapPortalMaintenanceEndpoints();

// Tenant onboarding and agent invite acceptance (public — no auth required)
app.MapOnboardingEndpoints();
app.MapTenantAdminInviteEndpoints();

// FreeSWITCH internal endpoints (no bearer auth — internal network only)
app.MapFreeSwitchDirectoryEndpoints();

// SignalR hub
app.MapHub<FlowHub>("/hubs/flow");

app.Run();
