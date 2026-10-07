using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Telephony;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony;

/// <summary>Agent dedications (S183) as routing sees them (EligibleAgentRanker), and the schedule maths.</summary>
public class AgentDedicationRoutingTests
{
    private static TenantDbContext NewDb() =>
        new(new DbContextOptionsBuilder<TenantDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static EligibleAgentRanker AllAvailable()
    {
        var store = new Mock<IAgentStateStore>();
        store.Setup(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), default))
            .ReturnsAsync(new AgentStateEntry(AgentStateCodes.Available, "Available", null, DateTimeOffset.UtcNow));
        return new EligibleAgentRanker(store.Object);
    }

    private static AgentDedication Dedicate(Guid agentId, params Guid[] campaigns) =>
        AgentDedication.Create(Guid.NewGuid(), agentId, campaigns, DedicationMode.Duration, DateTimeOffset.UtcNow.AddSeconds(-1),
            DateTimeOffset.UtcNow.AddHours(1), null, "UTC", null, Guid.NewGuid(), "Sup");

    [Fact]
    public async Task DedicatedElsewhere_NotOfferedTheirNormalCampaign()
    {
        await using var db = NewDb();
        var sales = Guid.NewGuid(); var support = Guid.NewGuid();
        var dedicated = Guid.NewGuid(); var other = Guid.NewGuid();
        db.AgentCampaignAssignments.Add(AgentCampaignAssignment.Create(dedicated, sales));
        db.AgentCampaignAssignments.Add(AgentCampaignAssignment.Create(other, sales));
        db.AgentDedications.Add(Dedicate(dedicated, support));
        await db.SaveChangesAsync();

        var ranked = await AllAvailable().GetRankedEligibleAgentsAsync(db, Guid.NewGuid(), sales);

        Assert.Equal([other], ranked.Select(r => r.AgentId));
    }

    [Fact]
    public async Task DedicatedWithoutAssignment_GetsTheCampaignsCalls()
    {
        await using var db = NewDb();
        var support = Guid.NewGuid(); var agent = Guid.NewGuid();
        db.AgentDedications.Add(Dedicate(agent, support));
        await db.SaveChangesAsync();

        var ranked = await AllAvailable().GetRankedEligibleAgentsAsync(db, Guid.NewGuid(), support);

        var r = Assert.Single(ranked);
        Assert.Equal(agent, r.AgentId);
        Assert.Equal(AgentDedications.DefaultProficiency, r.EffectiveProficiency);
        Assert.NotNull(await EligibleAgentRanker.ResolveRouteAsync(db, support, agent));
    }

    [Fact]
    public async Task EndedDedication_NormalAssignmentsResume()
    {
        await using var db = NewDb();
        var sales = Guid.NewGuid(); var support = Guid.NewGuid(); var agent = Guid.NewGuid();
        db.AgentCampaignAssignments.Add(AgentCampaignAssignment.Create(agent, sales));
        var d = Dedicate(agent, support);
        d.End(Guid.NewGuid(), DateTimeOffset.UtcNow);
        db.AgentDedications.Add(d);
        await db.SaveChangesAsync();

        Assert.Single(await AllAvailable().GetRankedEligibleAgentsAsync(db, Guid.NewGuid(), sales));
        Assert.Empty(await AllAvailable().GetRankedEligibleAgentsAsync(db, Guid.NewGuid(), support));
    }

    [Fact]
    public async Task DedicationDoesNotOpenAPinnedGroup()
    {
        await using var db = NewDb();
        var support = Guid.NewGuid(); var agent = Guid.NewGuid();
        db.AgentDedications.Add(Dedicate(agent, support));
        await db.SaveChangesAsync();

        Assert.Empty(await AllAvailable().GetRankedEligibleAgentsAsync(db, Guid.NewGuid(), support, restrictGroupId: Guid.NewGuid()));
    }

    // ── Schedule ──────────────────────────────────────────────────────────────

    private static readonly DateTimeOffset Wed1000Utc = new(2026, 10, 7, 10, 0, 0, TimeSpan.Zero); // a Wednesday

    private static AgentDedication Weekly(params DedicationWindow[] windows) =>
        AgentDedication.Create(Guid.NewGuid(), Guid.NewGuid(), [Guid.NewGuid()], DedicationMode.Schedule, Wed1000Utc.AddDays(-1),
            null, windows, "UTC", null, Guid.NewGuid(), "Sup");

    [Fact]
    public void Schedule_ActiveOnlyInsideItsWindows()
    {
        var d = Weekly(new DedicationWindow([1, 2, 3, 4, 5], "09:00", "13:00"));
        Assert.True(d.IsActiveAt(Wed1000Utc));
        Assert.False(d.IsActiveAt(Wed1000Utc.AddHours(3)));                // 13:00 — end is exclusive
        Assert.False(d.IsActiveAt(Wed1000Utc.AddDays(3)));                 // Saturday
        Assert.Equal(Wed1000Utc.AddHours(3), d.NextChangeAfter(Wed1000Utc)); // switches off at 13:00
        Assert.Equal(Wed1000Utc.AddDays(1).AddHours(-1), d.NextChangeAfter(Wed1000Utc.AddHours(4))); // back on Thu 09:00
    }

    [Fact]
    public void Schedule_WindowPastMidnight()
    {
        var d = Weekly(new DedicationWindow([3], "22:00", "02:00")); // Wednesday night
        Assert.False(d.IsActiveAt(Wed1000Utc));
        Assert.True(d.IsActiveAt(Wed1000Utc.AddHours(13)));   // Wed 23:00
        Assert.True(d.IsActiveAt(Wed1000Utc.AddHours(15)));   // Thu 01:00 — still Wednesday's window
        Assert.False(d.IsActiveAt(Wed1000Utc.AddHours(16)));  // Thu 02:00
    }

    [Fact]
    public void Create_RejectsBadInput()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentException>(() => AgentDedication.Create(Guid.NewGuid(), Guid.NewGuid(), [], DedicationMode.Duration, now, now.AddHours(1), null, "UTC", null, Guid.NewGuid(), ""));
        Assert.Throws<ArgumentException>(() => AgentDedication.Create(Guid.NewGuid(), Guid.NewGuid(), [Guid.NewGuid()], DedicationMode.Until, now, now.AddHours(-1), null, "UTC", null, Guid.NewGuid(), ""));
        Assert.Throws<ArgumentException>(() => AgentDedication.Create(Guid.NewGuid(), Guid.NewGuid(), [Guid.NewGuid()], DedicationMode.Schedule, now, null, [], "UTC", null, Guid.NewGuid(), ""));
        Assert.Throws<ArgumentException>(() => AgentDedication.Create(Guid.NewGuid(), Guid.NewGuid(), [Guid.NewGuid()], DedicationMode.Schedule, now, null,
            [new DedicationWindow([1], "09:00", "09:00")], "UTC", null, Guid.NewGuid(), ""));
    }
}
