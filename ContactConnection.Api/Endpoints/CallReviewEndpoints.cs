using System.Security.Claims;
using System.Text.Json;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// Call Records admin (S165): find a past call, see everything on it, correct its data and re-run
/// its flow's API calls — built for the Life Seasons order-failure loop, where the flow emails a
/// reviewer when the Order API rejects an order and the reviewer fixes the data and resubmits.
///
/// Resubmitting re-runs the flow's own api_call node (IFlowEngine.RerunApiCallNodeAsync) rather
/// than a second order-submission path, so the payload is exactly the one the flow sends and the
/// node's oncePerCall guard still prevents a double post.
///
/// Viewing needs calls.view or calls.manage; every change needs calls.manage and is written to
/// call_record_audit_entries.
/// </summary>
public static class CallReviewEndpoints
{
    public static IEndpointRouteBuilder MapCallReviewEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/call-review/calls").RequireAuthorization();

        group.MapGet("", Search);
        group.MapGet("{id:guid}", GetDetail);
        group.MapPut("{id:guid}/contact", UpdateContact);
        group.MapPut("{id:guid}/addresses/{role}", UpdateAddress);
        group.MapPost("{id:guid}/cart/items", AddCartItem);
        group.MapPatch("{id:guid}/cart/items/{itemIndex:int}", UpdateCartItem);
        group.MapDelete("{id:guid}/cart/items/{itemIndex:int}", RemoveCartItem);
        group.MapPut("{id:guid}/custom-fields/{definitionId:guid}", UpdateCustomField);
        group.MapPut("{id:guid}/sessions/{sessionId:guid}/variables", UpdateVariables);
        group.MapPost("{id:guid}/sessions/{sessionId:guid}/api-calls/{nodeId}/rerun", RerunApiCall);

