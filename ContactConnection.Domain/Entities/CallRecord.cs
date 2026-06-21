using ContactConnection.Domain.ValueObjects;
using ContactConnection.Domain.ValueObjects.Commerce;

namespace ContactConnection.Domain.Entities;

/// <summary>
/// The single authoritative source for everything that happened on a call.
/// CDR data and CRM data live here together — never in separate systems requiring reconciliation.
/// See ARCHITECTURE.md §19.
/// </summary>
public class CallRecord
{
    // Identity
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ClientId { get; private set; }
    public Guid CampaignId { get; private set; }
    public Guid? AgentId { get; private set; }

    // Call metadata
    public string Source { get; private set; } = CallSource.Inbound;
    public string RecordType { get; private set; } = CallRecordType.Full;
    public string OverallStatus { get; private set; } = CallRecordStatus.Active;

    // Caller identity — relational, queried frequently
    public string? CallerId { get; private set; }        // ANI / inbound phone number
    public string? AccountNumber { get; private set; }
    public string? FirstName { get; private set; }
    public string? LastName { get; private set; }
    public string? Email { get; private set; }
    public string? Phone { get; private set; }

    // Timing — relational, aggregated constantly
    public DateTimeOffset? CallStartAt { get; private set; }
    public DateTimeOffset? CallEndAt { get; private set; }
    public int? HandleTimeSeconds { get; private set; }  // Generated stored column in PostgreSQL

    // Financial summary — relational, reporting
    public decimal? TotalAmount { get; private set; }
    public decimal? TaxAmount { get; private set; }
    public string? PaymentStatus { get; private set; }

    // Fulfillment summary — relational, operations
    public string? FulfillmentStatus { get; private set; }
    public string? TrackingNumber { get; private set; }

    // Telephony
    public string? ContactIdExternal { get; private set; }
    public string? RecordingUrl { get; private set; }

    // JSONB — variable/nested, not frequently queried by field
    public CallAddresses? Addresses { get; private set; }
    public List<CommitmentEvent> CommitmentEvents { get; private set; } = [];
    public CartDocument? Cart { get; private set; }             // JSONB — commerce engine owns this
    public string? FlowExecutionState { get; private set; }   // JSONB — flow engine owns this
    public string? CustomFields { get; private set; }         // JSONB — denormalized snapshot
    public string? ApiResponseCache { get; private set; }     // JSONB — adapter framework owns this
    public string? TelephonyEvents { get; private set; }      // JSONB — telephony layer owns this

    // Sensitive data lifecycle — PCI. See ARCHITECTURE.md §24
    public string? SensitiveData { get; private set; }        // AES-256 encrypted JSONB
    public DateTimeOffset? SensitiveDataStoredAt { get; private set; }
    public DateTimeOffset? SensitiveDataWipedAt { get; private set; }
    public string? SensitiveWipeReason { get; private set; }

    // Audit
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // Navigation
    private readonly List<CallInteraction> _interactions = [];
    public IReadOnlyList<CallInteraction> Interactions => _interactions.AsReadOnly();

    // Required by EF Core
    private CallRecord() { }

