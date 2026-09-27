using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Commerce;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Payments;

public class OrderNumberServiceTests
{
    private static void SetOrderNumber(CallRecord record, string value)
        => typeof(CallRecord).GetProperty(nameof(CallRecord.OrderNumber))!.SetValue(record, value);

    [Fact]
    public async Task ExistingOrderNumber_IsReturned_WithoutAllocating()
    {
        var record = CallRecord.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        SetOrderNumber(record, "LIFSEA-10000005");
        var repo = new Mock<IOrderNumberSequenceRepository>(MockBehavior.Strict);

        var result = await new OrderNumberService(repo.Object).GetOrAssignAsync(record);

        Assert.Equal("LIFSEA-10000005", result);
    }

    [Fact]
    public async Task NoClient_ReturnsNull_WithoutAllocating()
    {
        var record = CallRecord.Create(Guid.NewGuid(), Guid.Empty, Guid.NewGuid());
        var repo = new Mock<IOrderNumberSequenceRepository>(MockBehavior.Strict);

        Assert.Null(await new OrderNumberService(repo.Object).GetOrAssignAsync(record));
    }

    [Fact]
    public async Task ClientWithoutSequence_ReturnsNull_AndAssignsNothing()
    {
        var record = CallRecord.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var repo = new Mock<IOrderNumberSequenceRepository>();
        repo.Setup(r => r.AllocateAsync(record.ClientId, It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        Assert.Null(await new OrderNumberService(repo.Object).GetOrAssignAsync(record));
        repo.Verify(r => r.AssignToCallRecordAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Allocates_ThenReturnsWhateverTheConditionalAssignLeftOnTheCall()
    {
        var record = CallRecord.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var repo = new Mock<IOrderNumberSequenceRepository>();
        repo.Setup(r => r.AllocateAsync(record.ClientId, It.IsAny<CancellationToken>())).ReturnsAsync("LIFSEA-10000007");
        // Simulates losing a race: a concurrent first-use already stamped 10000006 on this call.
        repo.Setup(r => r.AssignToCallRecordAsync(record.Id, "LIFSEA-10000007", It.IsAny<CancellationToken>()))
            .ReturnsAsync("LIFSEA-10000006");

        var result = await new OrderNumberService(repo.Object).GetOrAssignAsync(record);

        Assert.Equal("LIFSEA-10000006", result);
    }
}
