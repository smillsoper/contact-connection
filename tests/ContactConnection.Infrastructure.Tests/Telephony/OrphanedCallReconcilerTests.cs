using System.Reflection;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Telephony;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony;

/// <summary>
/// Covers <see cref="OrphanedCallReconciler"/> — the startup sweep that closes calls a dead
/// process left non-terminal. Guards: only calls with no live Redis session and older than the
/// grace window are touched; both the <c>call_records</c> row and a dangling
/// <c>call_state_history</c> timeline get closed.
///
/// The reconciler opens (and <c>await using</c>-disposes) a fresh <see cref="TenantDbContext"/>
/// per tenant via the factory, so each test points that factory at a named in-memory database
/// and asserts through its own separate context on the same store.
/// </summary>
public class OrphanedCallReconcilerTests
{
    private const string Schema = "tenant_test_tenant";

    private static void Backdate(CallRecord record, TimeSpan ago)
    {
        var when = DateTimeOffset.UtcNow - ago;
        // CreatedAt has a private setter and is stamped to "now" by the factory; reach the
        // compiler-generated backing field to simulate a record from a previous process.
        typeof(CallRecord)
            .GetField("<CreatedAt>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(record, when);
    }

    private sealed class Harness
    {
        public required string DbName { get; init; }
        public required ContactConnectionDbContext PlatformDb { get; init; }
        public required Mock<ITelephonyCallSessionStore> SessionStore { get; init; }
        public required Mock<ICallStateHistoryRepository> StateHistory { get; init; }
        public required Mock<IDashboardNotifier> Notifier { get; init; }
        public required OrphanedCallReconciler Reconciler { get; init; }

        public TenantDbContext OpenTenantDb() =>
            new(new DbContextOptionsBuilder<TenantDbContext>().UseInMemoryDatabase(DbName).Options);
    }

    private static Harness NewHarness(
        Tenant tenant,
        TelephonyCallSession[]? liveSessions = null,
        NonTerminalCall[]? nonTerminal = null,
        int minAgeMinutes = 15)
    {
        var dbName = Guid.NewGuid().ToString();

        var platformDb = new ContactConnectionDbContext(
            new DbContextOptionsBuilder<ContactConnectionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        platformDb.Tenants.Add(tenant);
        platformDb.SaveChanges();

        var factory = new Mock<ITenantDbContextFactory>();
        factory.Setup(f => f.Create(It.IsAny<string>()))
            .Returns(() => new TenantDbContext(
                new DbContextOptionsBuilder<TenantDbContext>().UseInMemoryDatabase(dbName).Options));

        var sessionStore = new Mock<ITelephonyCallSessionStore>();
        sessionStore.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(liveSessions ?? []);

        var stateHistory = new Mock<ICallStateHistoryRepository>();
        stateHistory.Setup(r => r.GetNonTerminalCallsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((nonTerminal ?? []).ToList());
        stateHistory.Setup(r => r.GetMaxSequenceAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3); // existing timeline has rows through sequence 3

        var notifier = new Mock<IDashboardNotifier>();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Telephony:OrphanReconciliation:MinAgeMinutes"] = minAgeMinutes.ToString(),
            })
            .Build();

        var reconciler = new OrphanedCallReconciler(
            platformDb, factory.Object, sessionStore.Object, stateHistory.Object, notifier.Object,
            config, NullLogger<OrphanedCallReconciler>.Instance);

        return new Harness
        {
            DbName       = dbName,
            PlatformDb   = platformDb,
            SessionStore = sessionStore,
            StateHistory = stateHistory,
            Notifier     = notifier,
            Reconciler   = reconciler,
        };
    }

    private static Tenant NewTenant()
    {
        var t = Tenant.Create("Test Tenant", "test-tenant", "America/Chicago");
        Assert.Equal(Schema, t.SchemaName); // Tenant.Create normalizes "test-tenant" → "tenant_test_tenant"
        return t;
    }

    private static CallRecord SeededRecord(Harness h, Tenant tenant, string channel, TimeSpan age, bool completed = false)
    {
        var record = CallRecord.CreateInbound(tenant.Id, "+15551234567", contactIdExternal: channel);
        Backdate(record, age);
        if (completed) record.Complete();

        using var db = h.OpenTenantDb();
        db.CallRecords.Add(record);
        db.SaveChanges();
        return record;
    }

    // ── call_records sweep ──────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_ClosesOpenRecord_OlderThanGrace_WithNoLiveSession()
    {
        var tenant = NewTenant();
        var h = NewHarness(tenant);
        var record = SeededRecord(h, tenant, "chan-abc", TimeSpan.FromHours(1));

        var result = await h.Reconciler.RunAsync();

        Assert.Equal(1, result.CallRecordsClosed);
        using var db = h.OpenTenantDb();
        var reloaded = await db.CallRecords.AsNoTracking().SingleAsync(r => r.Id == record.Id);
        Assert.NotNull(reloaded.CallEndAt);
        Assert.NotEqual(CallRecordStatus.Active, reloaded.OverallStatus);
    }

    [Fact]
    public async Task RunAsync_SkipsOpenRecord_WithLiveSessionByRecordId()
    {
        var tenant = NewTenant();
        var h = NewHarness(tenant);
        var record = SeededRecord(h, tenant, "chan-abc", TimeSpan.FromHours(1));
        h.SessionStore.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TelephonyCallSession { ChannelUuid = "other-chan", CallRecordId = record.Id }]);

        var result = await h.Reconciler.RunAsync();

