using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;

namespace ContactConnection.Api.Endpoints;

public static class AgentStateEndpoints
{
    public static IEndpointRouteBuilder MapAgentStateEndpoints(this IEndpointRouteBuilder app)
    {
        var state = app.MapGroup("/api/v1/agent-state").RequireAuthorization();
        state.MapGet("",  GetState);
        state.MapPut("",  SetState);

        var codes = app.MapGroup("/api/v1/unavailable-codes").RequireAuthorization();
        codes.MapGet("",                      GetCodes);        // softphone — current user's role
        codes.MapGet("role/{roleId:guid}",    GetCodesForRole); // admin — all codes for a role
        codes.MapPost("",                     CreateCode);
        codes.MapPut("{id:guid}",             UpdateCode);
        codes.MapDelete("{id:guid}",          DeleteCode);

        return app;
    }

    // ── GET /api/v1/agent-state ──────────────────────────────────────────────

    private static async Task<IResult> GetState(
        HttpContext http,
        IAgentStateStore store,
        CancellationToken ct)
    {
        if (!TryGetAgentClaims(http, out var tenantId, out var agentId))
            return Results.Unauthorized();

        var state = await store.GetAsync(tenantId, agentId, ct);
        return Results.Ok(state ?? new AgentStateEntry(
            AgentStateCodes.Unavailable, "Unavailable", null, DateTimeOffset.UtcNow));
    }

    // ── PUT /api/v1/agent-state ──────────────────────────────────────────────

    private static async Task<IResult> SetState(
        SetAgentStateRequest req,
        HttpContext http,
        IAgentStateStore store,
        CancellationToken ct)
    {
        if (!TryGetAgentClaims(http, out var tenantId, out var agentId))
            return Results.Unauthorized();

        var tenantSchema = http.User.FindFirst("tenant_schema")?.Value;
        if (string.IsNullOrEmpty(tenantSchema))
            return Results.Unauthorized();

        var label = req.Code switch
        {
            AgentStateCodes.Available        => "Available",
            AgentStateCodes.UnavailableBreak => "Unavailable - Break",
            AgentStateCodes.UnavailableLunch => "Unavailable - Lunch",
            AgentStateCodes.Unavailable      => "Unavailable",
            AgentStateCodes.LoggedOut        => "Logged Out",
            _                                => req.CustomLabel ?? "Unavailable",
        };

        var entry = new AgentStateEntry(req.Code, label, req.CustomCodeId, DateTimeOffset.UtcNow);
        await store.SetAsync(tenantId, agentId, tenantSchema, entry, ct);
        return Results.Ok(entry);
    }

    // ── GET /api/v1/unavailable-codes ────────────────────────────────────────
    // Returns codes visible to the current agent based on their role_id claim.

    private static async Task<IResult> GetCodes(
        HttpContext http,
        ICustomUnavailableCodeRepository repo,
        CancellationToken ct)
    {
        var roleId = http.User.FindFirst("role_id")?.Value ?? "";
        var codes = await repo.GetForRoleAsync(roleId, ct);
        return Results.Ok(codes.Select(ToResponse));
    }

    // ── GET /api/v1/unavailable-codes/role/{roleId} ───────────────────────────
    // Admin endpoint — returns all active codes scoped to (or visible by) a role.

    private static async Task<IResult> GetCodesForRole(
        Guid roleId,
        HttpContext http,
        ICustomUnavailableCodeRepository repo,
        CancellationToken ct)
    {
        if (!HasPermission(http, Permission.RolesManage))
            return Results.Forbid();

        var codes = await repo.GetAllAsync(ct);
        var filtered = codes.Where(c => c.Roles.Length == 0 || c.Roles.Contains(roleId.ToString())).ToList();
        return Results.Ok(filtered.Select(ToResponse));
    }

    // ── POST /api/v1/unavailable-codes ───────────────────────────────────────

    private static async Task<IResult> CreateCode(
        UnavailableCodeRequest req,
        HttpContext http,
        ICustomUnavailableCodeRepository repo,
        CancellationToken ct)
    {
        if (!TryGetAgentClaims(http, out var tenantId, out _))
            return Results.Unauthorized();

        if (!HasPermission(http, Permission.RolesManage))
            return Results.Forbid();

        if (string.IsNullOrWhiteSpace(req.Name))
            return Results.BadRequest(new { error = "Name is required." });

        var code = CustomUnavailableCode.Create(tenantId, req.Name, req.Roles ?? []);
        await repo.AddAsync(code, ct);
        await repo.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/unavailable-codes/{code.Id}", ToResponse(code));
    }

    // ── PUT /api/v1/unavailable-codes/{id} ───────────────────────────────────

    private static async Task<IResult> UpdateCode(
        Guid id,
        UnavailableCodeRequest req,
        HttpContext http,
        ICustomUnavailableCodeRepository repo,
        CancellationToken ct)
    {
        if (!HasPermission(http, Permission.RolesManage))
            return Results.Forbid();

        var code = await repo.GetByIdAsync(id, ct);
        if (code is null) return Results.NotFound();

        if (string.IsNullOrWhiteSpace(req.Name))
            return Results.BadRequest(new { error = "Name is required." });

        code.Update(req.Name, req.Roles ?? []);
        if (req.IsActive == false) code.Deactivate();
        else code.Activate();

        await repo.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(code));
    }

    // ── DELETE /api/v1/unavailable-codes/{id} ────────────────────────────────

    private static async Task<IResult> DeleteCode(
        Guid id,
        HttpContext http,
        ICustomUnavailableCodeRepository repo,
        CancellationToken ct)
    {
        if (!HasPermission(http, Permission.RolesManage))
            return Results.Forbid();

        var code = await repo.GetByIdAsync(id, ct);
        if (code is null) return Results.NotFound();

        code.Deactivate();
        await repo.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool TryGetAgentClaims(HttpContext http, out Guid tenantId, out Guid agentId)
    {
        tenantId = default;
        agentId  = default;
        var tenantIdStr = http.User.FindFirst("tenant_id")?.Value;
        var agentIdStr  = http.User.FindFirst("sub")?.Value;
        return Guid.TryParse(tenantIdStr, out tenantId) && Guid.TryParse(agentIdStr, out agentId);
    }

    private static bool HasPermission(HttpContext http, string permission)
    {
        var permissions = http.User.FindFirst("permissions")?.Value ?? "";
        return permissions.Split(',').Contains(permission);
    }

    private static object ToResponse(CustomUnavailableCode c) => new
    {
        id       = c.Id,
        name     = c.Name,
        roles    = c.Roles,
        isActive = c.IsActive,
    };
}

public record SetAgentStateRequest(string Code, Guid? CustomCodeId = null, string? CustomLabel = null);
public record UnavailableCodeRequest(string Name, string[]? Roles, bool? IsActive);