    public static CallRecord Create(
        Guid tenantId,
        Guid clientId,
        Guid campaignId,
        string source = CallSource.Inbound,
        string? callerId = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new CallRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ClientId = clientId,
            CampaignId = campaignId,
            Source = source,
            RecordType = CallRecordType.Full,
            OverallStatus = CallRecordStatus.Active,
            CallerId = callerId,
            CallStartAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    /// <summary>
    /// Creates a call record for an inbound telephony event before a client/campaign is known.
    /// ClientId and CampaignId are left as Guid.Empty and should be resolved during the call.
    /// </summary>
    public static CallRecord CreateInbound(
        Guid tenantId,
        string? callerId,
        Guid? agentId = null,
        string? contactIdExternal = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new CallRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ClientId = Guid.Empty,
            CampaignId = Guid.Empty,
            Source = CallSource.Inbound,
            RecordType = CallRecordType.Full,
            OverallStatus = CallRecordStatus.Active,
            CallerId = callerId,
            AgentId = agentId,
            ContactIdExternal = contactIdExternal,
            CallStartAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    /// <summary>
    /// Creates a call record for an agent-initiated outbound call before a client/campaign is known.
    /// dialedNumber is stored as CallerId (the number being called); resolved during the call.
    /// </summary>
    public static CallRecord CreateOutbound(
        Guid tenantId,
        string? dialedNumber,
        Guid? agentId = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new CallRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ClientId = Guid.Empty,
            CampaignId = Guid.Empty,
            Source = CallSource.Outbound,
            RecordType = CallRecordType.Full,
            OverallStatus = CallRecordStatus.Active,
            CallerId = dialedNumber,
            AgentId = agentId,
            CallStartAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    /// <summary>Creates a stub record for telephony events (abandon, callback, etc.) with no agent session.</summary>
    public static CallRecord CreateStub(Guid tenantId, Guid clientId, Guid campaignId, string? callerId = null)
    {
        var record = Create(tenantId, clientId, campaignId, CallSource.Inbound, callerId);
        record.RecordType = CallRecordType.Stub;
        return record;
    }

    public CallInteraction AddInteraction(string type)
    {
        var interaction = CallInteraction.Create(Id, _interactions.Count + 1, type);
        _interactions.Add(interaction);
        UpdatedAt = DateTimeOffset.UtcNow;
        return interaction;
    }

    public void SetAgent(Guid agentId)
    {
        AgentId = agentId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetCampaign(Guid campaignId)
    {
        CampaignId = campaignId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetContactIdExternal(string contactId)
    {
        ContactIdExternal = contactId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetCallerIdentity(
        string? firstName, string? lastName,
        string? email, string? phone,
        string? accountNumber)
    {
        FirstName = firstName;
        LastName = lastName;
        Email = email;
        Phone = phone;
        AccountNumber = accountNumber;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetAddresses(CallAddresses addresses)
    {
        Addresses = addresses;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Complete()
    {
        OverallStatus = DeriveOverallStatus();
        CallEndAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkIncomplete()
    {
        OverallStatus = CallRecordStatus.Incomplete;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void AddCommitmentEvent(CommitmentEvent evt)
    {
        CommitmentEvents.Add(evt);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetCart(CartDocument cart)
    {
        Cart      = cart;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateCustomFieldsSnapshot(string? snapshotJson)
    {
        CustomFields = snapshotJson;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetFinancials(decimal totalAmount, decimal taxAmount, string paymentStatus)
    {
        TotalAmount = totalAmount;
        TaxAmount = taxAmount;
        PaymentStatus = paymentStatus;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetFulfillment(string fulfillmentStatus, string? trackingNumber)
    {
        FulfillmentStatus = fulfillmentStatus;
        TrackingNumber = trackingNumber;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetRecording(string recordingUrl)
    {
        RecordingUrl = recordingUrl;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetExternalContactId(string contactIdExternal)
    {
        ContactIdExternal = contactIdExternal;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Derives call-level disposition from interaction outcomes.
    /// Not entered by agent — calculated from the record. See ARCHITECTURE.md §22.
    /// </summary>
    private string DeriveOverallStatus()
    {
        if (!_interactions.Any())
            return CallRecordStatus.Incomplete;

        var completed = _interactions.Where(i => i.Status == InteractionStatus.Complete).ToList();
        if (!completed.Any())
            return CallRecordStatus.Incomplete;

        return CallRecordStatus.Complete;
    }
}

public static class CallSource
{
    public const string Inbound = "inbound";
    public const string Outbound = "outbound";
    public const string Callback = "callback";
}

public static class CallRecordType
{
    public const string Full = "full";    // Full agent session
    public const string Stub = "stub";   // Telephony-only event (abandon, etc.)
}

public static class CallRecordStatus
{
    public const string Active = "active";
    public const string Complete = "complete";
    public const string Incomplete = "incomplete";
}
