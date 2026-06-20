using ContactConnection.Application.Helpers;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace ContactConnection.Api.Endpoints;

public static class AgentsEndpoints
{
    public static IEndpointRouteBuilder MapAgentsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/agents").RequireAuthorization();

        group.MapGet("{id:guid}", GetById);
        group.MapPost("", Create).AllowAnonymous(); // Bootstrap: first agent has no JWT yet
        group.MapPost("{id:guid}/sip-credentials/reset", ResetSipCredentials)
            .RequireAuthorization("TenantAdmin");

        return app;
    }

    private static async Task<IResult> GetById(
        Guid id,
        IAgentRepository agents,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();

        var agent = await agents.GetByIdAsync(id, ct);
        return agent is null ? Results.NotFound() : Results.Ok(ToResponse(agent));
    }

    private static async Task<IResult> Create(
        CreateAgentRequest request,
        IAgentRepository agents,
        IPasswordHasher hasher,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();

        if (!AgentRole.IsValid(request.Role))
            return Results.BadRequest(new { error = $"Invalid role '{request.Role}'. Valid: {string.Join(", ", AgentRole.All)}" });

        if (await agents.EmailExistsAsync(request.Email, ct))
            return Results.Conflict(new { error = $"Email '{request.Email}' is already registered." });

        var agent = Agent.Create(
            tenantContext.Current!.Id,
            request.FirstName,
            request.LastName,
            request.Email,
            hasher.Hash(request.Password),
            request.Role);

        // Auto-assign SIP extension (starts at 1000, increments per tenant)
        var maxExt = await agents.GetMaxSipExtensionAsync(ct);
        var sipExt = (maxExt + 1).ToString();
        var sipPwd = SipCredentialHelper.GeneratePassword();
        var a1hash = SipCredentialHelper.ComputeA1Hash(sipExt, tenantContext.Current.Subdomain, sipPwd);
        agent.SetSipCredentials(sipExt, a1hash);

        await agents.AddAsync(agent, ct);
        await agents.SaveChangesAsync(ct);

        return Results.Created($"/api/v1/agents/{agent.Id}", new
        {
            agent.Id,
            agent.TenantId,
            agent.FirstName,
            agent.LastName,
            agent.Email,
            agent.Role,
            agent.IsActive,
            agent.CreatedAt,
            agent.LastLoginAt,
            agent.SipExtension,
            SipPassword = sipPwd  // plaintext returned once only
        });
    }

    private static async Task<IResult> ResetSipCredentials(
        Guid id,
        IAgentRepository agents,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();

        var agent = await agents.GetByIdAsync(id, ct);
        if (agent is null) return Results.NotFound();

        // Preserve existing extension; issue a fresh password and recompute a1hash
        var sipExt = agent.SipExtension
            ?? ((await agents.GetMaxSipExtensionAsync(ct)) + 1).ToString();
        var sipPwd = SipCredentialHelper.GeneratePassword();
        var a1hash = SipCredentialHelper.ComputeA1Hash(sipExt, tenantContext.Current!.Subdomain, sipPwd);
        agent.SetSipCredentials(sipExt, a1hash);

        await agents.SaveChangesAsync(ct);

        return Results.Ok(new { SipExtension = sipExt, SipPassword = sipPwd });
    }

    private static object ToResponse(Agent a) => new
    {
        a.Id,
        a.TenantId,
        a.FirstName,
        a.LastName,
        a.Email,
        a.Role,
        a.IsActive,
        a.CreatedAt,
        a.LastLoginAt,
        a.SipExtension
        // PasswordHash and SipA1Hash intentionally excluded
    };
}

public record CreateAgentRequest(
    string FirstName,
    string LastName,
    string Email,
    string Password,
    string Role);