        return app;
    }

    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web);

    // ── GET /api/v1/call-review/calls ───────────────────────────────────────

    private static async Task<IResult> Search(
        DateTimeOffset? from, DateTimeOffset? to, Guid? campaignId, string? phone, string? orderNumber,
        string? name, bool? failedOnly, int? page, int? pageSize,
        HttpContext http,
        ICallRecordRepository callRecords,
        ICampaignRepository campaigns,
        IAgentRepository agents,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        if (!CanView(http)) return Results.Forbid();

        var size = Math.Clamp(pageSize ?? 50, 1, 200);
        var pageNo = Math.Max(1, page ?? 1);
        var result = await callRecords.SearchAsync(new CallRecordSearchCriteria(
            from, to, campaignId, phone, orderNumber, name, failedOnly == true,
            Skip: (pageNo - 1) * size, Take: size), ct);

        var failed = await callRecords.FindWithFailedApiCallsAsync(result.Items.Select(r => r.Id).ToList(), ct);
        var campaignNames = (await campaigns.GetAllAsync(null, ct)).ToDictionary(c => c.Id, c => c.Name);
        var agentNames = (await agents.GetAllAsync(ct)).ToDictionary(a => a.Id, a => a.FullName);

        return Results.Ok(new
        {
            total = result.Total,
            page = pageNo,
            pageSize = size,
            items = result.Items.Select(r => new
            {
                r.Id,
                r.CreatedAt,
                r.CallStartAt,
                r.CallEndAt,
                r.HandleTimeSeconds,
                r.CampaignId,
                campaignName = campaignNames.GetValueOrDefault(r.CampaignId),
                r.AgentId,
                agentName = r.AgentId is { } a ? agentNames.GetValueOrDefault(a) : null,
                r.Source,
                r.OverallStatus,
                r.CallerId,
                r.BillingPhone,
                r.ShippingPhone,
                customerName = FullName(r.FirstName, r.LastName),
                r.OrderNumber,
                cartTotal = r.Cart?.CartTotal,
                hasFailedApiCall = failed.Contains(r.Id),
            }),
        });
    }

    // ── GET /api/v1/call-review/calls/{id} ──────────────────────────────────

    private static async Task<IResult> GetDetail(
        Guid id,
        HttpContext http,
        ICallRecordRepository callRecords,
        IFlowSessionRepository sessions,
        IFlowRepository flows,
        IFlowEngine engine,
        IPaymentTransactionRepository payments,
        ICallRecordAuditRepository audit,
        ICampaignRepository campaigns,
        IClientRepository clients,
        IAgentRepository agents,
        ICustomFieldService customFields,
        ICustomFieldValueRepository customFieldValues,
        ICustomFieldDefinitionRepository customFieldDefinitions,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        if (!CanView(http)) return Results.Forbid();

        var r = await callRecords.GetByIdWithInteractionsAsync(id, ct);
        if (r is null) return Results.NotFound();

        var campaign = r.CampaignId == Guid.Empty ? null : await campaigns.GetByIdAsync(r.CampaignId, ct);
        var client = r.ClientId == Guid.Empty ? null : await clients.GetByIdAsync(r.ClientId, ct);
        var agent = r.AgentId is { } agentId ? await agents.GetByIdAsync(agentId, ct) : null;

        var sessionViews = new List<object>();
        foreach (var s in await sessions.GetByCallRecordAsync(id, ct))
        {
            var flow = await flows.GetByIdAsync(s.FlowId, ct);
            var snapshot = await engine.GetSessionSnapshotAsync(s.Id, ct);
            sessionViews.Add(new
            {
                s.Id,
                s.FlowId,
                flowName = flow?.Name,
                s.FlowVersion,
                s.Status,
                s.StartedAt,
                s.CompletedAt,
                isLive = snapshot?.IsLive ?? false,
                commitLabel = snapshot?.CommitLabel,
                flowVars = snapshot?.FlowVars ?? [],
                inputs = snapshot?.Inputs ?? [],
                apiCalls = snapshot?.ApiCalls ?? [],
            });
        }

        var txns = await payments.GetByCallRecordAsync(id, ct);
        var authorized = txns.LastOrDefault(t => t.Status == PaymentTransactionStatus.Approved && t.VoidedAt is null);

        return Results.Ok(new
        {
            r.Id,
            r.CreatedAt,
            r.UpdatedAt,
            r.CallStartAt,
            r.CallEndAt,
            r.HandleTimeSeconds,
            r.Source,
            r.RecordType,
            r.OverallStatus,
            r.ClientId,
            clientName = client?.Name,
            r.CampaignId,
            campaignName = campaign?.Name,
            r.AgentId,
            agentName = agent?.FullName,
            r.CallerId,
            r.Dnis,
            r.OrderNumber,
            r.RecordingUrl,
            contact = new
            {
                r.FirstName,
                r.LastName,
                r.Email,
                r.Phone,
                r.BillingPhone,
                r.ShippingPhone,
            },
            r.Addresses,
            r.Cart,
            authorizedAmount = authorized?.Amount,
            payments = txns.Select(t => new
            {
                t.Id,
                t.Gateway,
                t.TransactionType,
                t.Amount,
                t.Status,
                t.GatewayTransactionId,
                t.AuthCode,
                t.ResponseReasonText,
                t.CardLast4,
                t.CardType,
                t.CreatedAt,
                t.VoidedAt,
            }),
            customFields = await CustomFieldViewsAsync(id, customFields, customFieldValues, customFieldDefinitions, ct),
            r.CommitmentEvents,
            dispositions = r.Interactions.OrderBy(i => i.InteractionNumber)
                .Select(i => new { i.InteractionNumber, i.Type, i.Disposition, i.Status, i.StartedAt, i.CompletedAt }),
            sessions = sessionViews,
            audit = (await audit.GetByCallRecordAsync(id, ct)).Select(e => new
            {
                e.Id,
                e.Action,
                e.Summary,
                detail = ParseJson(e.Detail),
                e.ActorName,
                e.CreatedAt,
            }),
        });
    }

    // ── PUT /api/v1/call-review/calls/{id}/contact ──────────────────────────

    private static async Task<IResult> UpdateContact(
        Guid id,
        UpdateCallContactRequest req,
        HttpContext http,
        ICallRecordRepository callRecords,
        ICallRecordAuditRepository audit,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        if (!CanManage(http, out var actor)) return Results.Forbid();

        var r = await callRecords.GetByIdAsync(id, ct);
        if (r is null) return Results.NotFound();

        var email = Clean(req.Email);
        if (email is not null && !email.Contains('@'))
            return Results.BadRequest(new { error = "Email address is not valid." });

        var before = new { r.FirstName, r.LastName, r.Email, r.BillingPhone, r.ShippingPhone };
        var after = new
        {
            FirstName = Clean(req.FirstName),
            LastName = Clean(req.LastName),
            Email = email,
            BillingPhone = Digits(req.BillingPhone),
            ShippingPhone = Digits(req.ShippingPhone),
        };

        r.SetCallerIdentity(after.FirstName, after.LastName, after.Email, r.Phone, r.AccountNumber);
        r.SetContactPhones(after.BillingPhone, after.ShippingPhone);
        await callRecords.SaveChangesAsync(ct);

        await Audit(audit, id, CallAuditAction.ContactEdited, "Customer contact details edited",
            new { before, after }, actor, ct, http);
        return Results.NoContent();
    }

    // ── PUT /api/v1/call-review/calls/{id}/addresses/{role} ─────────────────
    // role = billing | shipping. A shipping change re-prices the cart (tax follows the ship-to).

    private static async Task<IResult> UpdateAddress(
        Guid id,
        string role,
        AddressData address,
        HttpContext http,
        ICallRecordRepository callRecords,
        ICallAddressService addresses,
        ICallRecordAuditRepository audit,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        if (!CanManage(http, out var actor)) return Results.Forbid();
        if (role is not (CallAddressRole.Billing or CallAddressRole.Shipping))
            return Results.BadRequest(new { error = "Address role must be 'billing' or 'shipping'." });

        var r = await callRecords.GetByIdAsync(id, ct);
        if (r is null) return Results.NotFound();
        var before = role == CallAddressRole.Billing ? r.Addresses?.Billing : r.Addresses?.Shipping;
        var totalBefore = r.Cart?.CartTotal;

        // An admin-typed address hasn't been through a validation service.
        address.IsVerified = false;
        address.VerificationSource = "manual";
        var repriced = await addresses.SetAsync(id, role, address, ct);

        var summary = $"{char.ToUpperInvariant(role[0])}{role[1..]} address edited";
        if (repriced is not null && repriced.CartTotal != totalBefore)
            summary += $" (cart re-priced {totalBefore:0.00} → {repriced.CartTotal:0.00})";
        await Audit(audit, id, CallAuditAction.AddressEdited, summary, new { role, before, after = address }, actor, ct, http);

        return Results.Ok(new { cart = repriced });
    }

    // ── Cart edits ──────────────────────────────────────────────────────────

    private static Task<IResult> AddCartItem(
        Guid id, ReviewAddCartItemRequest req, HttpContext http, ICartService cart, ICallRecordRepository callRecords,
        ICallRecordAuditRepository audit, TenantContext tenantContext, CancellationToken ct) =>
        CartEdit(id, http, callRecords, audit, tenantContext,
            () => cart.AddItemAsync(id, req.OfferId, req.Quantity, ct), $"Added offer to cart (qty {req.Quantity})",
            new { req.OfferId, req.Quantity }, ct);

    private static Task<IResult> UpdateCartItem(
        Guid id, int itemIndex, ReviewUpdateCartItemRequest req, HttpContext http, ICartService cart,
        ICallRecordRepository callRecords, ICallRecordAuditRepository audit, TenantContext tenantContext, CancellationToken ct) =>
        CartEdit(id, http, callRecords, audit, tenantContext,
            () => cart.UpdateQuantityAsync(id, itemIndex, req.Quantity, ct), $"Changed cart line {itemIndex + 1} quantity to {req.Quantity}",
            new { itemIndex, req.Quantity }, ct);

    private static Task<IResult> RemoveCartItem(
        Guid id, int itemIndex, HttpContext http, ICartService cart, ICallRecordRepository callRecords,
        ICallRecordAuditRepository audit, TenantContext tenantContext, CancellationToken ct) =>
        CartEdit(id, http, callRecords, audit, tenantContext,
            () => cart.RemoveItemAsync(id, itemIndex, ct), $"Removed cart line {itemIndex + 1}",
            new { itemIndex }, ct);

    private static async Task<IResult> CartEdit(
        Guid id, HttpContext http, ICallRecordRepository callRecords, ICallRecordAuditRepository audit,
        TenantContext tenantContext, Func<Task<CartOperationResult>> edit, string summary, object change,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        if (!CanManage(http, out var actor)) return Results.Forbid();

        var r = await callRecords.GetByIdAsync(id, ct);
        if (r is null) return Results.NotFound();
        var before = r.Cart?.Items.Select(i => new { i.Sku, i.Description, i.Quantity }).ToList();
        var totalBefore = r.Cart?.CartTotal;

        CartOperationResult result;
        try { result = await edit(); }
        catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (ArgumentOutOfRangeException ex) { return Results.BadRequest(new { error = ex.Message }); }

        if (!result.Succeeded)
            return Results.Conflict(new
            {
                error = $"Insufficient inventory for: {string.Join(", ", result.UnavailableSkus)}.",
                unavailableSkus = result.UnavailableSkus,
            });

        var after = result.Cart?.Items.Select(i => new { i.Sku, i.Description, i.Quantity }).ToList();
        await Audit(audit, id, CallAuditAction.CartEdited,
            $"{summary} (total {totalBefore:0.00} → {result.Cart?.CartTotal:0.00})",
            new { change, before, after }, actor, ct, http);
        return Results.Ok(result.Cart);
    }

    // ── PUT /api/v1/call-review/calls/{id}/custom-fields/{definitionId} ─────
    // Body { value } — parsed by the field's data type; null/blank clears the stored value.

    private static async Task<IResult> UpdateCustomField(
        Guid id,
        Guid definitionId,
        UpdateCustomFieldRequest req,
        HttpContext http,
        ICustomFieldService customFields,
        ICallRecordAuditRepository audit,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        if (!CanManage(http, out var actor)) return Results.Forbid();

        List<ResolvedCustomField> fields;
        try { fields = await customFields.GetFieldsForCallAsync(id, ct); }
        catch (InvalidOperationException) { return Results.NotFound(); }
        var field = fields.FirstOrDefault(f => f.Definition.Id == definitionId);
        if (field is null) return Results.BadRequest(new { error = "That custom field doesn't apply to this call's campaign." });

        var before = FormatCustomValue(field.Value?.GetTypedValue());
        var after = string.IsNullOrWhiteSpace(req.Value) ? null : req.Value.Trim();
        try
        {
            if (after is null) await customFields.DeleteValueAsync(id, definitionId, ct);
            else await customFields.SetValueAsync(id, definitionId, after, ct);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException or OverflowException)
        {
            return Results.BadRequest(new { error = $"{field.Definition.DisplayLabel}: {ex.Message}" });
        }

        await Audit(audit, id, CallAuditAction.CustomFieldEdited,
            $"Custom field “{field.Definition.DisplayLabel}”: {before ?? "(blank)"} → {after ?? "(blank)"}",
            new { definitionId, field.Definition.FieldName, before, after }, actor, ct, http);
        return Results.NoContent();
    }

    /// <summary>
    /// Every custom field that applies to the call's campaign (campaign > client > tenant scope),
    /// with its stored value — blank when never set — editable. Plus any value stored on the call
    /// whose field no longer applies (deactivated, or the call's campaign changed), shown
    /// read-only so nothing captured on the call is hidden.
    /// </summary>
    private static async Task<List<object>> CustomFieldViewsAsync(
        Guid callRecordId, ICustomFieldService customFields, ICustomFieldValueRepository values,
        ICustomFieldDefinitionRepository definitions, CancellationToken ct)
    {
        var resolved = await customFields.GetFieldsForCallAsync(callRecordId, ct);
        var shown = resolved.Select(f => f.Definition.Id).ToHashSet();
        var orphanValues = (await values.GetByCallRecordAsync(callRecordId, ct))
            .Where(v => !shown.Contains(v.DefinitionId)).ToList();

        var orphans = new List<ResolvedCustomField>();
        foreach (var v in orphanValues)
            if (await definitions.GetByIdAsync(v.DefinitionId, ct) is { } def)
                orphans.Add(new ResolvedCustomField(def, v));

        return resolved.Select(f => (Field: f, Editable: true))
            .Concat(orphans.Select(f => (Field: f, Editable: false)))
            .OrderBy(x => !x.Editable).ThenBy(x => x.Field.Definition.DisplayOrder).ThenBy(x => x.Field.Definition.DisplayLabel)
            .Select(x => (object)new
            {
                definitionId = x.Field.Definition.Id,
                x.Field.Definition.FieldName,
                x.Field.Definition.DisplayLabel,
                x.Field.Definition.DataTypeName,
                x.Field.Definition.IsRequired,
                x.Field.Definition.IsActive,
                scope = x.Field.Definition.CampaignId is not null ? "campaign" : x.Field.Definition.ClientId is not null ? "client" : "tenant",
                inScope = x.Editable,
                value = FormatCustomValue(x.Field.Value?.GetTypedValue()),
                storedAt = x.Field.Value?.StoredAt,
            })
            .ToList();
    }

    private static string? FormatCustomValue(object? value) => value switch
    {
        null => null,
        bool b => b ? "true" : "false",
        DateTimeOffset dto => dto.ToString("O"),
        DateOnly d => d.ToString("yyyy-MM-dd"),
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    // ── PUT /api/v1/call-review/calls/{id}/sessions/{sessionId}/variables ───
    // Body { changes: { "vendor_number": "123", "coupon_code": null } } — null removes the variable.

    private static async Task<IResult> UpdateVariables(
        Guid id,
        Guid sessionId,
        UpdateSessionVariablesRequest req,
        HttpContext http,
        IFlowSessionRepository sessions,
        IFlowEngine engine,
        ICallRecordAuditRepository audit,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        if (!CanManage(http, out var actor)) return Results.Forbid();
        if (req.Changes is null || req.Changes.Count == 0) return Results.BadRequest(new { error = "No changes." });

        var session = await sessions.GetByIdAsync(sessionId, ct);
        if (session is null || session.CallRecordId != id) return Results.NotFound();

        var previous = await engine.UpdateSessionVariablesAsync(sessionId, req.Changes, ct);
        await Audit(audit, id, CallAuditAction.VariablesEdited,
            $"Flow variables edited: {string.Join(", ", previous.Keys)}",
            new { sessionId, before = previous, after = req.Changes }, actor, ct, http);
        return Results.NoContent();
    }

    // ── POST /api/v1/call-review/calls/{id}/sessions/{sessionId}/api-calls/{nodeId}/rerun ──

    private static async Task<IResult> RerunApiCall(
        Guid id,
        Guid sessionId,
        string nodeId,
        HttpContext http,
        IFlowSessionRepository sessions,
        IFlowEngine engine,
        ICallRecordAuditRepository audit,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        if (!CanManage(http, out var actor)) return Results.Forbid();

        var session = await sessions.GetByIdAsync(sessionId, ct);
        if (session is null || session.CallRecordId != id) return Results.NotFound();

        ApiCallRerunResult result;
        try { result = await engine.RerunApiCallNodeAsync(sessionId, nodeId, ct); }
        catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }

        var summary = result.Replayed
            ? "API call re-run: already succeeded on this call — replayed, nothing sent"
            : result.Success
                ? $"API call re-run: succeeded ({result.StatusCode})"
                : $"API call re-run: {result.Transition} — {Truncate(result.Error, 200)}";
        await Audit(audit, id, CallAuditAction.ApiCallRerun, summary,
            new { sessionId, nodeId, result.Success, result.Transition, result.StatusCode, result.Error, result.Replayed },
            actor, ct, http);

        return Results.Ok(result);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static bool HasPermission(HttpContext http, string permission) =>
        (http.User.FindFirst("permissions")?.Value ?? "").Split(',').Contains(permission);

    private static bool CanView(HttpContext http) =>
        HasPermission(http, Permission.CallsView) || HasPermission(http, Permission.CallsManage);

    private static bool CanManage(HttpContext http, out (Guid Id, string Name) actor)
    {
        actor = ActorResolver.Resolve(http.User) ?? (Guid.Empty, "Unknown");
        return HasPermission(http, Permission.CallsManage) && actor.Id != Guid.Empty;
    }

    /// <summary>Records the change, then refreshes the screen of any agent still in a script on this
    /// call (a supervisor fixing data while the agent is on the call — see PushLiveUpdateAsync).
    /// A failed push never fails the edit itself.</summary>
    private static async Task Audit(
        ICallRecordAuditRepository audit, Guid callRecordId, string action, string summary, object detail,
        (Guid Id, string Name) actor, CancellationToken ct, HttpContext? http = null)
    {
        summary = Truncate(summary, 500)!;
        await audit.AddAsync(CallRecordAuditEntry.Create(
            callRecordId, action, summary, JsonSerializer.Serialize(detail, AuditJson), actor.Id, actor.Name), ct);

        if (http is null) return;
        var services = http.RequestServices;
        try { await services.GetRequiredService<IFlowNotifier>().PushCallChangedAsync(callRecordId, ct); }
        catch { /* best effort — refreshes other open review pages for this call */ }
        var sessions = services.GetRequiredService<IFlowSessionRepository>();
        var engine = services.GetRequiredService<IFlowEngine>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(CallReviewEndpoints));
        foreach (var session in await sessions.GetByCallRecordAsync(callRecordId, ct))
        {
            if (session.Status != FlowSessionStatus.Active) continue;
            try { await engine.PushLiveUpdateAsync(session.Id, $"Updated by {actor.Name}: {summary}", ct); }
            catch (Exception ex) { logger.LogWarning(ex, "Live update push failed for session {SessionId}", session.Id); }
        }
    }

    private static JsonElement? ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonDocument.Parse(json).RootElement.Clone(); }
        catch (JsonException) { return null; }
    }

    private static string? FullName(string? first, string? last)
    {
        var name = string.Join(" ", new[] { first, last }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return name.Length == 0 ? null : name;
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? Digits(string? s)
    {
        var d = new string((s ?? "").Where(char.IsDigit).ToArray());
        return d.Length == 0 ? null : d;
    }

    private static string? Truncate(string? s, int max) =>
        s is null || s.Length <= max ? s : s[..max] + "…";
}

public record UpdateCallContactRequest(
    string? FirstName, string? LastName, string? Email, string? BillingPhone, string? ShippingPhone);
public record ReviewAddCartItemRequest(Guid OfferId, int Quantity);
public record ReviewUpdateCartItemRequest(int Quantity);
public record UpdateCustomFieldRequest(string? Value);
public record UpdateSessionVariablesRequest(Dictionary<string, string?>? Changes);
