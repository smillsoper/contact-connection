using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Payments;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Payments;

public class PaymentServiceOrderNumberTests
{
    private const string CardJson = """{"card_number":"4111111111111111","exp":"1227","cvv":"123"}""";

    private static (PaymentService Service, Mock<IPaymentGatewayClient> Gateway, Mock<IPaymentTransactionRepository> Transactions)
        NewService(CallRecord record, string? orderNumber)
    {
        var callRecords = new Mock<ICallRecordRepository>();
        callRecords.Setup(r => r.GetByIdWithInteractionsAsync(record.Id, It.IsAny<CancellationToken>())).ReturnsAsync(record);

        var protector = new Mock<ISensitiveDataProtector>();
        protector.Setup(p => p.Unprotect(It.IsAny<string>())).Returns(CardJson);

        var gateway = new Mock<IPaymentGatewayClient>();
        gateway.Setup(g => g.AuthorizeAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GatewayAuthResult(true, PaymentTransactionStatus.Approved, "60123", "ABC123", "1",
                "This transaction has been approved.", "Y", "M", "1111", "Visa"));
        var factory = new Mock<IPaymentGatewayClientFactory>();
        factory.Setup(f => f.Resolve("authorize_net")).Returns(gateway.Object);

        var transactions = new Mock<IPaymentTransactionRepository>();
        var orderNumbers = new Mock<IOrderNumberService>();
        orderNumbers.Setup(o => o.GetOrAssignAsync(record, It.IsAny<CallInteraction?>(), It.IsAny<CancellationToken>())).ReturnsAsync(orderNumber);

        var service = new PaymentService(callRecords.Object, protector.Object, factory.Object, transactions.Object, orderNumbers.Object);
        return (service, gateway, transactions);
    }

    private static CallRecord RecordWithCard()
    {
        var record = CallRecord.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        record.StoreSensitiveData("ciphertext");
        return record;
    }

    [Fact]
    public async Task Authorize_SendsOrderNumberToGateway_RecordsIt_AndReturnsIt()
    {
        var record = RecordWithCard();
        var (service, gateway, transactions) = NewService(record, "LIFSEA-10000123");

        var result = await service.AuthorizeAsync(record.Id, "authorize_net", "card_number", "exp", "cvv", null, "97470", 53.07m);

        Assert.Equal("LIFSEA-10000123", result.OrderNumber);
        gateway.Verify(g => g.AuthorizeAsync(record.CampaignId, record.ClientId, 53.07m, "4111111111111111", "1227", "123",
            "97470", "LIFSEA-10000123", It.IsAny<CancellationToken>()));
        transactions.Verify(t => t.AddAsync(
            It.Is<PaymentTransaction>(pt => pt.OrderNumber == "LIFSEA-10000123"), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task Authorize_WithoutSequence_SendsNoOrderNumber()
    {
        var record = RecordWithCard();
        var (service, gateway, _) = NewService(record, orderNumber: null);

        var result = await service.AuthorizeAsync(record.Id, "authorize_net", "card_number", "exp", "cvv", null, null, 10m);

        Assert.Null(result.OrderNumber);
        gateway.Verify(g => g.AuthorizeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), 10m, It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), null, null, It.IsAny<CancellationToken>()));
    }
}
