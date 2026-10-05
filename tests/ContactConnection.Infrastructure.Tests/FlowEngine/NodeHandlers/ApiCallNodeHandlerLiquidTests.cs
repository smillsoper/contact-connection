using System.Text.Json;
using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.ApiExecution;
using ContactConnection.Infrastructure.FlowEngine;
using ContactConnection.Infrastructure.FlowEngine.NodeHandlers;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.FlowEngine.NodeHandlers;

/// <summary>api_call with a Liquid body, body-level success criteria, and oncePerCall.</summary>
public class ApiCallNodeHandlerLiquidTests
{
    private sealed class Harness
    {
        public readonly TenantApiDefinition Definition = TenantApiDefinition.Create(ApiCategory.General, "Life Seasons", "POST", "https://vendor.example.com");
        public readonly TenantApiEndpoint Endpoint;
        public readonly Mock<IApiDefinitionExecutor> Executor = new();
        public readonly Mock<IApiResponseCacheStore> Cache = new();
        public readonly Mock<ICardDataRetentionService> CardRetention = new();
        public readonly Mock<ITenantCredentialStore> Credentials = new();
        public readonly FlowExecutionContext Ctx = new()
        {
            SessionId = Guid.NewGuid(), FlowId = Guid.NewGuid(), FlowVersion = 1, CallRecordId = Guid.NewGuid(),
            InteractionId = Guid.NewGuid(), AgentId = Guid.NewGuid(), TenantId = Guid.NewGuid(), CurrentNodeId = "n_api",
        };
        public ApiDefinitionExecutionRequest? Sent;

        public Harness(string bodyTemplate, string successCriteria = "{}")
        {
            Definition.Activate();
            Endpoint = TenantApiEndpoint.Create(Definition.Id, ApiCategory.General, "", "Add order", "/api/v1/addorder", "POST");
            Endpoint.SetRequestBodyTemplate(bodyTemplate);
            Endpoint.SetBodyTemplateType(BodyTemplateType.Liquid);
            Endpoint.SetSuccessCriteria(successCriteria);
            Executor.Setup(e => e.ExecuteAsync(It.IsAny<ApiDefinitionExecutionRequest>(), It.IsAny<CancellationToken>()))
                .Callback<ApiDefinitionExecutionRequest, CancellationToken>((r, _) => Sent = r)
                .ReturnsAsync(new ApiDefinitionExecutionResult(true, 200, "OK", new(), """{"success":true}""", false, null));
        }

