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
                endpoints.Object, Mock.Of<IPortalApiEndpointRepository>(), Mock.Of<ITenantCredentialStore>(),
                Mock.Of<IPortalCredentialStore>(), Executor.Object, new FluidLiquidTemplateRenderer(), model.Object, Cache.Object);
        }

        public JsonObject Node(bool oncePerCall = false) => new()
        {
            ["type"] = "api_call", ["apiEndpointId"] = Endpoint.Id.ToString(), ["apiDefinitionScope"] = "tenant",
            ["outputVariable"] = "order", ["oncePerCall"] = oncePerCall,
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
}
