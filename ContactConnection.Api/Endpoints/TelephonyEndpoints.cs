using ContactConnection.Api.Telephony;

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
    /// Called when an agent picks up a queued inbound call. The actual claim/deliver logic lives
    /// in QueuedCallDeliveryService, shared with the server-initiated (no HTTP round trip)
    /// delivery path RingStrategy.AutoAnswerBestAgent uses — see that class for the two
    /// delivery-path (simple vs. whisper) details.
    /// </summary>
    private static async Task<IResult> AnswerQueuedCall(
        AnswerQueuedCallRequest req,
        HttpContext http,
        QueuedCallDeliveryService deliveryService,
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

        var result = await deliveryService.DeliverAsync(
            tenantId, tenantSchema, tenantSubdomain, req.CallRecordId, agentId, ct);

        return result.Success ? Results.NoContent() : Results.Problem(detail: result.ErrorDetail, statusCode: 400);
    }
}

public record AnswerQueuedCallRequest(Guid CallRecordId);
public record AnswerQueuedCallResponse(object FlowSession);
public record OriginateTestRequest(
    string CallerIdNumber,
    string DestinationNumber,
    string? SipHost = null,
    string? SipPort = null);