        Assert.Equal(0, result.CallRecordsClosed);
        using var db = h.OpenTenantDb();
        Assert.Null((await db.CallRecords.AsNoTracking().SingleAsync(r => r.Id == record.Id)).CallEndAt);
    }

    [Fact]
    public async Task RunAsync_SkipsOpenRecord_WithLiveSessionByChannelUuid()
    {
        var tenant = NewTenant();
        var h = NewHarness(tenant,
            liveSessions: [new TelephonyCallSession { ChannelUuid = "chan-live", CallRecordId = Guid.NewGuid() }]);
        var record = SeededRecord(h, tenant, "chan-live", TimeSpan.FromHours(1));

        var result = await h.Reconciler.RunAsync();

        Assert.Equal(0, result.CallRecordsClosed);
        using var db = h.OpenTenantDb();
        Assert.Null((await db.CallRecords.AsNoTracking().SingleAsync(r => r.Id == record.Id)).CallEndAt);
    }

    [Fact]
    public async Task RunAsync_SkipsOpenRecord_InsideGraceWindow()
    {
        var tenant = NewTenant();
        var h = NewHarness(tenant);
        var record = SeededRecord(h, tenant, "chan-fresh", TimeSpan.FromMinutes(2)); // < 15-minute grace

        var result = await h.Reconciler.RunAsync();

        Assert.Equal(0, result.CallRecordsClosed);
        using var db = h.OpenTenantDb();
        Assert.Null((await db.CallRecords.AsNoTracking().SingleAsync(r => r.Id == record.Id)).CallEndAt);
    }

    [Fact]
    public async Task RunAsync_IgnoresAlreadyClosedRecord()
    {
        var tenant = NewTenant();
        var h = NewHarness(tenant);
        SeededRecord(h, tenant, "chan-done", TimeSpan.FromHours(1), completed: true);

        var result = await h.Reconciler.RunAsync();

        Assert.Equal(0, result.CallRecordsClosed);
    }

    // ── call_state_history sweep ────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_AppendsTerminalStateRow_ForDanglingNonTerminalTimeline()
    {
        var tenant = NewTenant();
        var campaignId = Guid.NewGuid();
        var record = CallRecord.CreateInbound(tenant.Id, "+15551234567", contactIdExternal: "chan-dangle");
        Backdate(record, TimeSpan.FromHours(1));
        record.Complete(); // record itself is closed; only the timeline dangles

        var h = NewHarness(tenant, nonTerminal: [new NonTerminalCall(record.Id, campaignId)]);
        using (var db = h.OpenTenantDb()) { db.CallRecords.Add(record); db.SaveChanges(); }

        var result = await h.Reconciler.RunAsync();

        Assert.Equal(1, result.StateTimelinesClosed);
        h.StateHistory.Verify(r => r.AddAsync(
            It.Is<CallStateHistoryEntry>(e =>
                e.CallRecordId == record.Id &&
                e.State == CallHistoryState.Completed &&
                e.CampaignId == campaignId &&
                e.Sequence == 4 &&                       // GetMaxSequenceAsync stub returns 3
                e.AgentId == null &&
                e.Detail != null),
            Schema, It.IsAny<CancellationToken>()), Times.Once);
        h.Notifier.Verify(n => n.NotifyCallStateChangedAsync(
            tenant.Id, campaignId, CallHistoryState.Completed, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_SkipsDanglingTimeline_WhenRecordHasLiveSession()
    {
        var tenant = NewTenant();
        var h = NewHarness(tenant);
        var record = SeededRecord(h, tenant, "chan-x", TimeSpan.FromHours(1));
        h.SessionStore.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TelephonyCallSession { ChannelUuid = "chan-x", CallRecordId = record.Id }]);
        h.StateHistory.Setup(r => r.GetNonTerminalCallsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new NonTerminalCall(record.Id, Guid.NewGuid())]);

        var result = await h.Reconciler.RunAsync();

        Assert.Equal(0, result.CallRecordsClosed);
        Assert.Equal(0, result.StateTimelinesClosed);
        h.StateHistory.Verify(r => r.AddAsync(
            It.IsAny<CallStateHistoryEntry>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        h.Notifier.Verify(n => n.NotifyCallStateChangedAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_SkipsDanglingTimeline_ForVanishedRecord()
    {
        var tenant = NewTenant();
        var h = NewHarness(tenant, nonTerminal: [new NonTerminalCall(Guid.NewGuid(), Guid.NewGuid())]);

        var result = await h.Reconciler.RunAsync();

        Assert.Equal(0, result.StateTimelinesClosed);
    }

    [Fact]
    public async Task RunAsync_ClosesBothRecordAndTimeline_InOnePass()
    {
        var tenant = NewTenant();
        var campaignId = Guid.NewGuid();
        var h = NewHarness(tenant);
        var record = SeededRecord(h, tenant, "chan-both", TimeSpan.FromHours(1));
        h.StateHistory.Setup(r => r.GetNonTerminalCallsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new NonTerminalCall(record.Id, campaignId)]);

        var result = await h.Reconciler.RunAsync();

        Assert.Equal(1, result.CallRecordsClosed);
        Assert.Equal(1, result.StateTimelinesClosed);
        Assert.Equal(1, result.TenantsScanned);
    }

    [Fact]
    public async Task RunAsync_SkipsInactiveTenants()
    {
        var tenant = NewTenant();
        tenant.Deactivate();
        var h = NewHarness(tenant);
        SeededRecord(h, tenant, "chan-inactive", TimeSpan.FromHours(1));

        var result = await h.Reconciler.RunAsync();

        Assert.Equal(0, result.TenantsScanned);
        Assert.Equal(0, result.CallRecordsClosed);
    }
}
