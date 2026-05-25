using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;

namespace ContactConnection.Api.Endpoints;

public static class FlowSessionsEndpoints
{
    public static void MapFlowSessionsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/flow-sessions").RequireAuthorization();

        // Start a new flow session against a call record + interaction
        group.MapPost("/", async (
            StartSessionRequest req,
            IFlowEngine engine,
            TenantContext tenantContext,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (tenantContext.Current is null) return Results.Unauthorized();

            var agentIdClaim = http.User.FindFirst("sub")?.Value;
            if (!Guid.TryParse(agentIdClaim, out var agentId))
                return Results.Unauthorized();

            try
            {
                var state = await engine.StartAsync(new StartFlowRequest
                {
                    FlowId        = req.FlowId,
                    CallRecordId  = req.CallRecordId,
                    InteractionId = req.InteractionId,
                    AgentId       = agentId,
                    TenantId      = tenantContext.Current.Id
                }, ct);

                return Results.Ok(state);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // Get current node state for an active session (reconnect / refresh)
        group.MapGet("/{id:guid}", async (
            Guid id,
            IFlowEngine engine,
            CancellationToken ct) =>
        {
            var state = await engine.GetCurrentStateAsync(id, ct);
            return state is null ? Results.NotFound() : Results.Ok(state);
        });

        // Advance the session — submit agent input and get next node state
        group.MapPost("/{id:guid}/advance", async (
            Guid id,
            AdvanceSessionRequest req,
            IFlowEngine engine,
            CancellationToken ct) =>
        {
            try
            {
                var state = await engine.AdvanceAsync(new AdvanceFlowRequest
                {
                    SessionId            = id,
                    InputValue           = req.InputValue,
                    Transition           = req.Transition ?? "default",
                    JumpToSectionNodeId  = req.JumpToSectionNodeId,
                }, ct);

                return Results.Ok(state);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
    }
}

public record StartSessionRequest(
    Guid FlowId,
    Guid CallRecordId,
    Guid InteractionId);

public record AdvanceSessionRequest(
    string? InputValue,
    string? Transition,
    string? JumpToSectionNodeId);
