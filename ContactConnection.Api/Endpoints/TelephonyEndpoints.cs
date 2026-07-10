using System.Text.Json;
using ContactConnection.Api.Hubs;
using ContactConnection.Api.Telephony;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Api.Endpoints;

public static class TelephonyEndpoints
{
    public static IEndpointRouteBuilder MapTelephonyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/telephony").RequireAuthorization();
        group.MapPost("answer-queued-call", AnswerQueuedCall);
        group.MapPost("originate-test",     OriginateTest);
        return app;
    }

    private static async Task<IResult> OriginateTest(
        OriginateTestRequest req,
        IConfiguration config,
        CancellationToken ct)
    {
        var host    = config["FreeSWITCH:Host"]        ?? "127.0.0.1";
        var port    = int.Parse(config["FreeSWITCH:EslPort"]     ?? "8021");
        var pass    = config["FreeSWITCH:EslPassword"] ?? "ClueCon";
        var sipHost = req.SipHost ?? config["FreeSWITCH:SipHost"] ?? host;
        var sipPort = req.SipPort ?? config["FreeSWITCH:SipPort"] ?? "5060";

        var command = $"originate {{origination_caller_id_number={req.CallerIdNumber}}}" +
                      $"sofia/internal/{req.DestinationNumber}@{sipHost}:{sipPort} &park()";

        await using var esl = new EslClient();
        await esl.ConnectAsync(host, port, pass, ct);
        var result = await esl.RunCommandAsync(command, ct);

        var success = result?.StartsWith("+OK") == true;
        return success
            ? Results.Ok(new { success = true, result, command })
            : Results.BadRequest(new { success = false, result, command });
    }

    /// <summary>
    /// Called when an agent picks up a queued inbound call.
    ///
    /// Two paths depending on whether the flow has a tf_on_agent_selected event branch:
    ///
    /// No agent_selected branch (simple path):
    ///   Bridge caller → agent immediately, fire "agent_answer" event, return CRM session if any.
    ///
    /// Has agent_selected branch (whisper path):
    ///   Originate to agent with auto-answer so JsSIP accepts silently → park agent channel →
    ///   store _agent_uuid + _pending_agent_id/_pending_interaction_id in caller session →
    ///   fire "agent_selected" event branch (may include tf_whisper) → return NoContent.
    ///   The bridge happens later inside TelEndNodeHandler (BridgeChannelsAsync).
    ///   CHANNEL_BRIDGE then fires "agent_answer" and pushes the CRM script pop via SignalR.
    /// </summary>
    private static async Task<IResult> AnswerQueuedCall(
        AnswerQueuedCallRequest req,
        HttpContext http,
        ITenantDbContextFactory dbFactory,
        ITelephonyFlowEngine telephonyFlowEngine,
        ITelephonyCallSessionStore sessionStore,
        IFlowEngine flowEngine,
        IHubContext<FlowHub, IFlowHubClient> hub,
        IAgentStateStore stateStore,
        IConfiguration config,
        CancellationToken ct)
    {
        var agentIdStr      = http.User.FindFirst("sub")?.Value;
        var tenantIdStr     = http.User.FindFirst("tenant_id")?.Value;
        var tenantSchema    = http.User.FindFirst("tenant_schema")?.Value;
        var tenantSubdomain = http.User.FindFirst("tenant_subdomain")?.Value;

        if (!Guid.TryParse(agentIdStr, out var agentId) ||
            !Guid.TryParse(tenantIdStr, out var tenantId) ||
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

        var callerUuid = record.ContactIdExternal;

        // Assign this agent to the call record and create the interaction
        record.SetAgent(agentId);
        var interaction = record.AddInteraction(InteractionType.CustomerService);
        db.CallInteractions.Add(interaction);
        await db.SaveChangesAsync(ct);

        var host = config["FreeSWITCH:Host"] ?? "127.0.0.1";
        var port = int.Parse(config["FreeSWITCH:EslPort"] ?? "8021");
        var pass = config["FreeSWITCH:EslPassword"] ?? "ClueCon";

        await using var esl = new EslClient();
        await esl.ConnectAsync(host, port, pass, ct);

        // Check whether the telephony flow has an agent_selected event branch
        var session         = await sessionStore.GetAsync(callerUuid, ct);
        var hasAgentSelected = session?.EventHandlers.ContainsKey("agent_selected") == true;

        if (hasAgentSelected)
        {
            // Originate to agent with auto-answer and park — bridge happens later via TelEndNodeHandler
            var (agentUuid, originateError) = await esl.OriginateAndParkAsync(
                agent.SipExtension, tenantSubdomain, record.CallerId ?? "Unknown", ct);

            if (agentUuid is null)
            {
                // Undo the interaction — bridge never happened, call stays in queue
                db.CallInteractions.Remove(interaction);
                await db.SaveChangesAsync(ct);
                return Results.Problem(
                    detail: "Your softphone could not be reached. Make sure the softphone is registered and try again.",
                    statusCode: 400);
            }

            // Dequeue — prevents QueuePollingService from re-delivering this call
            session!.Vars.Remove("_queued");
            // _assigned_agent_id persists through bridge for hangup cleanup
            session.Vars["_assigned_agent_id"]       = agentId.ToString();
            session.Vars["_agent_uuid"]              = agentUuid;
            session.Vars["_pending_agent_id"]        = agentId.ToString();
            session.Vars["_pending_interaction_id"]  = interaction.Id.ToString();
            await sessionStore.SaveAsync(session, ct);

            // Mark agent as on-call so QueuePollingService skips them + UI updates
            await stateStore.SetAsync(tenantId, agentId,
                new AgentStateEntry(AgentStateCodes.OnCall, "On Call", null, DateTimeOffset.UtcNow), ct);
            await hub.Clients.Group($"agent:{agentId}").ReceiveAgentStateChange(AgentStateCodes.OnCall, "On Call", null);

            // Store reverse mapping so PLAYBACK_STOP on the agent channel can find the caller session
            await sessionStore.SetKeyAsync($"whisper:{agentUuid}", callerUuid, TimeSpan.FromMinutes(10), ct);

            // Fire the agent_selected branch (may include tf_whisper, tf_script_pop, etc.)
            var agentSelectedResult = await telephonyFlowEngine.FireEventAsync(
                callerUuid, "agent_selected",
                new FireEventContext
                {
                    AgentId       = agentId,
                    InteractionId = interaction.Id,
                    FlowEngine    = flowEngine,
                    Esl           = esl,
                }, ct);

            // Push CRM script pop immediately if tf_script_pop ran in the agent_selected branch
            // (e.g. before or after whisper so agent has the script while the whisper plays).
            if (agentSelectedResult.CrmFlowSession is not null)
            {
                var sessionJson = JsonSerializer.Serialize(
                    agentSelectedResult.CrmFlowSession,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                await hub.Clients.Group($"agent:{agentId}").ReceiveScriptPop(sessionJson);
            }

            return Results.NoContent();
        }
        else
        {
            // Simple path: store pending IDs so CHANNEL_BRIDGE fires agent_answer (same as whisper path)
            if (session is not null)
            {
                session.Vars.Remove("_queued");
                session.Vars["_assigned_agent_id"]       = agentId.ToString();
                session.Vars["_pending_agent_id"]        = agentId.ToString();
                session.Vars["_pending_interaction_id"]  = interaction.Id.ToString();
                await sessionStore.SaveAsync(session, ct);
            }

            await stateStore.SetAsync(tenantId, agentId,
                new AgentStateEntry(AgentStateCodes.OnCall, "On Call", null, DateTimeOffset.UtcNow), ct);
            await hub.Clients.Group($"agent:{agentId}").ReceiveAgentStateChange(AgentStateCodes.OnCall, "On Call", null);

            await esl.BridgeToAgentAsync(callerUuid, agent.SipExtension, tenantSubdomain, record.CallerId ?? "Unknown", ct);

            return Results.NoContent();
        }
    }
}

public record AnswerQueuedCallRequest(Guid CallRecordId);
public record AnswerQueuedCallResponse(object FlowSession);
public record OriginateTestRequest(
    string CallerIdNumber,
    string DestinationNumber,
    string? SipHost = null,
    string? SipPort = null);
