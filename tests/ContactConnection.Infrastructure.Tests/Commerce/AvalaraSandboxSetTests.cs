using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Commerce;
using ContactConnection.Infrastructure.Credentials;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Commerce;

/// <summary>
/// Launch modes (S179): the sandbox credential set only ever reaches Avalara's sandbox, even if a sandbox key says
/// Environment = production. The production set still follows its Environment value.
/// </summary>
public class AvalaraSandboxSetTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Uri? Url;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Url = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""{"authenticated":true}"""),
            });
        }
    }

    private static (AvalaraTaxProvider Provider, CapturingHandler Handler) Build()
    {
        var store = new Mock<ITenantCredentialStore>();
        foreach (var vendor in new[] { "Avalara", "Avalara.sandbox" })
        {
            store.Setup(s => s.GetAsync($"{vendor}:AccountId", It.IsAny<CancellationToken>())).ReturnsAsync("123");
            store.Setup(s => s.GetAsync($"{vendor}:LicenseKey", It.IsAny<CancellationToken>())).ReturnsAsync("key");
            store.Setup(s => s.GetAsync($"{vendor}:Environment", It.IsAny<CancellationToken>())).ReturnsAsync("production");
        }
        var handler = new CapturingHandler();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, disposeHandler: false));
        return (new AvalaraTaxProvider(factory.Object, store.Object, NullLogger<AvalaraTaxProvider>.Instance), handler);
    }

    [Fact]
    public async Task SandboxSet_SaysProduction_StillPingsTheSandbox()
    {
        var (provider, handler) = Build();
        using (CredentialSetScope.Use("sandbox"))
            await provider.TestCredentialsAsync(Guid.Empty, Guid.Empty);
        Assert.Equal(AvalaraTaxProvider.SandboxPingUrl, handler.Url!.ToString());
    }

    [Fact]
    public async Task ProductionSet_FollowsItsEnvironment()
    {
        var (provider, handler) = Build();
        await provider.TestCredentialsAsync(Guid.Empty, Guid.Empty);
        Assert.Equal(AvalaraTaxProvider.ProductionPingUrl, handler.Url!.ToString());
    }
}
