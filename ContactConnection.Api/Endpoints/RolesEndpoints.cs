using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;

namespace ContactConnection.Api.Endpoints;

public static class RolesEndpoints
{
    public static IEndpointRouteBuilder MapRolesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/roles").RequireAuthorization();

        group.MapGet("",        GetAll);
        group.MapGet("{id}",    GetById);
        group.MapPost("",       Create);
        group.MapPut("{id}",    Update);
        group.MapDelete("{id}", Delete);

        // Also expose the full permission catalogue so the frontend can build toggle UI
        app.MapGet("/api/v1/permissions", () =>
            Permission.All.Select(p => new { key = p, group = p.Split('.')[0] })
        ).RequireAuthorization();

        app.MapGet("/api/v1/landing-pages", () =>
            LandingPage.All.Select(p => new { key = p, label = LandingPageLabel(p) })
        ).RequireAuthorization();

        return app;
    }

    private static async Task<IResult> GetAll(IRoleRepository roles, TenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var list = await roles.GetAllAsync(ct);
        return Results.Ok(list.Select(ToResponse));
    }

    private static async Task<IResult> GetById(Guid id, IRoleRepository roles, TenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var role = await roles.GetByIdAsync(id, ct);
        return role is null ? Results.NotFound() : Results.Ok(ToResponse(role));
    }

    private static async Task<IResult> Create(
        CreateRoleRequest request,
        IRoleRepository roles,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();

        var role = Role.Create(
            tenantContext.Current!.Id,
            request.Name.Trim(),
            request.Permissions,
            request.DefaultLandingPage);

        await roles.AddAsync(role, ct);
        await roles.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/roles/{role.Id}", ToResponse(role));
    }

    private static async Task<IResult> Update(
        Guid id,
        UpdateRoleRequest request,
        IRoleRepository roles,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();

        var role = await roles.GetByIdAsync(id, ct);
        if (role is null) return Results.NotFound();

        // Built-in roles: allow permission + landing page changes, but keep the original name.
        var name = role.IsBuiltIn ? role.Name : request.Name.Trim();
        role.Update(name, request.Permissions, request.DefaultLandingPage);
        await roles.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(role));
    }

    private static async Task<IResult> Delete(
        Guid id,
        IRoleRepository roles,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();

        var role = await roles.GetByIdAsync(id, ct);
        if (role is null) return Results.NotFound();
        if (role.IsBuiltIn) return Results.BadRequest(new { error = "Built-in roles cannot be deleted." });

        roles.Delete(role);
        await roles.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static RoleResponse ToResponse(Role r) => new(
        r.Id, r.Name, r.IsBuiltIn, r.Permissions, r.DefaultLandingPage, r.CreatedAt, r.UpdatedAt);

    private static string LandingPageLabel(string key) => key switch
    {
        LandingPage.AgentPortal    => "Agent Portal",
        LandingPage.AdminDashboard => "Admin Dashboard",
        LandingPage.FlowDesigner   => "Flow Designer",
        LandingPage.Telephony      => "Telephony",
        LandingPage.Reports        => "Reports",
        _                          => key
    };
}

public record CreateRoleRequest(string Name, List<string> Permissions, string DefaultLandingPage);
public record UpdateRoleRequest(string Name, List<string> Permissions, string DefaultLandingPage);
public record RoleResponse(
    Guid Id,
    string Name,
    bool IsBuiltIn,
    List<string> Permissions,
    string DefaultLandingPage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
