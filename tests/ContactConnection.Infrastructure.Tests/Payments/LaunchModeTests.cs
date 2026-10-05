using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Credentials;
using ContactConnection.Infrastructure.Payments;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Payments;

/// <summary>
/// Script launch modes (S179): a training / sandbox run uses the campaign's sandbox credentials and is simulated when it
/// has none — it must never reach a production gateway account. A production call is never simulated.
/// </summary>
public class LaunchModeTests
{
    private const string CardJson = """{"pan":"4111111111111111","expiry":"1227","cvv":"123"}""";

    private static (PaymentService Service, Mock<IPaymentGatewayClient> Gateway, CallRecord Record, List<bool> SandboxSeen) Build(
        CallRecord record, bool gatewayConfigured)
    {
        record.StoreSensitiveData("ciphertext");
        var callRecords = new Mock<ICallRecordRepository>();
        callRecords.Setup(r => r.GetByIdWithInteractionsAsync(record.Id, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        var protector = new Mock<ISensitiveDataProtector>();
        protector.Setup(p => p.Unprotect(It.IsAny<string>())).Returns(CardJson);

        var sandboxSeen = new List<bool>();
        var gateway = new Mock<IPaymentGatewayClient>();
        gateway.Setup(g => g.IsConfiguredAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(gatewayConfigured);
        gateway.Setup(g => g.AuthorizeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback(() => sandboxSeen.Add(CredentialSetScope.IsSandbox))
            .ReturnsAsync(new GatewayAuthResult(true, PaymentTransactionStatus.Approved, "60200", "REAL01", "1",
                "This transaction has been approved.", "Y", "M", "1111", "Visa"));
        var factory = new Mock<IPaymentGatewayClientFactory>();
        factory.Setup(f => f.Resolve("authorize_net")).Returns(gateway.Object);

        var service = new PaymentService(callRecords.Object, protector.Object, factory.Object,
            new Mock<IPaymentTransactionRepository>().Object, new Mock<IOrderNumberService>().Object);
        return (service, gateway, record, sandboxSeen);
    }

    private static Task<PaymentAuthResult> Auth(PaymentService s, CallRecord r) =>
        s.AuthorizeAsync(r.Id, "authorize_net", "pan", "expiry", "cvv", null, "84037", 49.95m);

    [Fact]
    public async Task Training_WithoutSandboxCredentials_IsSimulated_NoGatewayCall()
    {
        var (service, gateway, record, _) = Build(CallRecord.CreateManual(Guid.NewGuid(), Guid.NewGuid(), CallRunMode.Training), false);

        var result = await Auth(service, record);

        Assert.True(result.Succeeded);
        Assert.Equal("TRAINING", result.AuthCode);
        Assert.StartsWith(PaymentService.SimulatedPrefix, result.GatewayTransactionId);
        gateway.Verify(g => g.AuthorizeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Training_WithSandboxCredentials_CallsTheGateway_UnderTheSandboxSet()
    {
        var (service, _, record, sandboxSeen) = Build(CallRecord.CreateManual(Guid.NewGuid(), Guid.NewGuid(), CallRunMode.Training), true);

        await Auth(service, record);

        Assert.Equal([true], sandboxSeen);
        Assert.False(CredentialSetScope.IsSandbox);   // the scope ends with the call
    }

    [Fact]
    public async Task Production_IsNeverSimulated_AndUsesProductionCredentials()
    {
        var (service, _, record, sandboxSeen) = Build(CallRecord.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), false);

        var result = await Auth(service, record);

        Assert.Equal("REAL01", result.AuthCode);
        Assert.Equal([false], sandboxSeen);
    }

    [Fact]
    public async Task SandboxScope_ResolvesTheSandboxKeys_AndNeverTheProductionOnes()
    {
        var store = new Mock<ITenantCredentialStore>();
        store.Setup(s => s.GetAsync("AuthorizeNet:ApiLoginId", It.IsAny<CancellationToken>())).ReturnsAsync("PROD-LOGIN");
        store.Setup(s => s.GetAsync("AuthorizeNet.sandbox:ApiLoginId", It.IsAny<CancellationToken>())).ReturnsAsync("SANDBOX-LOGIN");

        Assert.Equal("PROD-LOGIN", await ScopedCredentials.ResolveAsync(store.Object, "AuthorizeNet", "ApiLoginId", Guid.Empty, Guid.Empty));
        using (CredentialSetScope.Use(CallCredentialSet.Sandbox))
            Assert.Equal("SANDBOX-LOGIN", await ScopedCredentials.ResolveAsync(store.Object, "AuthorizeNet", "ApiLoginId", Guid.Empty, Guid.Empty));

        store.Setup(s => s.GetAsync("AuthorizeNet.sandbox:ApiLoginId", It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        using (CredentialSetScope.Use(CallCredentialSet.Sandbox))
            Assert.Null(await ScopedCredentials.ResolveAsync(store.Object, "AuthorizeNet", "ApiLoginId", Guid.Empty, Guid.Empty));
    }

    [Fact]
    public void Training_AlwaysUsesTheSandboxSet_EvenIfProductionIsRequested()
    {
        var r = CallRecord.CreateManual(Guid.NewGuid(), Guid.NewGuid(), CallRunMode.Training, CallCredentialSet.Production);
        Assert.Equal((CallRunMode.Training, CallCredentialSet.Sandbox), (r.RunMode, r.CredentialSet));
        Assert.False(r.IsProductionRun);
    }
}
