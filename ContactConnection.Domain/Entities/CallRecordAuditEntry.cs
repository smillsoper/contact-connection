namespace ContactConnection.Domain.Entities;

/// <summary>What was done to a call after the fact — see CallRecordAuditEntry.</summary>
public static class CallAuditAction
{
    public const string ContactEdited    = "contact_edited";
    public const string AddressEdited    = "address_edited";
    public const string VariablesEdited  = "variables_edited";
    public const string CartEdited       = "cart_edited";
    public const string CustomFieldEdited = "custom_field_edited";
    public const string ApiCallRerun     = "api_call_rerun";
    public const string Finalized        = "finalized";
    public const string Supervisor       = "supervisor";   // monitor / coach / barge / take-over
    public const string MediaReattributed = "media_reattributed"; // replay media attribution (S171); detail holds before/after
    public const string AiSummaryConfirmed = "ai_summary_confirmed"; // AI call summary confirmed by a person (S171)
}

/// <summary>
/// Append-only log of post-call changes to a call record — an admin correcting the customer's
/// details, flow variables or cart, and re-running a flow's API call (e.g. resubmitting an order
/// the Order API rejected). <see cref="Detail"/> is JSON (before/after values, or the re-run's
/// outcome). Never holds card data — none of the editable fields carry it.
/// </summary>
public class CallRecordAuditEntry
{
    public Guid Id { get; private set; }
    public Guid CallRecordId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string Summary { get; private set; } = string.Empty;
    public string Detail { get; private set; } = "{}";
    public Guid ActorId { get; private set; }
    public string ActorName { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    private CallRecordAuditEntry() { }

    public static CallRecordAuditEntry Create(
        Guid callRecordId, string action, string summary, string detailJson, Guid actorId, string actorName) => new()
    {
        Id = Guid.NewGuid(),
        CallRecordId = callRecordId,
        Action = action,
        Summary = summary,
        Detail = string.IsNullOrWhiteSpace(detailJson) ? "{}" : detailJson,
        ActorId = actorId,
        ActorName = actorName,
        CreatedAt = DateTimeOffset.UtcNow,
    };
}
