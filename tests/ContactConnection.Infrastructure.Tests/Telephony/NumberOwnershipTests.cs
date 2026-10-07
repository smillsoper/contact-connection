using Xunit;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Telephony;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Tests.Telephony;

/// <summary>S182: Reserve numbers, release, and who may hold a number in the platform routing table.</summary>
public class NumberOwnershipTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    private static readonly Guid Campaign = Guid.NewGuid();

    private static ContactConnectionDbContext Db() => new(new DbContextOptionsBuilder<ContactConnectionDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    // ── PhoneNumber states ──────────────────────────────────────────────────

    [Theory]
    [InlineData("(503) 555-1234", "+15035551234")]
    [InlineData("15035551234", "+15035551234")]
    [InlineData("+1 503 555 1234", "+15035551234")]
    [InlineData("+442071234567", "+442071234567")]
    public void Normalize_gives_E164(string typed, string expected) =>
        Assert.Equal(expected, PhoneNumber.Normalize(typed));

    [Fact]
    public void Forms_cover_carrier_variants()
    {
        var forms = PhoneNumber.Forms("+15035551234");
        Assert.Contains("5035551234", forms);
        Assert.Contains("15035551234", forms);
        Assert.Contains("+15035551234", forms);
    }

    [Fact]
    public void Created_without_campaign_is_in_reserve_and_takes_no_calls()
    {
        var pn = PhoneNumber.Create(TenantA, null, "5035551234");
        Assert.True(pn.InReserve);
        Assert.NotNull(pn.ReservedAt);
        Assert.False(pn.TakesCalls);
        Assert.False(pn.IsReleased);
    }

    [Fact]
    public void Inactive_on_a_campaign_is_held_not_released()
    {
        var pn = PhoneNumber.Create(TenantA, Campaign, "5035551234");
        pn.Deactivate();
        Assert.False(pn.TakesCalls);
        Assert.False(pn.IsReleased);
    }

    [Fact]
    public void Deactivated_in_reserve_is_released()
    {
        var pn = PhoneNumber.Create(TenantA, null, "5035551234");
        pn.Deactivate();
        Assert.True(pn.IsReleased);
    }

    [Fact]
    public void Move_to_reserve_keeps_it_owned_and_drops_flow_overrides()
    {
        var pn = PhoneNumber.Create(TenantA, Campaign, "5035551234");
        pn.AssignTelephonyFlow(Guid.NewGuid());
        pn.Deactivate();
        pn.MoveToReserve();
        Assert.True(pn.InReserve);
        Assert.True(pn.IsActive);
        Assert.False(pn.IsReleased);
        Assert.Null(pn.TelephonyFlowId);
        Assert.NotNull(pn.ReservedAt);
    }

    [Fact]
    public void Reassign_to_another_campaign_clears_reserve_clock_and_flow_overrides()
    {
        var pn = PhoneNumber.Create(TenantA, null, "5035551234");
        pn.AssignFlow(Guid.NewGuid());
        pn.Reassign(Campaign);
        Assert.Equal(Campaign, pn.CampaignId);
        Assert.Null(pn.ReservedAt);
        Assert.Null(pn.FlowId);
        Assert.True(pn.TakesCalls);
    }

    // ── Ownership ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Unknown_number_is_free_and_gets_a_routing_row()
    {
        using var db = Db();
        var own = new NumberOwnership(db);
        var pn = PhoneNumber.Create(TenantA, Campaign, "5035551234");
        Assert.Null(await own.ConflictAsync(TenantA, pn.Number, false));
        await own.ApplyAsync(pn);
        await own.SaveAsync();
        var row = await db.PhoneNumberRoutings.SingleAsync();
        Assert.Equal(TenantA, row.TenantId);
        Assert.True(row.TakesCalls);
    }

    [Fact]
    public async Task Another_tenants_live_number_cannot_be_taken()
    {
        using var db = Db();
        db.PhoneNumberRoutings.Add(PhoneNumberRouting.Create("+15035551234", TenantB, Campaign));
        await db.SaveChangesAsync();
        var own = new NumberOwnership(db);

        Assert.NotNull(await own.ConflictAsync(TenantA, "503-555-1234", false));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            own.ApplyAsync(PhoneNumber.Create(TenantA, Campaign, "5035551234")));
    }

    [Fact]
    public async Task Another_tenants_reserve_number_is_still_theirs()
    {
        using var db = Db();
        db.PhoneNumberRoutings.Add(PhoneNumberRouting.Create("+15035551234", TenantB, null));
        await db.SaveChangesAsync();
        Assert.NotNull(await new NumberOwnership(db).ConflictAsync(TenantA, "+15035551234", false));
    }

    [Fact]
    public async Task Released_number_transfers_to_the_new_tenant()
    {
        using var db = Db();
        var released = PhoneNumberRouting.Create("+15035551234", TenantB, null);
        released.Deactivate();
        db.PhoneNumberRoutings.Add(released);
        await db.SaveChangesAsync();
        var own = new NumberOwnership(db);

        Assert.Null(await own.ConflictAsync(TenantA, "+15035551234", false));
        await own.ApplyAsync(PhoneNumber.Create(TenantA, Campaign, "+15035551234"));
        await own.SaveAsync();
        var row = await db.PhoneNumberRoutings.SingleAsync();
        Assert.Equal(TenantA, row.TenantId);
        Assert.True(row.TakesCalls);
    }

    [Fact]
    public async Task Releasing_never_touches_a_row_someone_else_now_owns()
    {
        using var db = Db();
        db.PhoneNumberRoutings.Add(PhoneNumberRouting.Create("+15035551234", TenantB, Campaign));
        await db.SaveChangesAsync();
        var own = new NumberOwnership(db);

        var oldCopy = PhoneNumber.Create(TenantA, null, "+15035551234");
        oldCopy.Deactivate();   // released in tenant A's history
        await own.ApplyAsync(oldCopy);
        await own.SaveAsync();
        var row = await db.PhoneNumberRoutings.SingleAsync();
        Assert.Equal(TenantB, row.TenantId);
        Assert.True(row.TakesCalls);
    }

    [Fact]
    public async Task Own_number_state_mirrors_into_routing()
    {
        using var db = Db();
        var own = new NumberOwnership(db);
        var pn = PhoneNumber.Create(TenantA, Campaign, "5035551234");
        await own.ApplyAsync(pn);
        await own.SaveAsync();

        pn.MoveToReserve();
        await own.ApplyAsync(pn);
        await own.SaveAsync();
        var row = await db.PhoneNumberRoutings.SingleAsync();
        Assert.Null(row.CampaignId);
        Assert.False(row.TakesCalls);
        Assert.False(row.IsReleased);

        pn.Deactivate();
        await own.ApplyAsync(pn);
        await own.SaveAsync();
        Assert.True((await db.PhoneNumberRoutings.SingleAsync()).IsReleased);
    }
}
