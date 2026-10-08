using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data.Configurations;
using ContactConnection.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Data;

/// <summary>
/// DbContext for all per-tenant tables (call_records, call_interactions, etc.).
/// Does NOT set a default schema — table names are unqualified.
/// The correct tenant schema is applied via PostgreSQL search_path on the connection,
/// set by TenantDbContextFactory for each request. See ARCHITECTURE.md §6.
/// </summary>
public class TenantDbContext : DbContext
{
    public TenantDbContext(DbContextOptions<TenantDbContext> options) : base(options) { }

    public DbSet<Agent> Agents => Set<Agent>();
    // Team chat (S183)
    public DbSet<ChatChannel> ChatChannels => Set<ChatChannel>();
    public DbSet<ChatMember> ChatMembers => Set<ChatMember>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<ChatReaction> ChatReactions => Set<ChatReaction>();
    public DbSet<ChatPersonalPin> ChatPersonalPins => Set<ChatPersonalPin>();
    public DbSet<ChatFile> ChatFiles => Set<ChatFile>();
    public DbSet<AgentSupervisor> AgentSupervisors => Set<AgentSupervisor>();
    public DbSet<HelpRequest> HelpRequests => Set<HelpRequest>();
    public DbSet<AgentDedication> AgentDedications => Set<AgentDedication>();
    public DbSet<ScreenViewSession> ScreenViewSessions => Set<ScreenViewSession>();
    public DbSet<CoachingNote> CoachingNotes => Set<CoachingNote>();
    public DbSet<RemoteAction> RemoteActions => Set<RemoteAction>();
    public DbSet<Helpdesk> Helpdesks => Set<Helpdesk>();
    public DbSet<HelpdeskTopic> HelpdeskTopics => Set<HelpdeskTopic>();
    public DbSet<HelpdeskFile> HelpdeskFiles => Set<HelpdeskFile>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<CallRecord> CallRecords => Set<CallRecord>();
    public DbSet<CallInteraction> CallInteractions => Set<CallInteraction>();
    public DbSet<ScreenRecording> ScreenRecordings => Set<ScreenRecording>();
    public DbSet<RecordingMergeJob> RecordingMergeJobs => Set<RecordingMergeJob>();
    public DbSet<Voicemail> Voicemails => Set<Voicemail>();
    public DbSet<ScheduledCallback> ScheduledCallbacks => Set<ScheduledCallback>();
    public DbSet<Flow> Flows => Set<Flow>();
    public DbSet<FlowSession> FlowSessions => Set<FlowSession>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductKit> ProductKits => Set<ProductKit>();
    public DbSet<Offer> Offers => Set<Offer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<ProductAttribute> ProductAttributes => Set<ProductAttribute>();
    public DbSet<ProductAttributeValue> ProductAttributeValues => Set<ProductAttributeValue>();
    public DbSet<CustomFieldDefinition> CustomFieldDefinitions => Set<CustomFieldDefinition>();
    public DbSet<CustomFieldValue> CustomFieldValues => Set<CustomFieldValue>();
    public DbSet<TenantApiDefinition> TenantApiDefinitions => Set<TenantApiDefinition>();
    public DbSet<TenantApiEndpoint> TenantApiEndpoints => Set<TenantApiEndpoint>();
    public DbSet<TenantApiPreference> TenantApiPreferences => Set<TenantApiPreference>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<PhoneNumber> PhoneNumbers => Set<PhoneNumber>();
    public DbSet<NumberProvider> NumberProviders => Set<NumberProvider>();
    public DbSet<AgentCampaignAssignment> AgentCampaignAssignments => Set<AgentCampaignAssignment>();
    public DbSet<AgentGroup> AgentGroups => Set<AgentGroup>();
    public DbSet<AgentGroupMember> AgentGroupMembers => Set<AgentGroupMember>();
    public DbSet<AgentGroupMemberCampaignExclusion> AgentGroupMemberCampaignExclusions => Set<AgentGroupMemberCampaignExclusion>();
    public DbSet<ExternalRoutingRequest> ExternalRoutingRequests => Set<ExternalRoutingRequest>();
    public DbSet<GroupCampaignAssignment> GroupCampaignAssignments => Set<GroupCampaignAssignment>();
    public DbSet<CampaignExternalNumber> CampaignExternalNumbers => Set<CampaignExternalNumber>();
    public DbSet<BlockListEntry> BlockListEntries => Set<BlockListEntry>();
    public DbSet<AudioFile> AudioFiles => Set<AudioFile>();
    public DbSet<CustomUnavailableCode> CustomUnavailableCodes => Set<CustomUnavailableCode>();
    public DbSet<CallTraceEvent> CallTraceEvents => Set<CallTraceEvent>();
    public DbSet<AgentStateHistoryEntry> AgentStateHistory => Set<AgentStateHistoryEntry>();
    public DbSet<CallStateHistoryEntry> CallStateHistory => Set<CallStateHistoryEntry>();
    public DbSet<Dashboard> Dashboards => Set<Dashboard>();
    public DbSet<EntityVersion> EntityVersions => Set<EntityVersion>();
    public DbSet<CredentialAuditEntry> CredentialAuditEntries => Set<CredentialAuditEntry>();
    public DbSet<WebhookEndpoint> WebhookEndpoints => Set<WebhookEndpoint>();
    public DbSet<WebhookEvent> WebhookEvents => Set<WebhookEvent>();
    public DbSet<StoredValue> StoredValues => Set<StoredValue>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();
    public DbSet<OrderNumberSequence> OrderNumberSequences => Set<OrderNumberSequence>();
    public DbSet<MediaAgency> MediaAgencies => Set<MediaAgency>();
    public DbSet<MediaAssignment> MediaAssignments => Set<MediaAssignment>();
    public DbSet<CommissionRule> CommissionRules => Set<CommissionRule>();
    public DbSet<CommissionEntry> CommissionEntries => Set<CommissionEntry>();
    public DbSet<CommissionRecalcBatch> CommissionRecalcBatches => Set<CommissionRecalcBatch>();
    public DbSet<MediaAssignmentChange> MediaAssignmentChanges => Set<MediaAssignmentChange>();
    public DbSet<MediaReplayBatch> MediaReplayBatches => Set<MediaReplayBatch>();
    public DbSet<CallSummary> CallSummaries => Set<CallSummary>();
    public DbSet<CallRecordAuditEntry> CallRecordAuditEntries => Set<CallRecordAuditEntry>();
    public DbSet<OutboundDialAttempt> OutboundDialAttempts => Set<OutboundDialAttempt>();
    public DbSet<ExportDefinition> ExportDefinitions => Set<ExportDefinition>();
    public DbSet<ExportRun> ExportRuns => Set<ExportRun>();
    public DbSet<ExportDelivery> ExportDeliveries => Set<ExportDelivery>();
    public DbSet<ExportAuditEntry> ExportAuditEntries => Set<ExportAuditEntry>();
    public DbSet<ExportKey> ExportKeys => Set<ExportKey>();
    public DbSet<DispositionCategory> DispositionCategories => Set<DispositionCategory>();
    public DbSet<Disposition> Dispositions => Set<Disposition>();
    public DbSet<CustomKpi> CustomKpis => Set<CustomKpi>();
    public DbSet<ClientUser> ClientUsers => Set<ClientUser>();
    public DbSet<ClientUserDashboard> ClientUserDashboards => Set<ClientUserDashboard>();
    public DbSet<ClientUserAuditEntry> ClientUserAudit => Set<ClientUserAuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new RoleConfiguration());
        modelBuilder.ApplyConfiguration(new AgentConfiguration());
        modelBuilder.ApplyConfiguration(new CallRecordConfiguration());
        modelBuilder.ApplyConfiguration(new CallRecordAuditEntryConfiguration());
        modelBuilder.ApplyConfiguration(new CallInteractionConfiguration());
        modelBuilder.ApplyConfiguration(new ScreenRecordingConfiguration());
        modelBuilder.ApplyConfiguration(new RecordingMergeJobConfiguration());
        modelBuilder.ApplyConfiguration(new VoicemailConfiguration());
        modelBuilder.ApplyConfiguration(new ScheduledCallbackConfiguration());
        modelBuilder.ApplyConfiguration(new FlowConfiguration());
        modelBuilder.ApplyConfiguration(new FlowSessionConfiguration());
        modelBuilder.ApplyConfiguration(new ProductConfiguration());
        modelBuilder.ApplyConfiguration(new ProductKitConfiguration());
        modelBuilder.ApplyConfiguration(new OfferConfiguration());
        modelBuilder.ApplyConfiguration(new OrderConfiguration());
        modelBuilder.ApplyConfiguration(new OrderLineConfiguration());
        modelBuilder.ApplyConfiguration(new SubscriptionConfiguration());
        modelBuilder.ApplyConfiguration(new ProductCategoryConfiguration());
        modelBuilder.ApplyConfiguration(new ProductAttributeConfiguration());
        modelBuilder.ApplyConfiguration(new ProductAttributeValueConfiguration());
        modelBuilder.ApplyConfiguration(new CustomFieldDefinitionConfiguration());
        modelBuilder.ApplyConfiguration(new CustomFieldValueConfiguration());
        modelBuilder.ApplyConfiguration(new TenantApiDefinitionConfiguration());
        modelBuilder.ApplyConfiguration(new TenantApiEndpointConfiguration());
        modelBuilder.ApplyConfiguration(new TenantApiPreferenceConfiguration());
        modelBuilder.ApplyConfiguration(new ClientConfiguration());
        modelBuilder.ApplyConfiguration(new CampaignConfiguration());
        modelBuilder.ApplyConfiguration(new PhoneNumberConfiguration());
        modelBuilder.ApplyConfiguration(new NumberProviderConfiguration());
        modelBuilder.ApplyConfiguration(new AgentGroupMemberCampaignExclusionConfiguration());
        modelBuilder.ApplyConfiguration(new ExternalRoutingRequestConfiguration());
        modelBuilder.ApplyConfiguration(new AgentCampaignAssignmentConfiguration());
        modelBuilder.ApplyConfiguration(new AgentGroupConfiguration());
        modelBuilder.ApplyConfiguration(new AgentGroupMemberConfiguration());
        modelBuilder.ApplyConfiguration(new GroupCampaignAssignmentConfiguration());
        modelBuilder.ApplyConfiguration(new CampaignExternalNumberConfiguration());
        modelBuilder.ApplyConfiguration(new BlockListConfiguration());
        modelBuilder.ApplyConfiguration(new AudioFileConfiguration());
        modelBuilder.ApplyConfiguration(new CustomUnavailableCodeConfiguration());
        modelBuilder.ApplyConfiguration(new CallTraceEventConfiguration());
        modelBuilder.ApplyConfiguration(new AgentStateHistoryConfiguration());
        modelBuilder.ApplyConfiguration(new CallStateHistoryConfiguration());
        modelBuilder.ApplyConfiguration(new DashboardConfiguration());
        modelBuilder.ApplyConfiguration(new EntityVersionConfiguration());
        modelBuilder.ApplyConfiguration(new CredentialAuditEntryConfiguration());
        modelBuilder.ApplyConfiguration(new WebhookEndpointConfiguration());
        modelBuilder.ApplyConfiguration(new WebhookEventConfiguration());
        modelBuilder.ApplyConfiguration(new StoredValueConfiguration());
        modelBuilder.ApplyConfiguration(new PaymentTransactionConfiguration());
        modelBuilder.ApplyConfiguration(new OrderNumberSequenceConfiguration());
        modelBuilder.ApplyConfiguration(new MediaAgencyConfiguration());
        modelBuilder.ApplyConfiguration(new MediaAssignmentConfiguration());
        modelBuilder.ApplyConfiguration(new CommissionRuleConfiguration());
        modelBuilder.ApplyConfiguration(new CommissionEntryConfiguration());
        modelBuilder.ApplyConfiguration(new CommissionRecalcBatchConfiguration());
        modelBuilder.ApplyConfiguration(new MediaAssignmentChangeConfiguration());
        modelBuilder.ApplyConfiguration(new MediaReplayBatchConfiguration());
        modelBuilder.ApplyConfiguration(new CallSummaryConfiguration());
        modelBuilder.ApplyConfiguration(new OutboundDialAttemptConfiguration());
        modelBuilder.ApplyConfiguration(new ExportDefinitionConfiguration());
        modelBuilder.ApplyConfiguration(new ExportRunConfiguration());
        modelBuilder.ApplyConfiguration(new ExportDeliveryConfiguration());
        modelBuilder.ApplyConfiguration(new ExportAuditEntryConfiguration());
        modelBuilder.ApplyConfiguration(new ExportKeyConfiguration());
        modelBuilder.ApplyConfiguration(new DispositionCategoryConfiguration());
        modelBuilder.ApplyConfiguration(new DispositionConfiguration());
        modelBuilder.ApplyConfiguration(new CustomKpiConfiguration());
        modelBuilder.ApplyConfiguration(new ClientUserConfiguration());
        modelBuilder.ApplyConfiguration(new ClientUserDashboardConfiguration());
        modelBuilder.ApplyConfiguration(new ClientUserAuditEntryConfiguration());
        modelBuilder.ApplyConfiguration(new ChatChannelConfiguration());
        modelBuilder.ApplyConfiguration(new ChatMemberConfiguration());
        modelBuilder.ApplyConfiguration(new ChatMessageConfiguration());
        modelBuilder.ApplyConfiguration(new ChatReactionConfiguration());
        modelBuilder.ApplyConfiguration(new ChatPersonalPinConfiguration());
        modelBuilder.ApplyConfiguration(new ChatFileConfiguration());
        modelBuilder.ApplyConfiguration(new AgentSupervisorConfiguration());
        modelBuilder.ApplyConfiguration(new HelpRequestConfiguration());
        modelBuilder.ApplyConfiguration(new AgentDedicationConfiguration());
        modelBuilder.ApplyConfiguration(new ScreenViewSessionConfiguration());
        modelBuilder.ApplyConfiguration(new CoachingNoteConfiguration());
        modelBuilder.ApplyConfiguration(new RemoteActionConfiguration());
        modelBuilder.ApplyConfiguration(new HelpdeskConfiguration());
        modelBuilder.ApplyConfiguration(new HelpdeskTopicConfiguration());
        modelBuilder.ApplyConfiguration(new HelpdeskFileConfiguration());
        base.OnModelCreating(modelBuilder);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        // Auto-update updated_at on any modified CallRecord
        foreach (var entry in ChangeTracker.Entries<CallRecord>())
        {
            if (entry.State == EntityState.Modified)
                entry.Property("UpdatedAt").CurrentValue = DateTimeOffset.UtcNow;
        }
        await StampAgentGroupsAsync(ct);
        return await base.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Agent-group snapshot (S181): an interaction that just got its agent records the groups that agent is in right now,
    /// so reporting by group stays true to the time of the call. Done here so every path that assigns an agent is covered.
    /// </summary>
    private async Task StampAgentGroupsAsync(CancellationToken ct)
    {
        var pending = ChangeTracker.Entries<CallInteraction>()
            .Where(e => e.Entity.AgentId is { } a && a != Guid.Empty
                        && (e.State == EntityState.Added || (e.State == EntityState.Modified && e.Property(i => i.AgentId).IsModified)))
            .Select(e => e.Entity).ToList();
        if (pending.Count == 0) return;
        var agentIds = pending.Select(i => i.AgentId!.Value).Distinct().ToList();
        var memberships = await AgentGroupMembers.AsNoTracking().Where(m => agentIds.Contains(m.AgentId))
            .Select(m => new { m.AgentId, m.GroupId }).ToListAsync(ct);
        foreach (var ix in pending)
            ix.SetAgentGroups(memberships.Where(m => m.AgentId == ix.AgentId).Select(m => m.GroupId));
    }
}
