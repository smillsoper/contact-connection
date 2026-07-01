using ContactConnection.Api.Telephony;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Api.Endpoints;

public static class TelephonyEndpoints
{
    public static IEndpointRouteBuilder MapTelephonyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/telephony").RequireAuthorization();
        group.MapPost("answer-queued-call", AnswerQueuedCall);
        return app;
    }

    /// <summary>
    /// Called when an agent picks up a queued inbound call.
    /// 1. Bridges the parked FreeSWITCH channel to the agent's SIP extension.
    /// 2. Fires the "agent_answer" event on the live telephony flow session.
    /// 3. If the flow has a tf_script_pop node in the agent_answer branch, starts the CRM
    ///    flow session and returns it so the agent UI can auto-pop the script.
    /// </summary>
    private static async Task<IResult> AnswerQueuedCall(
        AnswerQueuedCallRequest req,
        HttpContext http,
        ITenantDbContextFactory dbFactory,
        ITelephonyFlowEngine telephonyFlowEngine,
        IFlowEngine flowEngine,
        IConfiguration config,
        CancellationToken ct)
    {
        var agentIdStr      = http.User.FindFirst("sub")?.Value;
        var tenantSchema    = http.User.FindFirst("tenant_schema")?.Value;
        var tenantSubdomain = http.User.FindFirst("tenant_subdomain")?.Value;

        if (!Guid.TryParse(agentIdStr, out var agentId) ||
            string.IsNullOrEmpty(tenantSchema) ||
            string.IsNullOrEmpty(tenantSubdomain))
            return Results.Unauthorized();

        await using var db = dbFactory.Create(tenantSchema);

        var agent = await db.Agents.FirstOrDefaultAsync(a => a.Id == agentId && a.IsActive, ct);
        if (agent is null) return Results.NotFound(new { error = "Agent not found." });
        if (string.IsNullOrEmpty(agent.SipExtension))
            return Results.BadRequest(new { error = "Agent has no SIP extension configured." });

        var record = await db.CallRecords.FirstOrDefaultAsync(r => r.Id == req.CallRecordId, ct);
        if (record is null) return Results.NotFound(new { error = "Call record not found." });
        if (string.IsNullOrEmpty(record.ContactIdExternal))
            return Results.BadRequest(new { error = "Call record has no associated channel." });

        // Assign this agent to the call record and create the interaction
        record.SetAgent(agentId);
        var interaction = record.AddInteraction(InteractionType.CustomerService);
        db.CallInteractions.Add(interaction);
        await db.SaveChangesAsync(ct);

        // Bridge the parked channel to the agent's SIP extension
        var host = config["FreeSWITCH:Host"] ?? "127.0.0.1";
        var port = int.Parse(config["FreeSWITCH:EslPort"] ?? "8021");
        var pass = config["FreeSWITCH:EslPassword"] ?? "ClueCon";

        await using var esl = new EslClient();
        await esl.ConnectAsync(host, port, pass, ct);
        await esl.BridgeToAgentAsync(record.ContactIdExternal, agent.SipExtension, tenantSubdomain, record.CallerId ?? "Unknown", ct);

        // Fire the agent_answer event — runs the tf_on_agent_answer branch (e.g. tf_script_pop)
        var fireResult = await telephonyFlowEngine.FireEventAsync(
            channelUuid: record.ContactIdExternal,
            eventName:   "agent_answer",
            new FireEventContext
            {
                AgentId       = agentId,
                InteractionId = interaction.Id,
                FlowEngine    = flowEngine,
            }, ct);

        if (fireResult.CrmFlowSession is not null)
            return Results.Ok(new AnswerQueuedCallResponse(fireResult.CrmFlowSession));

        return Results.NoContent();
    }
}

public record AnswerQueuedCallRequest(Guid CallRecordId);
public record AnswerQueuedCallResponse(object FlowSession);
