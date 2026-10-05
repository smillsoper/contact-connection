using System.Globalization;
using ContactConnection.Api.Telephony;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Telephony.Outbound;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// Manual outbound from the agent softphone (S179, Sprint 1 item 1b, Slice A) — the Place call panel's choices,
/// placing the call server-side, the Internal directory with presence — plus the admin settings behind it (tenant
/// default outbound caller ID, campaign calling hours).
/// </summary>
public static class SoftphoneOutboundEndpoints
{
    public static IEndpointRouteBuilder MapSoftphoneOutboundEndpoints(this IEndpointRouteBuilder app)
    {
        var softphone = app.MapGroup("/api/v1/softphone").RequireAuthorization();
        softphone.MapGet("outbound-options", Options);
        softphone.MapPost("outbound-dial", Dial);
        softphone.MapGet("internal-directory", InternalDirectory);

        var admin = app.MapGroup("/api/v1").RequireAuthorization("TenantAdmin");
        admin.MapGet("telephony/outbound-settings", GetSettings);
        admin.MapPut("telephony/outbound-settings", PutSettings);
        admin.MapPut("campaigns/{id:guid}/outbound-hours", PutCampaignHours);
        return app;
    }

    private static bool Has(HttpContext http, string permission) =>
        (http.User.FindFirst("permissions")?.Value ?? "").Split(',').Contains(permission, StringComparer.OrdinalIgnoreCase);

    private static bool TryAgent(HttpContext http, out Guid id) => Guid.TryParse(http.User.FindFirst("sub")?.Value, out id);

    // ── GET /api/v1/softphone/outbound-options ──────────────────────────────
    // Clients → the agent's manual outbound campaigns, each with the caller ID it will present (the same server rule the
    // dial uses), and Direct dial when the role allows it.
    private static async Task<IResult> Options(HttpContext http, IManualOutboundService outbound, TenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.HasTenant || !TryAgent(http, out var agentId)) return Results.Unauthorized();
        var o = await outbound.GetOptionsAsync(agentId, Has(http, Permission.DirectDial), ct);
        return Results.Ok(new
        {
            clients = o.Clients.Select(c => new
            {
                c.ClientId, c.Name,
                campaigns = c.Campaigns.Select(k => new
                {
                    k.CampaignId, k.Name, k.CallerId,
                    callerIdDisplay = k.CallerId is null ? null : OutboundNumber.Display(k.CallerId),
                    k.CallerIdIsTenantDefault, k.HoursStart, k.HoursEnd,
                }),
            }),
            o.CanDirectDial,
            directDialCallerId = o.DirectDialCallerId,
            directDialCallerIdDisplay = o.DirectDialCallerId is null ? null : OutboundNumber.Display(o.DirectDialCallerId),
        });
    }

    // ── POST /api/v1/softphone/outbound-dial ────────────────────────────────
    // { campaignId?, number } — campaignId null = direct dial. Rings the agent's softphone, then the customer.
    private static async Task<IResult> Dial(OutboundDialBody body, HttpContext http, ManualOutboundCallService calls,
        IAgentRepository agents, TenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.HasTenant || !TryAgent(http, out var agentId)) return Results.Unauthorized();
        var agent = await agents.GetByIdAsync(agentId, ct);
        if (agent is null) return Results.Unauthorized();
        var result = await calls.DialAsync(agent, Has(http, Permission.DirectDial), body.CampaignId, body.Number ?? "", ct);
        return result.Ok ? Results.Ok(result.Data) : Results.BadRequest(new { error = result.Error });
    }

    // ── GET /api/v1/softphone/internal-directory ────────────────────────────
    // Users whose role is "Included in softphone internal dial list", with presence, for the Internal section.
    private static async Task<IResult> InternalDirectory(HttpContext http, IAgentRepository agents, IAgentStateStore states,
        IAgentRegistrationStore registrations, TenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.HasTenant || !TryAgent(http, out var me)) return Results.Unauthorized();
        var tenantId = tenantContext.Current!.Id;
        var result = new List<object>();
        foreach (var a in await agents.GetAllAsync(ct))
        {
            if (a.Id == me || !a.IsActive || string.IsNullOrWhiteSpace(a.SipExtension)) continue;
            var permissions = a.CustomRole?.Permissions ?? Permission.ForLegacyRole(a.Role).ToList();
            if (!permissions.Contains(Permission.InternalDialList)) continue;
            var state = await states.GetAsync(tenantId, a.Id, ct);
            result.Add(new
            {
                agentId = a.Id, name = a.FullName, extension = a.SipExtension,
                stateCode = state?.Code ?? AgentStateCodes.LoggedOut,
                stateLabel = state?.Label ?? "Logged Out",
                registered = registrations.Get(tenantId, a.SipExtension!) is not null,
            });
        }
        return Results.Ok(result);
    }

    // ── Tenant default outbound caller ID ───────────────────────────────────
    private static IResult GetSettings(TenantContext tenantContext) =>
        !tenantContext.HasTenant ? Results.Unauthorized()
            : Results.Ok(new { defaultOutboundCallerId = tenantContext.Current!.Settings.DefaultOutboundCallerId });

    private static async Task<IResult> PutSettings(OutboundSettingsBody body, TenantContext tenantContext,
        ContactConnectionDbContext platformDb, CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        string? callerId = null;
        if (!string.IsNullOrWhiteSpace(body.DefaultOutboundCallerId))
        {
            callerId = OutboundNumber.ToE164(body.DefaultOutboundCallerId);
            if (callerId is null) return Results.BadRequest(new { error = "Enter a 10-digit US number." });
        }
        var row = await platformDb.Tenants.FirstOrDefaultAsync(t => t.Id == tenantContext.Current!.Id, ct);
        if (row is null) return Results.NotFound();
        var settings = row.Settings.Clone();
        settings.DefaultOutboundCallerId = callerId;
        row.UpdateSettings(settings);
        await platformDb.SaveChangesAsync(ct);
        tenantContext.Current = row;
        return Results.Ok(new { defaultOutboundCallerId = callerId });
    }

    // ── PUT /api/v1/campaigns/{id}/outbound-hours ───────────────────────────
    // { start: "09:00", end: "20:00" } in the callee's local time; both null = the 8 AM – 9 PM default.
    private static async Task<IResult> PutCampaignHours(Guid id, OutboundHoursBody body, ICampaignRepository campaigns,
        TenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var campaign = await campaigns.GetByIdAsync(id, ct);
        if (campaign is null) return Results.NotFound();
        static TimeOnly? Parse(string? s) =>
            TimeOnly.TryParseExact(s?.Trim(), ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;
        var start = string.IsNullOrWhiteSpace(body.Start) ? null : Parse(body.Start);
        var end = string.IsNullOrWhiteSpace(body.End) ? null : Parse(body.End);
        if ((!string.IsNullOrWhiteSpace(body.Start) && start is null) || (!string.IsNullOrWhiteSpace(body.End) && end is null))
            return Results.BadRequest(new { error = "Use HH:mm times, e.g. 09:00." });
        try { campaign.SetOutboundHours(start, end); }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        await campaigns.SaveChangesAsync(ct);
        return Results.Ok(new
        {
            outboundHoursStart = campaign.OutboundHoursStart?.ToString("HH:mm"),
            outboundHoursEnd = campaign.OutboundHoursEnd?.ToString("HH:mm"),
        });
    }
}

public record OutboundDialBody(Guid? CampaignId, string? Number);
public record OutboundSettingsBody(string? DefaultOutboundCallerId);
public record OutboundHoursBody(string? Start, string? End);
