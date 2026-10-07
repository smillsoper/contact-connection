using ContactConnection.Domain.ValueObjects;
using ContactConnection.Domain.ValueObjects.Commerce;

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
    /// <summary>The catalog disposition the recorded text matched (S181); null = none recorded, or unmapped text.</summary>
    public Guid? DispositionId { get; private set; }
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

    // ── Commerce (interaction-scoped, S178 — docs/interaction-scoped-commerce.md) ──────────────────────
    // Each agent's piece of work has its own cart and order: a sales agent's order and a CS agent's free-form
    // order on the same call stay separate. The customer and addresses stay on the call record.

    public CartDocument? Cart { get; private set; }                 // JSONB
    /// <summary>Order number from the client's sequence; assigned once, lazily, by IOrderNumberService.</summary>
    public string? OrderNumber { get; private set; }
    /// <summary>When this interaction's order first went through (order-based commission rules wait for it).</summary>
    public DateTimeOffset? OrderSubmittedAt { get; private set; }
    public decimal? TotalAmount { get; private set; }
    public decimal? TaxAmount { get; private set; }
    public string? PaymentStatus { get; private set; }
    /// <summary>The parallel-queuing route this interaction's agent won the call through (commission reporting).</summary>
    public Guid? RoutedGroupId { get; private set; }
    public int? RoutedTier { get; private set; }
    public string? RoutedTierLabel { get; private set; }
    /// <summary>
    /// The agent groups this interaction's agent belonged to when they took it (S181) — so reporting by agent group stays
    /// true to the time of the call when people later move between groups. Stamped when the agent is assigned.
    /// </summary>
    public List<Guid> AgentGroupIds { get; private set; } = [];

    public void SetAgentGroups(IEnumerable<Guid> groupIds) => AgentGroupIds = groupIds.Distinct().ToList();

    /// <summary>True if this interaction counts for the group: its agent was a member at the time, or it was routed there.</summary>
    public bool InGroup(Guid groupId) => RoutedGroupId == groupId || AgentGroupIds.Contains(groupId);

    public void SetCart(CartDocument cart) => Cart = cart;

    public void SetOrderNumber(string orderNumber) => OrderNumber ??= orderNumber;

    /// <summary>Keeps the first time — resubmits don't move it.</summary>
    public void MarkOrderSubmitted(DateTimeOffset at) => OrderSubmittedAt ??= at;

    public void SetFinancials(decimal totalAmount, decimal taxAmount, string paymentStatus)
    {
        TotalAmount = totalAmount;
        TaxAmount = taxAmount;
        PaymentStatus = paymentStatus;
    }

    public void SetRoutedTier(Guid? groupId, int tier, string? tierLabel)
    {
        RoutedGroupId = groupId;
        RoutedTier = tier;
        RoutedTierLabel = tierLabel;
    }

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

    /// <summary>The disposition as it stands now — re-synced whenever the disposition field is written after completion
    /// (Call Records edit, AI summary confirm) so the interaction never keeps a stale copy (S181).</summary>
    public void SetDisposition(string? text, Guid? dispositionId)
    {
        Disposition = string.IsNullOrWhiteSpace(text) ? Disposition : text.Trim();
        DispositionId = dispositionId;
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
