using System.Security.Claims;
using System.Text.Json;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

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
        group.MapPost("{id:guid}/finalize", FinalizeCall);

        return app;
    }

    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web);

    // ── GET /api/v1/call-review/calls ───────────────────────────────────────

    private static async Task<IResult> Search(
        DateTimeOffset? from, DateTimeOffset? to, Guid? campaignId, string? phone, string? orderNumber,
        string? name, bool? failedOnly, int? page, int? pageSize, string? runMode,
        HttpContext http,
        ICallRecordRepository callRecords,
        ICampaignRepository campaigns,
        IAgentRepository agents,
        TenantContext tenantContext,
        ScopedTenantDbContextFactory dbFactory,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        if (!CanView(http)) return Results.Forbid();

        var size = Math.Clamp(pageSize ?? 50, 1, 200);
        var pageNo = Math.Max(1, page ?? 1);
        var result = await callRecords.SearchAsync(new CallRecordSearchCriteria(
            from, to, campaignId, phone, orderNumber, name, failedOnly == true,
            Skip: (pageNo - 1) * size, Take: size, RunMode: runMode), ct);

        var failed = await callRecords.FindWithFailedApiCallsAsync(result.Items.Select(r => r.Id).ToList(), ct);
        var abandons = await AbandonsAsync(dbFactory, result.Items.Select(r => r.Id).ToList(), ct);
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
                r.RunMode,
                r.OverallStatus,
                r.CallerId,
                r.BillingPhone,
                r.ShippingPhone,
                customerName = FullName(r.FirstName, r.LastName),
                // Per interaction (S178): every order number on the call, and the carts' combined total.
                orderNumber = string.Join(", ", r.Interactions.OrderBy(i => i.StartedAt)
                    .Select(i => i.OrderNumber).Where(n => !string.IsNullOrEmpty(n))) is { Length: > 0 } nums ? nums : null,
                cartTotal = r.Interactions.Any(i => i.Cart is { Items.Count: > 0 })
                    ? r.Interactions.Where(i => i.Cart is not null).Sum(i => i.Cart!.CartTotal) : (decimal?)null,
                hasFailedApiCall = failed.Contains(r.Id),
                abandon = abandons.GetValueOrDefault(r.Id),
            }),
        });
    }

    private static async Task<(Dictionary<Guid, string> Agents, Dictionary<Guid, string> Campaigns)> InteractionNamesAsync(
        ScopedTenantDbContextFactory dbFactory, IEnumerable<CallInteraction> interactions, CancellationToken ct)
    {
        var agentIds = interactions.Select(i => i.AgentId).OfType<Guid>().Distinct().ToList();
        var campaignIds = interactions.Select(i => i.CampaignId).OfType<Guid>().Distinct().ToList();
        await using var db = dbFactory.Create();
        var agents = await db.Agents.AsNoTracking().Where(a => agentIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.FirstName + " " + a.LastName, ct);
        var campaigns = await db.Campaigns.AsNoTracking().Where(c => campaignIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        return (agents, campaigns);
    }

    /// <summary>The caller hung up before being served (S178): type (pre_queue / in_queue / callback_abandon / …) and
    /// length (short / long) from the call's latest <c>abandoned</c> state. Null for calls that weren't abandoned.</summary>
    public sealed record AbandonInfo(string? Type, string? Length, DateTimeOffset At, string? Detail);

    private static async Task<Dictionary<Guid, AbandonInfo>> AbandonsAsync(
        ScopedTenantDbContextFactory dbFactory, List<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        await using var db = dbFactory.Create();
        var rows = await db.CallStateHistory.AsNoTracking()
            .Where(h => ids.Contains(h.CallRecordId) && h.State == CallHistoryState.Abandoned)
            .Select(h => new { h.CallRecordId, h.Sequence, h.AbandonType, h.AbandonLength, h.EnteredAt, h.Detail })
            .ToListAsync(ct);
        return rows.GroupBy(h => h.CallRecordId)
            .ToDictionary(g => g.Key, g =>
            {
                var h = g.MaxBy(x => x.Sequence)!;
                return new AbandonInfo(h.AbandonType, h.AbandonLength, h.EnteredAt, h.Detail);
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
        ITelephonyCallSessionStore telephonySessions,
        TenantContext tenantContext,
        ScopedTenantDbContextFactory dbFactory,
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
        var callSessionRows = await sessions.GetByCallRecordAsync(id, ct);
        var anyOpenScript = callSessionRows.Any(s => s.Status == FlowSessionStatus.Active);
        foreach (var s in callSessionRows)
        {
            var flow = await flows.GetByIdAsync(s.FlowId, ct);
            var snapshot = await engine.GetSessionSnapshotAsync(s.Id, ct);
            sessionViews.Add(new
            {
                s.Id,
                s.InteractionId,
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

        var liveCall = await LiveCallAsync(id, telephonySessions, ct);
        var ixNames = await InteractionNamesAsync(dbFactory, r.Interactions, ct);
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
            r.RunMode,
            r.RecordType,
            r.OverallStatus,
            abandon = (await AbandonsAsync(dbFactory, [r.Id], ct)).GetValueOrDefault(r.Id),
            r.ClientId,
            clientName = client?.Name,
            r.CampaignId,
            campaignName = campaign?.Name,
            r.AgentId,
            agentName = agent?.FullName,
            r.CallerId,
            r.Dnis,
            r.MediaAttribution,
            orderNumber = r.FirstInteraction?.OrderNumber,
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
            cart = r.FirstInteraction?.Cart,
            authorizedAmount = authorized?.Amount,
            // Card on file (never the data itself) — decides whether a re-authorization is possible.
            cardData = new
            {
                onFile = !string.IsNullOrEmpty(r.SensitiveData),
                storedAt = r.SensitiveDataStoredAt,
                wipedAt = r.SensitiveDataWipedAt,
                wipeReason = r.SensitiveWipeReason,
                retention = campaign?.CardDataRetention ?? CardDataRetentionMode.UntilScriptEnds,
                // When the Worker's sweep would wipe it: campaign override, else the platform default.
                expiresAt = string.IsNullOrEmpty(r.SensitiveData) || r.SensitiveDataStoredAt is null ? (DateTimeOffset?)null
                    : r.SensitiveDataStoredAt.Value.AddMinutes(campaign?.SensitiveDataRetentionMinutes
                        ?? http.RequestServices.GetRequiredService<IConfiguration>().GetValue("SensitiveData:Retention:TtlMinutes", 60)),
            },
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
            // A caller still connected on this call (live telephony channel) — Finalize warns first.
            liveCall = liveCall,
            // Finalize is for closing out what's still open — a script, a connected caller, or a call
            // that never ended. A call that ended normally has nothing to finalize.
            canFinalize = r.FinalizedAt is null
                && (liveCall is not null || r.CallEndAt is null || anyOpenScript),
            finalized = r.FinalizedAt is null ? null : new { at = r.FinalizedAt, byName = r.FinalizedByName, reason = r.FinalizeReason },
            agentLock = agent is null ? null : new
            {
                statusLocked = agent.IsStatusLocked,
                agent.SignInLocked,
                lockedBy = agent.StatusLockedByName,
                reason = agent.StatusLockReason,
            },
            // Each interaction = one agent's work on the call, with its own campaign (S178: a call transferred
            // sales → CS has two). Numbered by start order, which also tidies calls saved before the numbering fix.
            dispositions = r.Interactions.OrderBy(i => i.StartedAt).ThenBy(i => i.InteractionNumber)
                .Select((i, n) => new
                {
                    interactionNumber = n + 1, i.Type, i.Disposition, i.Status, i.StartedAt, i.CompletedAt,
                    i.AgentId, agentName = i.AgentId is { } ia ? ixNames.Agents.GetValueOrDefault(ia) : null,
                    i.CampaignId, campaignName = i.CampaignId is { } ic ? ixNames.Campaigns.GetValueOrDefault(ic) : null,
                    // A transferred interaction's own script-written fields (S178); null otherwise.
                    customFields = string.IsNullOrEmpty(i.CustomFields) ? null : ParseJson(i.CustomFields),
                    // Interaction-scoped commerce (S178): this agent's own cart, order and payments.
                    i.Id, i.Cart, i.OrderNumber, i.OrderSubmittedAt, i.PaymentStatus, i.RoutedTierLabel,
                    authorizedAmount = txns.LastOrDefault(t => t.InteractionId == i.Id
                        && t.Status == PaymentTransactionStatus.Approved && t.VoidedAt is null)?.Amount,
                    paymentIds = txns.Where(t => t.InteractionId == i.Id).Select(t => t.Id),
                    sessionIds = callSessionRows.Where(s => s.InteractionId == i.Id).Select(s => s.Id),
                }),
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

        var r = await callRecords.GetByIdWithInteractionsAsync(id, ct);
        if (r is null) return Results.NotFound();
        var before = role == CallAddressRole.Billing ? r.Addresses?.Billing : r.Addresses?.Shipping;
        var totalBefore = r.FirstInteraction?.Cart?.CartTotal;

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

    // Each edit names the interaction whose cart it changes (S178 — a transferred call has one per agent).
    private static Task<IResult> AddCartItem(
        Guid id, Guid? interactionId, ReviewAddCartItemRequest req, HttpContext http, ICartService cart, ICallRecordRepository callRecords,
        ICallRecordAuditRepository audit, TenantContext tenantContext, CancellationToken ct) =>
        CartEdit(id, interactionId, http, callRecords, audit, tenantContext,
            () => cart.AddItemAsync(id, req.OfferId, req.Quantity, ct, enforceScope: true, interactionId: interactionId),
            $"Added offer to cart (qty {req.Quantity})", new { req.OfferId, req.Quantity }, ct);

    private static Task<IResult> UpdateCartItem(
        Guid id, int itemIndex, Guid? interactionId, ReviewUpdateCartItemRequest req, HttpContext http, ICartService cart,
        ICallRecordRepository callRecords, ICallRecordAuditRepository audit, TenantContext tenantContext, CancellationToken ct) =>
        CartEdit(id, interactionId, http, callRecords, audit, tenantContext,
            () => cart.UpdateQuantityAsync(id, itemIndex, req.Quantity, ct, interactionId),
            $"Changed cart line {itemIndex + 1} quantity to {req.Quantity}", new { itemIndex, req.Quantity }, ct);

    private static Task<IResult> RemoveCartItem(
        Guid id, int itemIndex, Guid? interactionId, HttpContext http, ICartService cart, ICallRecordRepository callRecords,
        ICallRecordAuditRepository audit, TenantContext tenantContext, CancellationToken ct) =>
        CartEdit(id, interactionId, http, callRecords, audit, tenantContext,
            () => cart.RemoveItemAsync(id, itemIndex, ct, interactionId), $"Removed cart line {itemIndex + 1}",
            new { itemIndex }, ct);

    private static async Task<IResult> CartEdit(
        Guid id, Guid? interactionId, HttpContext http, ICallRecordRepository callRecords, ICallRecordAuditRepository audit,
        TenantContext tenantContext, Func<Task<CartOperationResult>> edit, string summary, object change,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        if (!CanManage(http, out var actor)) return Results.Forbid();

        var r = await callRecords.GetByIdWithInteractionsAsync(id, ct);
        if (r is null) return Results.NotFound();
        var ixCart = r.CommerceInteraction(interactionId)?.Cart;
        var before = ixCart?.Items.Select(i => new { i.Sku, i.Description, i.Quantity }).ToList();
        var totalBefore = ixCart?.CartTotal;

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
        ICommissionService commissions,
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
        // A flag-based commission may depend on this field (S171).
        await commissions.RecalculateAsync(id, CommissionTrigger.CustomFieldEdited, ct);
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
        try { result = await engine.RerunNodeAsync(sessionId, nodeId, ct); }
        catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }

        var isPayment = result.NodeType == "authorize_payment";
        var summary = isPayment
            ? result.Success
                ? $"Payment re-authorized: {result.Response}"
                : $"Payment re-authorization {result.Transition} — {Truncate(result.Error, 200)}"
            : result.Replayed
                ? "API call re-run: already succeeded on this call — replayed, nothing sent"
                : result.Success
                    ? $"API call re-run: succeeded ({result.StatusCode})"
                    : $"API call re-run: {result.Transition} — {Truncate(result.Error, 200)}";
        await Audit(audit, id, CallAuditAction.ApiCallRerun, summary,
            new { sessionId, nodeId, result.Success, result.Transition, result.StatusCode, result.Error, result.Replayed },
            actor, ct, http);

        return Results.Ok(result);
    }

    // ── POST /api/v1/call-review/calls/{id}/finalize ────────────────────────
    // Supervisor closes the call out: hangs up a still-connected caller (only once confirmed),
    // finishes any open script on the agent's screen (checkmark, tab closes), marks the call
    // complete with who/when/why, and optionally locks the agent (status, or sign-in = signed out).

    private static async Task<IResult> FinalizeCall(
        Guid id,
        FinalizeCallRequest req,
        HttpContext http,
        ICallRecordRepository callRecords,
        IFlowSessionRepository sessions,
        IFlowEngine engine,
        ITelephonyCallSessionStore telephonySessions,
        IEslCommanderFactory eslFactory,
        ICallRecordAuditRepository audit,
        TenantContext tenantContext,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        if (!CanManage(http, out var actor)) return Results.Forbid();
        var reason = req.Reason?.Trim();
        if (string.IsNullOrEmpty(reason)) return Results.BadRequest(new { error = "A reason is required to finalize a call." });
        var agentLock = req.AgentLock ?? "none";
        if (agentLock is not ("none" or "status" or "sign_in"))
            return Results.BadRequest(new { error = "agentLock must be none, status or sign_in." });

        var record = await callRecords.GetByIdAsync(id, ct);
        if (record is null) return Results.NotFound();
        if (record.FinalizedAt is not null)
            return Results.Conflict(new { error = $"Already finalized by {record.FinalizedByName} ({record.FinalizeReason})." });

        // 1. A caller still on the line: only with the supervisor's explicit confirmation.
        var live = (await telephonySessions.GetAllAsync(ct)).Where(s => s.CallRecordId == id).ToList();
        var callSessions = await sessions.GetByCallRecordAsync(id, ct);
        if (live.Count == 0 && record.CallEndAt is not null && callSessions.All(s => s.Status != FlowSessionStatus.Active))
            return Results.Conflict(new { error = "This call already ended normally — there's nothing open to finalize." });
        if (live.Count > 0 && !req.ConfirmLiveCall)
            return Results.Conflict(new { error = "The caller is still connected on this call. Confirm to hang up and finalize.", liveCall = true });

        var logger = loggerFactory.CreateLogger(nameof(CallReviewEndpoints));
        string? hangupError = null;
        if (live.Count > 0)
        {
            try
            {
                await using var esl = await eslFactory.CreateAsync(ct);
                foreach (var s in live)
                {
                    // The agent leg is bridged as an independently-parked channel — kill it too, and
                    // clear park_after_bridge first on both so neither leg is re-parked instead of
                    // ending (the phantom-call trap, see project_phantom_call_park_after_bridge).
                    var peer = s.Vars.GetValueOrDefault("_bridged_peer_uuid") ?? s.Vars.GetValueOrDefault("_agent_uuid");
                    foreach (var uuid in new[] { s.ChannelUuid, peer }.Where(u => !string.IsNullOrEmpty(u)).Distinct())
                    {
                        try { await esl.SetChannelVarAsync(uuid!, "park_after_bridge", "false", ct); } catch { /* leg may already be gone */ }
                    }
                    await esl.HangupChannelAsync(s.ChannelUuid, ct);
                    if (!string.IsNullOrEmpty(peer)) await esl.HangupChannelAsync(peer, ct);
                }
            }
            catch (Exception ex)
            {
                hangupError = ex.Message;
                logger.LogError(ex, "Finalize: hanging up call {CallRecordId} failed", id);
            }
        }

        // 2. Close every open script on the call (the agent sees it finish).
        var message = $"Call finalized by {actor.Name}: {reason}";
        var closed = 0;
        foreach (var s in callSessions.Where(s => s.Status == FlowSessionStatus.Active))
            if (await engine.FinalizeSessionAsync(s.Id, message, ct)) closed++;

        // 3. The record itself (re-read: finishing a script may have saved it).
        record = await callRecords.GetByIdAsync(id, ct) ?? record;
        record.Finalize(actor.Id, actor.Name, reason);
        await callRecords.SaveChangesAsync(ct);

        // 4. Optional agent lock — the call's agent plus anyone whose script was on it.
        var lockedAgents = new List<string>();
        if (agentLock != "none")
        {
            var agentIds = callSessions.Select(s => s.AgentId).Append(record.AgentId ?? Guid.Empty)
                .Where(a => a != Guid.Empty).Distinct();
            foreach (var agentId in agentIds)
                if (await AgentLockEndpoints.LockAsync(http, agentId, agentLock == "sign_in", req.LockReason ?? reason, actor, ct) is { } locked)
                    lockedAgents.Add(locked.FullName);
        }

        var parts = new List<string> { $"Call finalized: {reason}" };
        if (live.Count > 0) parts.Add(hangupError is null ? "caller hung up" : $"hang-up FAILED ({hangupError})");
        if (closed > 0) parts.Add($"{closed} script{(closed == 1 ? "" : "s")} closed");
        if (lockedAgents.Count > 0)
            parts.Add($"{string.Join(", ", lockedAgents)} {(agentLock == "sign_in" ? "signed out and locked" : "status locked")}");
        await Audit(audit, id, CallAuditAction.Finalized, string.Join(" \u00b7 ", parts),
            new { reason, hungUp = live.Count > 0 && hangupError is null, hangupError, scriptsClosed = closed, agentLock, lockedAgents }, actor, ct, http);

        return Results.Ok(new { hungUp = live.Count > 0 && hangupError is null, hangupError, scriptsClosed = closed, lockedAgents });
    }

    /// <summary>Live telephony channel(s) for the call — Redis sessions exist only while connected.</summary>
    private static async Task<object?> LiveCallAsync(Guid callRecordId, ITelephonyCallSessionStore store, CancellationToken ct)
    {
        var live = (await store.GetAllAsync(ct)).Where(s => s.CallRecordId == callRecordId).ToList();
        if (live.Count == 0) return null;
        var bridged = live.Any(s => !string.IsNullOrEmpty(s.Vars.GetValueOrDefault("_bridged_peer_uuid") ?? s.Vars.GetValueOrDefault("_agent_uuid")));
        return new { connected = true, withAgent = bridged, callerNumber = live[0].CallerNumber };
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
public record FinalizeCallRequest(string? Reason, string? AgentLock, string? LockReason, bool ConfirmLiveCall);
public record UpdateSessionVariablesRequest(Dictionary<string, string?>? Changes);
