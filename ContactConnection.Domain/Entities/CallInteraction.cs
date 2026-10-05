using ContactConnection.Domain.ValueObjects;

namespace ContactConnection.Domain.Entities;

/// <summary>
/// A single independently-dispositioned interaction within a call.
/// One call can contain multiple interactions (order sale + subscription change, etc.).
/// See ARCHITECTURE.md §22.
/// </summary>
public class CallInteraction
{
    public Guid Id { get; private set; }
    public Guid CallRecordId { get; private set; }
    public int InteractionNumber { get; private set; }   // Sequence within the call (1, 2, 3...)
    public string Type { get; private set; } = string.Empty;
    public Guid? FlowId { get; private set; }
    public int? FlowVersion { get; private set; }
    public string? Disposition { get; private set; }
    public string? FlowExecutionState { get; private set; }  // JSONB — owned by flow engine
    public List<CommitmentEvent> CommitmentEvents { get; private set; } = [];
    public string? CustomFields { get; private set; }        // JSONB — denormalized snapshot
    public Guid? CartId { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string Status { get; private set; } = InteractionStatus.Active;

    // Required by EF Core
    private CallInteraction() { }

    public static CallInteraction Create(Guid callRecordId, int interactionNumber, string type, Guid? id = null)
    {
        return new CallInteraction
        {
            Id = id is { } given && given != Guid.Empty ? given : Guid.NewGuid(),
            CallRecordId = callRecordId,
            InteractionNumber = interactionNumber,
            Type = type,
            Status = InteractionStatus.Active,
            StartedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>Who handled this interaction, and for which campaign (S178). On a call transferred mid-call
    /// (sales → CS) each agent's work is its own interaction, while the call record keeps the original
    /// campaign, agent and routing tier for attribution and commissions.</summary>
    public Guid? AgentId { get; private set; }
    public Guid? CampaignId { get; private set; }

    /// <summary>Merge one field into this interaction's own custom fields (a transferred interaction's script
    /// writes, S178). Values are the typed values, serialized as JSON.</summary>
    public void SetCustomField(string fieldName, object? value)
    {
        var map = string.IsNullOrEmpty(CustomFields)
            ? new Dictionary<string, object?>()
            : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(CustomFields) ?? [];
        map[fieldName] = value;
        CustomFields = System.Text.Json.JsonSerializer.Serialize(map);
    }

    public void AssignTo(Guid agentId, Guid campaignId)
    {
        AgentId = agentId;
        CampaignId = campaignId == Guid.Empty ? null : campaignId;
    }

    public void SetFlow(Guid flowId, int flowVersion)
    {
        FlowId = flowId;
        FlowVersion = flowVersion;
    }

    public void Complete(string disposition)
    {
        Disposition = disposition;
        Status = InteractionStatus.Complete;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void MarkIncomplete()
    {
        Status = InteractionStatus.Incomplete;
    }

    public void AddCommitmentEvent(CommitmentEvent evt)
    {
        CommitmentEvents.Add(evt);
    }

    public void SetFlowExecutionState(string stateJson)
    {
        FlowExecutionState = stateJson;
    }

    public void SetCartId(Guid cartId) => CartId = cartId;
}

public static class InteractionStatus
{
    public const string Active = "active";
    public const string Complete = "complete";
    public const string Incomplete = "incomplete";
}

public static class InteractionType
{
    public const string OrderSale = "order_sale";
    public const string LeadCapture = "lead_capture";
    public const string AccountChange = "account_change";
    public const string SubscriptionChange = "subscription_change";
    public const string CustomerService = "customer_service";
    public const string PaymentUpdate = "payment_update";
    public const string ReturnRequest = "return_request";
    public const string InformationOnly = "information_only";
    public const string OutboundFollowUp = "outbound_follow_up";
    public const string AutoshipAttempt = "autoship_attempt";
}
