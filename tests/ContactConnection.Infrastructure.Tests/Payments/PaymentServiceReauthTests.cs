using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Payments;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Payments;

/// <summary>authorize_payment reached more than once (S164): the agent went back and changed the
/// order after a successful auth. No live auth → authorize; same amount + same card capture → no-op;
/// different amount (or a newly captured card) → void the old auth, then authorize. The card is kept
/// after an auth so a re-auth needs no re-keying.</summary>
public class PaymentServiceReauthTests
{
    private const string CardJson = """{"pan":"4111111111111111","expiry":"1227","cvv":"123"}""";

    private sealed class Harness
    {
        public required PaymentService Service;
        public required Mock<IPaymentGatewayClient> Gateway;
        public required Mock<IPaymentTransactionRepository> Transactions;
        public required CallRecord Record;
        public PaymentTransaction? Existing;
    }

    private static Harness Build(bool voidSucceeds = true)
    {
        var record = CallRecord.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        record.StoreSensitiveData("ciphertext");
        var callRecords = new Mock<ICallRecordRepository>();
        callRecords.Setup(r => r.GetByIdWithInteractionsAsync(record.Id, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        var protector = new Mock<ISensitiveDataProtector>();
        protector.Setup(p => p.Unprotect(It.IsAny<string>())).Returns(CardJson);

        var gateway = new Mock<IPaymentGatewayClient>();
        gateway.Setup(g => g.AuthorizeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GatewayAuthResult(true, PaymentTransactionStatus.Approved, "60200", "NEW123", "1",
                "This transaction has been approved.", "Y", "M", "1111", "Visa"));
        gateway.Setup(g => g.VoidAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GatewayVoidResult(voidSucceeds, voidSucceeds ? "Voided" : "Transaction cannot be voided"));
        var factory = new Mock<IPaymentGatewayClientFactory>();
        factory.Setup(f => f.Resolve("authorize_net")).Returns(gateway.Object);

        var h = new Harness
        {
            Service = null!, Gateway = gateway, Transactions = new Mock<IPaymentTransactionRepository>(), Record = record,
        };
        h.Transactions.Setup(t => t.GetMostRecentApprovedAsync(record.Id, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => h.Existing is { VoidedAt: null } ? h.Existing : null);
        var orderNumbers = new Mock<IOrderNumberService>();
        orderNumbers.Setup(o => o.GetOrAssignAsync(record, It.IsAny<CallInteraction?>(), It.IsAny<CancellationToken>())).ReturnsAsync("LIFSEA-10000001");
        h.Service = new PaymentService(callRecords.Object, protector.Object, factory.Object, h.Transactions.Object, orderNumbers.Object);
        return h;
    }

    private static PaymentTransaction Approved(CallRecord r, decimal amount) => PaymentTransaction.Create(
        Guid.NewGuid(), r.TenantId, r.Id, r.ClientId, r.CampaignId, "authorize_net", amount, PaymentTransactionStatus.Approved,
        "60100", "OLD123", "1", "This transaction has been approved.", "Y", "M", "1111", "Visa", "LIFSEA-10000001");

    private static Task<PaymentAuthResult> Auth(Harness h, decimal amount) =>
        h.Service.AuthorizeAsync(h.Record.Id, "authorize_net", "pan", "expiry", "cvv", null, "84037", amount);

    private static void VerifyAuthorized(Harness h, Times times) =>
        h.Gateway.Verify(g => g.AuthorizeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), times);

    [Fact]
    public async Task NoPriorAuth_Authorizes_AndKeepsTheCard()
    {
        var h = Build();
        var result = await Auth(h, 144.85m);

        Assert.Equal((true, PaymentAuthAction.Authorized, 144.85m, "1111"), (result.Succeeded, result.Action, result.Amount, result.CardLast4));
        VerifyAuthorized(h, Times.Once());
        Assert.NotNull(h.Record.SensitiveData);   // kept for a possible re-auth
    }

    [Fact]
    public async Task SameAmount_SameCard_IsANoOp()
    {
        var h = Build();
        await Task.Delay(15);
        h.Existing = Approved(h.Record, 144.85m);   // authorized after the card was captured

        var result = await Auth(h, 144.85m);

        Assert.Equal((true, PaymentTransactionStatus.Approved, PaymentAuthAction.AlreadyAuthorized), (result.Succeeded, result.Status, result.Action));
        Assert.Equal("60100", result.GatewayTransactionId);
        VerifyAuthorized(h, Times.Never());
        h.Gateway.Verify(g => g.VoidAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DifferentAmount_VoidsThePreviousAuth_ThenAuthorizes()
    {
        var h = Build();
        await Task.Delay(15);
        h.Existing = Approved(h.Record, 144.85m);

        var result = await Auth(h, 194.75m);

        h.Gateway.Verify(g => g.VoidAsync(h.Record.CampaignId, h.Record.ClientId, "60100", It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(h.Existing!.VoidedAt);
        VerifyAuthorized(h, Times.Once());
        Assert.Equal((true, PaymentAuthAction.Reauthorized, 194.75m, "60200"), (result.Succeeded, result.Action, result.Amount, result.GatewayTransactionId));
    }

    [Fact]
    public async Task SameAmount_ButCardRecaptured_Reauthorizes()
    {
        var h = Build();
        h.Existing = Approved(h.Record, 144.85m);
        await Task.Delay(15);
        h.Record.StoreSensitiveData("new-card-ciphertext");   // "Change Payment Info" → caller keyed a new card

        var result = await Auth(h, 144.85m);

        Assert.Equal(PaymentAuthAction.Reauthorized, result.Action);
        h.Gateway.Verify(g => g.VoidAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), "60100", It.IsAny<CancellationToken>()), Times.Once);
        VerifyAuthorized(h, Times.Once());
    }

    [Fact]
    public async Task VoidFails_DoesNotAuthorizeAgain()
    {
        var h = Build(voidSucceeds: false);
        await Task.Delay(15);
        h.Existing = Approved(h.Record, 144.85m);

        var result = await Auth(h, 194.75m);

        Assert.False(result.Succeeded);
        Assert.Equal(PaymentTransactionStatus.Error, result.Status);
        Assert.Contains("not re-authorizing", result.ResponseReasonText);
        Assert.Null(h.Existing!.VoidedAt);
        VerifyAuthorized(h, Times.Never());
    }
}
