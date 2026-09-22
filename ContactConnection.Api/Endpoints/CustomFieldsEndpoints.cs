using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Api.Endpoints;

public static class CustomFieldsEndpoints
{
    public static IEndpointRouteBuilder MapCustomFieldsEndpoints(this IEndpointRouteBuilder app)
    {
        // Data types reference — read-only, used by flow designer to populate type picker
        app.MapGet("/api/v1/data-types", GetDataTypes).RequireAuthorization();

        // Definition management
        var defs = app.MapGroup("/api/v1/custom-field-definitions").RequireAuthorization();
        defs.MapPost("", CreateDefinition);
        defs.MapGet("", GetDefinitions);
        defs.MapGet("{id:guid}", GetDefinitionById);
        defs.MapPatch("{id:guid}", UpdateDefinition);

        // Value management — nested under call-records
        var calls = app.MapGroup("/api/v1/call-records").RequireAuthorization();
        calls.MapGet("{id:guid}/custom-fields", GetFieldsForCall);
        calls.MapPut("{id:guid}/custom-fields/{definitionId:guid}", SetValue);
        calls.MapDelete("{id:guid}/custom-fields/{definitionId:guid}", DeleteValue);

        return app;
    }

    // ── GET /api/v1/data-types ───────────────────────────────────────────────

    private static async Task<IResult> GetDataTypes(
        IDataTypeRepository dataTypes,
        CancellationToken ct)
    {
        var list = await dataTypes.GetAllAsync(ct);
        return Results.Ok(list.Select(ToDataTypeResponse));
    }

    // ── POST /api/v1/custom-field-definitions ───────────────────────────────

    private static async Task<IResult> CreateDefinition(
        CreateCustomFieldDefinitionRequest req,
        ICustomFieldDefinitionRepository definitions,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();

        CustomFieldDefinition def;
        try
        {
            def = CustomFieldDefinition.Create(
                tenantContext.Current!.Id,
                req.FieldName,
                req.DisplayLabel,
                req.DataTypeName,
                req.IsRequired,
                req.DisplayOrder,
                req.ClientId,
                req.CampaignId,
                req.ValidationRules);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }

        await definitions.AddAsync(def, ct);
        try
        {
            await definitions.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return Results.Conflict(new
            {
                error = "A field with this name already exists at this scope (tenant/client/campaign)."
            });
        }

        return Results.Created($"/api/v1/custom-field-definitions/{def.Id}", ToDefinitionResponse(def));
    }

    // ── GET /api/v1/custom-field-definitions ────────────────────────────────

    private static async Task<IResult> GetDefinitions(
        Guid? clientId,
        Guid? campaignId,
        ICustomFieldDefinitionRepository definitions,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();

        var list = clientId.HasValue || campaignId.HasValue
            ? await definitions.GetForContextAsync(tenantContext.Current!.Id, clientId, campaignId, ct)
            : await definitions.GetAllForTenantAsync(tenantContext.Current!.Id, ct);

        return Results.Ok(list.Select(ToDefinitionResponse));
    }

    // ── GET /api/v1/custom-field-definitions/{id} ───────────────────────────

    private static async Task<IResult> GetDefinitionById(
        Guid id,
        ICustomFieldDefinitionRepository definitions,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();

        var def = await definitions.GetByIdAsync(id, ct);
        if (def is null || def.TenantId != tenantContext.Current!.Id)
            return Results.NotFound();

        return Results.Ok(ToDefinitionResponse(def));
    }

    // ── PATCH /api/v1/custom-field-definitions/{id} ──────────────────────────

    private static async Task<IResult> UpdateDefinition(
        Guid id,
        UpdateCustomFieldDefinitionRequest req,
        ICustomFieldDefinitionRepository definitions,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();

        var def = await definitions.GetByIdAsync(id, ct);
        if (def is null || def.TenantId != tenantContext.Current!.Id)
            return Results.NotFound();

        if (req.DisplayLabel is not null) def.UpdateLabel(req.DisplayLabel);
        if (req.DisplayOrder is not null) def.SetDisplayOrder(req.DisplayOrder.Value);
        if (req.IsRequired is not null) def.SetRequired(req.IsRequired.Value);
        if (req.ValidationRules is not null) def.SetValidationRules(req.ValidationRules);
        if (req.IsActive is not null)
        {
            if (req.IsActive.Value) def.Activate();
            else def.Deactivate();
        }

        await definitions.SaveChangesAsync(ct);
        return Results.Ok(ToDefinitionResponse(def));
    }

    // ── GET /api/v1/call-records/{id}/custom-fields ──────────────────────────

    private static async Task<IResult> GetFieldsForCall(
        Guid id,
        ICustomFieldService customFields,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();

        try
        {
            var fields = await customFields.GetFieldsForCallAsync(id, ct);
            return Results.Ok(fields.Select(f => new
            {
                Definition = ToDefinitionResponse(f.Definition),
                Value = f.Value is null ? null : ToValueResponse(f.Value)
            }));
        }
        catch (InvalidOperationException)
        {
            return Results.NotFound();
        }
    }

    // ── PUT /api/v1/call-records/{id}/custom-fields/{definitionId} ───────────

    private static async Task<IResult> SetValue(
        Guid id, Guid definitionId,
        SetCustomFieldValueRequest req,
        ICustomFieldService customFields,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();

        try
        {
            await customFields.SetValueAsync(id, definitionId, req.Value, ct);
            return Results.Ok();
        }
        catch (InvalidOperationException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
        catch (FormatException ex)
        {
            return Results.BadRequest(new { error = $"Value format error: {ex.Message}" });
        }
    }

    // ── DELETE /api/v1/call-records/{id}/custom-fields/{definitionId} ────────

    private static async Task<IResult> DeleteValue(
        Guid id, Guid definitionId,
        ICustomFieldService customFields,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();

        await customFields.DeleteValueAsync(id, definitionId, ct);
        return Results.NoContent();
    }

    // ── Response shapes ──────────────────────────────────────────────────────

    private static object ToDataTypeResponse(DataType d) => new
    {
        d.TypeName,
        d.ClrType,
        d.PostgresType,
        d.DisplayFormat,
        d.IsAggregatable,
        d.AggregationFunctions
    };

    internal static object ToDefinitionResponse(CustomFieldDefinition d) => new
    {
        d.Id,
        d.TenantId,
        d.ClientId,
        d.CampaignId,
        d.FieldName,
        d.DisplayLabel,
        d.DataTypeName,
        d.IsRequired,
        d.ValidationRules,
        d.DisplayOrder,
        d.IsActive
    };

    private static object ToValueResponse(CustomFieldValue v) => new
    {
        v.DefinitionId,
        v.ValueString,
        v.ValueInteger,
        v.ValueDecimal,
        v.ValueBoolean,
        v.ValueDate,
        v.ValueDatetime,
        v.ValueJson,
        TypedValue = v.GetTypedValue(),
        v.StoredAt
    };
}

// ── Request records ──────────────────────────────────────────────────────────

public record CreateCustomFieldDefinitionRequest(
    string FieldName,
    string DisplayLabel,
    string DataTypeName,
    bool IsRequired = false,
    int DisplayOrder = 0,
    Guid? ClientId = null,
    Guid? CampaignId = null,
    string? ValidationRules = null);

public record UpdateCustomFieldDefinitionRequest(
    string? DisplayLabel = null,
    int? DisplayOrder = null,
    bool? IsRequired = null,
    string? ValidationRules = null,
    bool? IsActive = null);

public record SetCustomFieldValueRequest(string Value);
