using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Telephony.NodeHandlers;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony.NodeHandlers;

public class CheckBlockListNodeHandlerTests
{
    private static TenantDbContext NewDb() =>
        new(new DbContextOptionsBuilder<TenantDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static TelephonyFlowContext Ctx(Guid tenantId, string callerNumber = "+15551234567") => new()
    {
        ChannelUuid = "uuid-1", CallerNumber = callerNumber, DestinationNumber = "+15557654321",
        TenantId = tenantId, CampaignId = Guid.NewGuid(), CallRecordId = Guid.NewGuid(),
        TenantSubdomain = "test-tenant", TenantSchemaName = "tenant_test_tenant", TenantTimezone = "America/Chicago",
    };

    private static JsonObject Node(string? checkVariable = null) => new()
    {
        ["type"] = "tf_check_block_list",
        ["checkVariable"] = checkVariable,
        ["transitions"] = new JsonObject { ["blocked"] = "n_blocked", ["not_blocked"] = "n_clear" },
    };

    private static CheckBlockListNodeHandler NewHandler(TenantDbContext db)
    {
        var factory = new Mock<ITenantDbContextFactory>();
        factory.Setup(f => f.Create(It.IsAny<string>())).Returns(db);
        return new CheckBlockListNodeHandler(factory.Object);
    }

    [Fact]
    public async Task ExactMatch_OnCallerAni_TakesBlocked()
    {
        await using var db = NewDb();
        var tenantId = Guid.NewGuid();
        db.BlockListEntries.Add(BlockListEntry.Create(tenantId, "5551234567", BlockListMatchType.Exact, "spam", null));
        await db.SaveChangesAsync();

        var result = await NewHandler(db).ExecuteAsync(Node(), Ctx(tenantId));

        Assert.Equal("blocked", result.TransitionTaken);
        Assert.Equal("n_blocked", result.NextNodeId);
    }

    [Fact]
    public async Task NoMatchingEntry_TakesNotBlocked()
    {
        await using var db = NewDb();
        var tenantId = Guid.NewGuid();
        db.BlockListEntries.Add(BlockListEntry.Create(tenantId, "5559999999", BlockListMatchType.Exact, null, null));
        await db.SaveChangesAsync();

        var result = await NewHandler(db).ExecuteAsync(Node(), Ctx(tenantId));

        Assert.Equal("not_blocked", result.TransitionTaken);
    }

    [Fact]
    public async Task PrefixMatch_BlocksAnyNumberStartingWithPrefix()
    {
        await using var db = NewDb();
        var tenantId = Guid.NewGuid();
        db.BlockListEntries.Add(BlockListEntry.Create(tenantId, "555999", BlockListMatchType.Prefix, "known robocaller area", null));
        await db.SaveChangesAsync();

        var result = await NewHandler(db).ExecuteAsync(Node(), Ctx(tenantId, "+15559998888"));

        Assert.Equal("blocked", result.TransitionTaken);
    }

    [Fact]
    public async Task NormalizesLeadingCountryCode_SoStoredAndDialedFormsMatch()
    {
        await using var db = NewDb();
        var tenantId = Guid.NewGuid();
        // Stored bare (no country code); caller number arrives with "+1" — both should normalize
        // to the same 10 digits.
        db.BlockListEntries.Add(BlockListEntry.Create(tenantId, "5551234567", BlockListMatchType.Exact, null, null));
        await db.SaveChangesAsync();

        var result = await NewHandler(db).ExecuteAsync(Node(), Ctx(tenantId, "+15551234567"));

        Assert.Equal("blocked", result.TransitionTaken);
    }

    [Fact]
    public async Task ExpiredEntry_IsIgnored_TakesNotBlocked()
    {
        await using var db = NewDb();
        var tenantId = Guid.NewGuid();
        db.BlockListEntries.Add(BlockListEntry.Create(
            tenantId, "5551234567", BlockListMatchType.Exact, null, DateTimeOffset.UtcNow.AddDays(-1)));
        await db.SaveChangesAsync();

        var result = await NewHandler(db).ExecuteAsync(Node(), Ctx(tenantId));

        Assert.Equal("not_blocked", result.TransitionTaken);
    }

    [Fact]
    public async Task DifferentTenant_EntryDoesNotApply()
    {
        await using var db = NewDb();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        db.BlockListEntries.Add(BlockListEntry.Create(otherTenantId, "5551234567", BlockListMatchType.Exact, null, null));
        await db.SaveChangesAsync();

        var result = await NewHandler(db).ExecuteAsync(Node(), Ctx(tenantId));

        Assert.Equal("not_blocked", result.TransitionTaken);
    }

    [Fact]
    public async Task CheckVariable_ChecksNamedVariable_NotCallerAni()
    {
        await using var db = NewDb();
        var tenantId = Guid.NewGuid();
        db.BlockListEntries.Add(BlockListEntry.Create(tenantId, "5559990000", BlockListMatchType.Exact, null, null));
        await db.SaveChangesAsync();

        var ctx = Ctx(tenantId, "+15551234567"); // caller ANI itself is clean
        ctx.Vars["transfer_target"] = "+15559990000"; // but the number about to be dialed is blocked

        var result = await NewHandler(db).ExecuteAsync(Node("transfer_target"), ctx);

        Assert.Equal("blocked", result.TransitionTaken);
    }
}
