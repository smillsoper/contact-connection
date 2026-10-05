using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Commerce;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Payments;

public class OrderNumberServiceTests
{
    private static (CallRecord Record, CallInteraction Ix) Call(Guid? clientId = null)
    {
        var record = CallRecord.Create(Guid.NewGuid(), clientId ?? Guid.NewGuid(), Guid.NewGuid());
        return (record, record.AddInteraction(InteractionType.OrderSale));
    }

    [Fact]
    public async Task ExistingOrderNumber_IsReturned_WithoutAllocating()
    {
        var (record, ix) = Call();
        ix.SetOrderNumber("LIFSEA-10000005");
        var repo = new Mock<IOrderNumberSequenceRepository>(MockBehavior.Strict);

        var result = await new OrderNumberService(repo.Object).GetOrAssignAsync(record, ix);

        Assert.Equal("LIFSEA-10000005", result);
    }

    [Fact]
    public async Task NoClient_ReturnsNull_WithoutAllocating()
    {
        var (record, ix) = Call(Guid.Empty);
        var repo = new Mock<IOrderNumberSequenceRepository>(MockBehavior.Strict);

        Assert.Null(await new OrderNumberService(repo.Object).GetOrAssignAsync(record, ix));
    }

    [Fact]
    public async Task ClientWithoutSequence_ReturnsNull_AndAssignsNothing()
    {
        var (record, ix) = Call();
        var repo = new Mock<IOrderNumberSequenceRepository>();
        repo.Setup(r => r.AllocateAsync(record.ClientId, It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        Assert.Null(await new OrderNumberService(repo.Object).GetOrAssignAsync(record, ix));
        repo.Verify(r => r.AssignToInteractionAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Allocates_ThenReturnsWhateverTheConditionalAssignLeftOnTheCall()
    {
        var (record, ix) = Call();
        var repo = new Mock<IOrderNumberSequenceRepository>();
        repo.Setup(r => r.AllocateAsync(record.ClientId, It.IsAny<CancellationToken>())).ReturnsAsync("LIFSEA-10000007");
        // Simulates losing a race: a concurrent first-use already stamped 10000006 on this call.
        repo.Setup(r => r.AssignToInteractionAsync(ix.Id, "LIFSEA-10000007", It.IsAny<CancellationToken>()))
            .ReturnsAsync("LIFSEA-10000006");

        var result = await new OrderNumberService(repo.Object).GetOrAssignAsync(record, ix);

        Assert.Equal("LIFSEA-10000006", result);
    }
}