        public ApiCallNodeHandler Handler()
        {
            var defs = new Mock<ITenantApiDefinitionRepository>();
            defs.Setup(d => d.GetByIdAsync(Definition.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Definition);
            var endpoints = new Mock<ITenantApiEndpointRepository>();
            endpoints.Setup(e => e.GetByIdAsync(Endpoint.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Endpoint);
            var model = new Mock<IApiTemplateModelBuilder>();
            model.Setup(m => m.BuildAsync(It.IsAny<FlowExecutionContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ApiTemplateModelBuilder.Sample());
            return new ApiCallNodeHandler(new VariableResolver(), defs.Object, Mock.Of<IPortalApiDefinitionRepository>(),
                endpoints.Object, Mock.Of<IPortalApiEndpointRepository>(), Credentials.Object,
                Mock.Of<IPortalCredentialStore>(), Executor.Object, new FluidLiquidTemplateRenderer(), model.Object, Cache.Object, CardRetention.Object);
        }

        public JsonObject Node(bool oncePerCall = false, bool releasesCardData = false) => new()
        {
            ["type"] = "api_call", ["apiEndpointId"] = Endpoint.Id.ToString(), ["apiDefinitionScope"] = "tenant",
            ["outputVariable"] = "order", ["oncePerCall"] = oncePerCall, ["releasesCardData"] = releasesCardData,
            ["transitions"] = new JsonObject { ["success"] = "n_ok", ["error"] = "n_err" },
        };
    }

    [Fact]
    public async Task LiquidBody_RenderedFromCallModel_AndSent()
    {
        var h = new Harness("""{"order": {{ call_record.order_number | json }}, "total": {{ cart.total | money }}}""");

        var result = await h.Handler().ExecuteAsync(h.Node(), h.Ctx, null, "");

        Assert.Equal("n_ok", result.NextNodeId);
        Assert.Equal("""{"order": "LIFSEA-10000123", "total": 58.83}""", h.Sent!.Body);
    }

    [Fact]
    public async Task LiquidBody_InvalidJson_FailsWithoutSending()
    {
        var h = new Harness("""{"order": {{ call_record.order_number }} }"""); // forgot | json → unquoted

        var result = await h.Handler().ExecuteAsync(h.Node(), h.Ctx, null, "");

        Assert.Equal("n_err", result.NextNodeId);
        h.Executor.Verify(e => e.ExecuteAsync(It.IsAny<ApiDefinitionExecutionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Contains("invalid JSON", h.Ctx.FlowVars["order.error"]);
    }

    [Fact]
    public async Task SuccessCriteria_Http200SuccessFalse_TakesErrorPath_WithVendorMessage()
    {
        var h = new Harness("{}", """{"rules":[{"path":"success","value":"true"}],"errorMessagePath":"message"}""");
        h.Executor.Setup(e => e.ExecuteAsync(It.IsAny<ApiDefinitionExecutionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiDefinitionExecutionResult(true, 200, "OK", new(), """{"success":false,"message":"Invalid vendor number"}""", false, null));

        var result = await h.Handler().ExecuteAsync(h.Node(), h.Ctx, null, "");

        Assert.Equal("n_err", result.NextNodeId);
        Assert.Equal("Invalid vendor number", h.Ctx.FlowVars["order.error"]);
    }

    [Fact]
    public async Task OncePerCall_StoresSuccess_AndReplaysWithoutCallingAgain()
    {
        var h = new Harness("{}");
        string? stored = null;
        h.Cache.Setup(c => c.SetAsync(h.Ctx.CallRecordId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, string, CancellationToken>((_, _, json, _) => stored = json);

        var first = await h.Handler().ExecuteAsync(h.Node(oncePerCall: true), h.Ctx, null, "");
        Assert.Equal("n_ok", first.NextNodeId);
        Assert.NotNull(stored);

        h.Cache.Setup(c => c.GetAsync(h.Ctx.CallRecordId, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(stored);
        h.Ctx.FlowVars.Clear();
        var second = await h.Handler().ExecuteAsync(h.Node(oncePerCall: true), h.Ctx, null, "");

        Assert.Equal("n_ok", second.NextNodeId);
        h.Executor.Verify(e => e.ExecuteAsync(It.IsAny<ApiDefinitionExecutionRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("true", h.Ctx.FlowVars["order.success"]);  // output variables restored from the replay
    }

    // S166 — the order submission releases the captured card (CardDataRetentionMode.UntilOrderSubmitted).
    [Fact]
    public async Task ReleasesCardData_OnSuccess_WipesTheCard()
    {
        var h = new Harness("{}");
        await h.Handler().ExecuteAsync(h.Node(releasesCardData: true), h.Ctx, null, "");
        h.CardRetention.Verify(c => c.ReleaseAfterOrderSubmittedAsync(h.Ctx.CallRecordId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReleasesCardData_NotOnFailure_NorWhenUnmarked()
    {
        var failing = new Harness("{}", """{"rules":[{"path":"success","value":"true"}]}""");
        failing.Executor.Setup(e => e.ExecuteAsync(It.IsAny<ApiDefinitionExecutionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiDefinitionExecutionResult(true, 200, "OK", new(), """{"success":false}""", false, null));
        await failing.Handler().ExecuteAsync(failing.Node(releasesCardData: true), failing.Ctx, null, "");

        var unmarked = new Harness("{}");
        await unmarked.Handler().ExecuteAsync(unmarked.Node(), unmarked.Ctx, null, "");

        failing.CardRetention.VerifyNoOtherCalls();
        unmarked.CardRetention.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OncePerCall_FailureIsNeverStored()
    {
        var h = new Harness("{}", """{"rules":[{"path":"success","value":"true"}]}""");
        h.Executor.Setup(e => e.ExecuteAsync(It.IsAny<ApiDefinitionExecutionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiDefinitionExecutionResult(true, 200, "OK", new(), """{"success":false}""", false, null));

        await h.Handler().ExecuteAsync(h.Node(oncePerCall: true), h.Ctx, null, "");

        h.Cache.Verify(c => c.SetAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WithoutOncePerCall_CacheIsNeverConsulted()
    {
        var h = new Harness("{}");
        h.Cache.Setup(c => c.GetAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new ApiDefinitionExecutionResult(true, 200, "OK", new(), "{}", false, null)));

        await h.Handler().ExecuteAsync(h.Node(oncePerCall: false), h.Ctx, null, "");

        h.Executor.Verify(e => e.ExecuteAsync(It.IsAny<ApiDefinitionExecutionRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // S179 launch modes — a practice run never calls the client's API on the sandbox credential set.
    private static void Practice(Harness h, string mode, string credentialSet)
    {
        h.Ctx.CallRecord["run_mode"] = mode;
        h.Ctx.CallRecord["credential_set"] = credentialSet;
    }

    [Fact]
    public async Task TrainingRun_ReturnsTheTrainingResponse_WithoutCallingTheApi()
    {
        var h = new Harness("{}", """{"rules":[{"path":"success","value":"true"}]}""");
        h.Endpoint.SetTrainingResponse("""{"success":true,"orderNumber":"TRN-1001"}""");
        Practice(h, "training", "sandbox");

        var result = await h.Handler().ExecuteAsync(h.Node(), h.Ctx, null, "");

        Assert.Equal("n_ok", result.NextNodeId);
        Assert.Equal("TRN-1001", h.Ctx.FlowVars["order.response.orderNumber"]);
        Assert.Equal(ApiCallNodeHandler.SimulatedHistoryNote, h.Ctx.ExecutionHistory[^1].InputValue);
        h.Executor.Verify(e => e.ExecuteAsync(It.IsAny<ApiDefinitionExecutionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TrainingRun_WithoutATrainingResponse_GetsTheGenericSuccess()
    {
        var h = new Harness("{}");
        Practice(h, "training", "sandbox");

        await h.Handler().ExecuteAsync(h.Node(), h.Ctx, null, "");

        Assert.Equal("true", h.Ctx.FlowVars["order.response.simulated"]);
        h.Executor.Verify(e => e.ExecuteAsync(It.IsAny<ApiDefinitionExecutionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DesignerSandbox_OnProductionCredentials_CallsTheRealApi()
    {
        var h = new Harness("{}");
        h.Endpoint.SetTrainingResponse("""{"success":true}""");
        Practice(h, "sandbox", "production");

        await h.Handler().ExecuteAsync(h.Node(), h.Ctx, null, "");

        h.Executor.Verify(e => e.ExecuteAsync(It.IsAny<ApiDefinitionExecutionRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Null(h.Ctx.ExecutionHistory[^1].InputValue);
    }

    [Fact]
    public async Task ProductionCall_NeverUsesTheTrainingResponse()
    {
        var h = new Harness("{}");
        h.Endpoint.SetTrainingResponse("""{"success":true,"orderNumber":"TRN-1001"}""");
        Practice(h, "production", "production");

        await h.Handler().ExecuteAsync(h.Node(), h.Ctx, null, "");

        h.Executor.Verify(e => e.ExecuteAsync(It.IsAny<ApiDefinitionExecutionRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.False(h.Ctx.FlowVars.ContainsKey("order.response.orderNumber"));
    }

    // S179 — the definition's sandbox environment (e.g. Life Seasons' campaign approval runs through their sandbox).
    private static Harness SandboxHarness(bool trainingUsesSandbox = false, bool sandboxKeySet = true)
    {
        var h = new Harness("{}");
        h.Definition.SetAuthConfig("""{"type":"api_key","placement":"header","paramName":"x-functions-key","credentialKey":"LS:OrderApiKey"}""");
        h.Definition.SetSandbox("https://vendor-staging.example.com", null, trainingUsesSandbox);
        h.Endpoint.SetSandboxPath("/api/v1/test/addorder");
        h.Credentials.Setup(c => c.GetAsync("LS:OrderApiKey", It.IsAny<CancellationToken>())).ReturnsAsync("PROD-KEY");
        if (sandboxKeySet)
            h.Credentials.Setup(c => c.GetAsync("LS:OrderApiKey.sandbox", It.IsAny<CancellationToken>())).ReturnsAsync("SANDBOX-KEY");
        return h;
    }

    [Fact]
    public async Task DesignerSandbox_WithASandboxEnvironment_CallsTheSandbox_WithSandboxCredentials()
    {
        var h = SandboxHarness();
        Practice(h, "sandbox", "sandbox");

        var result = await h.Handler().ExecuteAsync(h.Node(), h.Ctx, null, "");

        Assert.Equal("n_ok", result.NextNodeId);
        Assert.Equal("https://vendor-staging.example.com/api/v1/test/addorder", h.Sent!.Url);
        Assert.Equal("SANDBOX-KEY", await h.Sent.GetCredential("LS:OrderApiKey", default));
        Assert.NotEqual(h.Definition.Id, h.Sent.DefinitionId);   // its own circuit breaker / rate limit
        Assert.Equal(ApiCallNodeHandler.SandboxHistoryNote, h.Ctx.ExecutionHistory[^1].InputValue);
    }

    [Fact]
    public async Task Training_UsesTheTrainingResponse_UnlessTheDefinitionAllowsTheSandbox()
    {
        var off = SandboxHarness(trainingUsesSandbox: false);
        Practice(off, "training", "sandbox");
        await off.Handler().ExecuteAsync(off.Node(), off.Ctx, null, "");
        off.Executor.Verify(e => e.ExecuteAsync(It.IsAny<ApiDefinitionExecutionRequest>(), It.IsAny<CancellationToken>()), Times.Never);

        var on = SandboxHarness(trainingUsesSandbox: true);
        Practice(on, "training", "sandbox");
        await on.Handler().ExecuteAsync(on.Node(), on.Ctx, null, "");
        Assert.StartsWith("https://vendor-staging.example.com", on.Sent!.Url);
    }

    [Fact]
    public async Task MissingSandboxCredential_Fails_NeverFallsBackToProduction()
    {
        var h = SandboxHarness(sandboxKeySet: false);
        Practice(h, "sandbox", "sandbox");

        var result = await h.Handler().ExecuteAsync(h.Node(), h.Ctx, null, "");

        Assert.Equal("n_err", result.NextNodeId);
        Assert.Contains("LS:OrderApiKey.sandbox", h.Ctx.FlowVars["order.error"]);
        h.Executor.Verify(e => e.ExecuteAsync(It.IsAny<ApiDefinitionExecutionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LiveCall_IgnoresTheSandboxEnvironment()
    {
        var h = SandboxHarness(trainingUsesSandbox: true);
        Practice(h, "production", "production");

        await h.Handler().ExecuteAsync(h.Node(), h.Ctx, null, "");

        Assert.Equal("https://vendor.example.com/api/v1/addorder", h.Sent!.Url);
        Assert.Equal("PROD-KEY", await h.Sent.GetCredential("LS:OrderApiKey", default));
        Assert.Equal(h.Definition.Id, h.Sent.DefinitionId);
    }

    [Fact]
    public void SandboxAuthConfig_SwapsOnlyTheOAuthTokenUrl()
    {
        const string oauth = """{"type":"oauth2","tokenUrl":"https://auth.example.com/token","clientIdKey":"V:Id","clientSecretKey":"V:Secret"}""";
        Assert.Contains("https://sandbox-auth.example.com/token", SandboxEnvironment.AuthConfig(oauth, "https://sandbox-auth.example.com/token"));
        Assert.Equal(oauth, SandboxEnvironment.AuthConfig(oauth, null));
        Assert.Equal([("clientIdKey", "V:Id"), ("clientSecretKey", "V:Secret")], SandboxEnvironment.CredentialKeys(oauth));
    }
}
