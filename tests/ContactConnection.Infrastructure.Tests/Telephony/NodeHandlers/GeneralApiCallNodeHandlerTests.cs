using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Telephony.NodeHandlers;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony.NodeHandlers;

/// <summary>
/// tf_general_api_call — the telephony engine's twin of the CRM api_call node — had zero coverage.
/// IApiDefinitionExecutor's own HTTP mechanics (retries, circuit breaking, rate limiting) and
/// ResponseFieldMasker are tested elsewhere; this file covers the handler's OWN glue: tenant vs.
/// portal scope dispatch, missing/inactive endpoint guards, success/error/timeout transition
/// mapping, output-variable flattening, sensitive-field masking wiring, and the header/query-param/
/// HMAC-payload template resolution (including the "_skipIfEmpty" query-param convention).
/// </summary>
public class GeneralApiCallNodeHandlerTests
{
    private static TenantDbContext NewTenantDb() =>
        new(new DbContextOptionsBuilder<TenantDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ContactConnectionDbContext NewPortalDb() =>
        new(new DbContextOptionsBuilder<ContactConnectionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static TelephonyFlowContext Ctx(string tenantSchema = "tenant_test_tenant") => new()
    {
        ChannelUuid = "uuid-1", CallerNumber = "+15551234567", DestinationNumber = "+15557654321",
        TenantId = Guid.NewGuid(), CampaignId = Guid.NewGuid(), CallRecordId = Guid.NewGuid(),
        TenantSubdomain = "test-tenant", TenantSchemaName = tenantSchema, TenantTimezone = "America/Chicago",
    };

    private static JsonObject Node(string? endpointId, string scope = "tenant", string? outputVariable = "api_result", int? timeoutSeconds = null) => new()
    {
        ["type"] = "tf_general_api_call",
        ["apiEndpointId"] = endpointId,
        ["apiDefinitionScope"] = scope,
        ["outputVariable"] = outputVariable,
        ["timeoutSeconds"] = timeoutSeconds,
        ["transitions"] = new JsonObject { ["success"] = "n_success", ["error"] = "n_error", ["timeout"] = "n_timeout" },
    };

    private static (TenantApiDefinition def, TenantApiEndpoint endpoint) MakeTenantTarget(
        string baseUrl = "https://api.example.com", string path = "customers", bool defActive = true, bool endpointActive = true)
    {
        var def = TenantApiDefinition.Create(ApiCategory.General, "Example API", "GET", baseUrl);
        if (defActive) def.Activate();
        var endpoint = TenantApiEndpoint.Create(def.Id, ApiCategory.General, "", "Get Customer", path);
        if (!endpointActive) endpoint.Deactivate();
        return (def, endpoint);
    }

    private static GeneralApiCallNodeHandler NewHandler(
        TenantDbContext tenantDb, ContactConnectionDbContext portalDb,
        IApiDefinitionExecutor? executor = null, ITenantCredentialStore? tenantCreds = null, IPortalCredentialStore? portalCreds = null)
    {
        var factory = new Mock<ITenantDbContextFactory>();
        factory.Setup(f => f.Create(It.IsAny<string>())).Returns(tenantDb);
        executor ??= Mock.Of<IApiDefinitionExecutor>();
        tenantCreds ??= Mock.Of<ITenantCredentialStore>();
        portalCreds ??= Mock.Of<IPortalCredentialStore>();
        return new GeneralApiCallNodeHandler(factory.Object, portalDb, tenantCreds, portalCreds, executor);
    }

    private static Mock<IApiDefinitionExecutor> SuccessExecutor(string responseBody = "{\"ok\":true}") =>
        MockExecutor(new ApiDefinitionExecutionResult(true, 200, "OK", new(), responseBody, false, null));

    private static Mock<IApiDefinitionExecutor> MockExecutor(ApiDefinitionExecutionResult result)
    {
        var executor = new Mock<IApiDefinitionExecutor>();
        executor.Setup(e => e.ExecuteAsync(It.IsAny<ApiDefinitionExecutionRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(result);
        return executor;
    }

    // ── Configuration guards ────────────────────────────────────────────────────

    [Fact]
    public async Task NoEndpointConfigured_TakesError_NoExecutorCall()
    {
        await using var tenantDb = NewTenantDb();
        await using var portalDb = NewPortalDb();
        var executor = new Mock<IApiDefinitionExecutor>();

        var result = await NewHandler(tenantDb, portalDb, executor.Object).ExecuteAsync(Node(null), Ctx());

        Assert.Equal("error", result.TransitionTaken);
        Assert.Equal("n_error", result.NextNodeId);
        executor.Verify(e => e.ExecuteAsync(It.IsAny<ApiDefinitionExecutionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MalformedEndpointGuid_TakesError()
    {
        await using var tenantDb = NewTenantDb();
        await using var portalDb = NewPortalDb();

        var result = await NewHandler(tenantDb, portalDb).ExecuteAsync(Node("not-a-guid"), Ctx());

        Assert.Equal("error", result.TransitionTaken);
    }

    [Fact]
    public async Task EndpointNotFound_TakesError()
    {
        await using var tenantDb = NewTenantDb();
        await using var portalDb = NewPortalDb();

        var result = await NewHandler(tenantDb, portalDb).ExecuteAsync(Node(Guid.NewGuid().ToString()), Ctx());

        Assert.Equal("error", result.TransitionTaken);
    }

    [Fact]
    public async Task InactiveDefinition_TakesError_NoExecutorCall()
    {
        await using var tenantDb = NewTenantDb();
        await using var portalDb = NewPortalDb();
        var (def, endpoint) = MakeTenantTarget(defActive: false);
        tenantDb.TenantApiDefinitions.Add(def);
        tenantDb.TenantApiEndpoints.Add(endpoint);
        await tenantDb.SaveChangesAsync();

        var executor = new Mock<IApiDefinitionExecutor>();
        var result = await NewHandler(tenantDb, portalDb, executor.Object).ExecuteAsync(Node(endpoint.Id.ToString()), Ctx());

        Assert.Equal("error", result.TransitionTaken);
        executor.Verify(e => e.ExecuteAsync(It.IsAny<ApiDefinitionExecutionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InactiveEndpoint_TakesError_NoExecutorCall()
    {
        await using var tenantDb = NewTenantDb();
        await using var portalDb = NewPortalDb();
        var (def, endpoint) = MakeTenantTarget(endpointActive: false);
        def.Activate();
        tenantDb.TenantApiDefinitions.Add(def);
        tenantDb.TenantApiEndpoints.Add(endpoint);
        await tenantDb.SaveChangesAsync();

        var executor = new Mock<IApiDefinitionExecutor>();
        var result = await NewHandler(tenantDb, portalDb, executor.Object).ExecuteAsync(Node(endpoint.Id.ToString()), Ctx());

        Assert.Equal("error", result.TransitionTaken);
        executor.Verify(e => e.ExecuteAsync(It.IsAny<ApiDefinitionExecutionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Transition mapping ──────────────────────────────────────────────────────

    [Fact]
    public async Task SuccessfulCall_TakesSuccessTransition()
    {
        await using var tenantDb = NewTenantDb();
        await using var portalDb = NewPortalDb();
        var (def, endpoint) = MakeTenantTarget();
        tenantDb.TenantApiDefinitions.Add(def);
        tenantDb.TenantApiEndpoints.Add(endpoint);
        await tenantDb.SaveChangesAsync();

        var result = await NewHandler(tenantDb, portalDb, SuccessExecutor().Object).ExecuteAsync(Node(endpoint.Id.ToString()), Ctx());

        Assert.Equal("success", result.TransitionTaken);
        Assert.Equal("n_success", result.NextNodeId);
    }

    [Fact]
    public async Task FailedCall_NotTimedOut_TakesErrorTransition()
    {
        await using var tenantDb = NewTenantDb();
        await using var portalDb = NewPortalDb();
        var (def, endpoint) = MakeTenantTarget();
        tenantDb.TenantApiDefinitions.Add(def);
        tenantDb.TenantApiEndpoints.Add(endpoint);
        await tenantDb.SaveChangesAsync();

        var executor = MockExecutor(new ApiDefinitionExecutionResult(false, 500, "Internal Server Error", new(), null, false, "server error"));
        var result = await NewHandler(tenantDb, portalDb, executor.Object).ExecuteAsync(Node(endpoint.Id.ToString()), Ctx());

        Assert.Equal("error", result.TransitionTaken);
        Assert.Equal("n_error", result.NextNodeId);
    }

    [Fact]
    public async Task TimedOutCall_TakesTimeoutTransition_EvenThoughSuccessIsFalse()
    {
        await using var tenantDb = NewTenantDb();
        await using var portalDb = NewPortalDb();
        var (def, endpoint) = MakeTenantTarget();
        tenantDb.TenantApiDefinitions.Add(def);
        tenantDb.TenantApiEndpoints.Add(endpoint);
        await tenantDb.SaveChangesAsync();

        var executor = MockExecutor(new ApiDefinitionExecutionResult(false, null, null, new(), null, true, "timed out"));
        var result = await NewHandler(tenantDb, portalDb, executor.Object).ExecuteAsync(Node(endpoint.Id.ToString()), Ctx());

        Assert.Equal("timeout", result.TransitionTaken);
        Assert.Equal("n_timeout", result.NextNodeId);
    }

    // ── Output variable flattening ──────────────────────────────────────────────

    [Fact]
    public async Task SuccessfulCall_FlattensResultIntoOutputVariable()
    {
        await using var tenantDb = NewTenantDb();
        await using var portalDb = NewPortalDb();
        var (def, endpoint) = MakeTenantTarget();
        tenantDb.TenantApiDefinitions.Add(def);
        tenantDb.TenantApiEndpoints.Add(endpoint);
        await tenantDb.SaveChangesAsync();

        var ctx = Ctx();
        await NewHandler(tenantDb, portalDb, SuccessExecutor().Object).ExecuteAsync(Node(endpoint.Id.ToString(), outputVariable: "api_result"), ctx);

        Assert.True(ctx.Vars.ContainsKey("api_result"));
        Assert.Equal("true", ctx.Vars["api_result.success"]);
        Assert.Equal("200", ctx.Vars["api_result.status_code"]);
    }

    [Fact]
    public async Task NoOutputVariable_WritesNoVars()
    {
        await using var tenantDb = NewTenantDb();
        await using var portalDb = NewPortalDb();
        var (def, endpoint) = MakeTenantTarget();
        tenantDb.TenantApiDefinitions.Add(def);
        tenantDb.TenantApiEndpoints.Add(endpoint);
        await tenantDb.SaveChangesAsync();

        var ctx = Ctx();
        await NewHandler(tenantDb, portalDb, SuccessExecutor().Object).ExecuteAsync(Node(endpoint.Id.ToString(), outputVariable: null), ctx);

        Assert.Empty(ctx.Vars);
    }

    // ── Request construction: URL, headers, query params, HMAC payload ─────────

    [Fact]
    public async Task Url_JoinsBaseUrlAndPath_ResolvingTemplateTags()
    {
        await using var tenantDb = NewTenantDb();
        await using var portalDb = NewPortalDb();
        var def = TenantApiDefinition.Create(ApiCategory.General, "Example", "GET", "https://api.example.com/");
        def.Activate();
        var endpoint = TenantApiEndpoint.Create(def.Id, ApiCategory.General, "", "Lookup", "/customers/{{caller.ani}}");
        tenantDb.TenantApiDefinitions.Add(def);
        tenantDb.TenantApiEndpoints.Add(endpoint);
        await tenantDb.SaveChangesAsync();

        var executor = SuccessExecutor();
        await NewHandler(tenantDb, portalDb, executor.Object).ExecuteAsync(Node(endpoint.Id.ToString()), Ctx());

        executor.Verify(e => e.ExecuteAsync(
            It.Is<ApiDefinitionExecutionRequest>(r => r.Url == "https://api.example.com/customers/+15551234567"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Headers_AreResolvedThroughTemplateResolver()
    {
        await using var tenantDb = NewTenantDb();
        await using var portalDb = NewPortalDb();
        var (def, endpoint) = MakeTenantTarget();
        endpoint.SetHeaders("{\"X-Ani\":\"{{caller.ani}}\",\"X-Static\":\"fixed\"}");
        tenantDb.TenantApiDefinitions.Add(def);
        tenantDb.TenantApiEndpoints.Add(endpoint);
        await tenantDb.SaveChangesAsync();

        var executor = SuccessExecutor();
        await NewHandler(tenantDb, portalDb, executor.Object).ExecuteAsync(Node(endpoint.Id.ToString()), Ctx());

        executor.Verify(e => e.ExecuteAsync(
            It.Is<ApiDefinitionExecutionRequest>(r => r.Headers["X-Ani"] == "+15551234567" && r.Headers["X-Static"] == "fixed"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task QueryParams_SkipIfEmpty_OmitsKeyWhenResolvedValueIsBlank()
    {
        await using var tenantDb = NewTenantDb();
        await using var portalDb = NewPortalDb();
        var (def, endpoint) = MakeTenantTarget();
        endpoint.SetQueryParams("{\"account\":\"{{flow.account_number}}\",\"source\":\"ivr\",\"_skipIfEmpty\":[\"account\"]}");
        tenantDb.TenantApiDefinitions.Add(def);
        tenantDb.TenantApiEndpoints.Add(endpoint);
        await tenantDb.SaveChangesAsync();

        var executor = SuccessExecutor();
        var ctx = Ctx();
        // account_number never set — resolves to "".
        await NewHandler(tenantDb, portalDb, executor.Object).ExecuteAsync(Node(endpoint.Id.ToString()), ctx);

        executor.Verify(e => e.ExecuteAsync(
            It.Is<ApiDefinitionExecutionRequest>(r => !r.QueryParams.ContainsKey("account") && r.QueryParams["source"] == "ivr" && !r.QueryParams.ContainsKey("_skipIfEmpty")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task QueryParams_NotSkipIfEmpty_SendsEmptyStringWhenUnresolved()
    {
        await using var tenantDb = NewTenantDb();
        await using var portalDb = NewPortalDb();
        var (def, endpoint) = MakeTenantTarget();
        endpoint.SetQueryParams("{\"account\":\"{{flow.account_number}}\"}");
        tenantDb.TenantApiDefinitions.Add(def);
        tenantDb.TenantApiEndpoints.Add(endpoint);
        await tenantDb.SaveChangesAsync();

        var executor = SuccessExecutor();
        await NewHandler(tenantDb, portalDb, executor.Object).ExecuteAsync(Node(endpoint.Id.ToString()), Ctx());

        executor.Verify(e => e.ExecuteAsync(
            It.Is<ApiDefinitionExecutionRequest>(r => r.QueryParams["account"] == ""),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HmacAuthType_PayloadTemplate_IsResolvedAndPassedThrough()
    {
        await using var tenantDb = NewTenantDb();
        await using var portalDb = NewPortalDb();
        var (def, endpoint) = MakeTenantTarget();
        def.SetAuthConfig("{\"type\":\"hmac\",\"payloadTemplate\":\"ani={{caller.ani}}\"}");
        tenantDb.TenantApiDefinitions.Add(def);
        tenantDb.TenantApiEndpoints.Add(endpoint);
        await tenantDb.SaveChangesAsync();

        var executor = SuccessExecutor();
        await NewHandler(tenantDb, portalDb, executor.Object).ExecuteAsync(Node(endpoint.Id.ToString()), Ctx());

        executor.Verify(e => e.ExecuteAsync(
            It.Is<ApiDefinitionExecutionRequest>(r => r.HmacPayload == "ani=+15551234567"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NonHmacAuthType_HmacPayloadIsNull()
    {
        await using var tenantDb = NewTenantDb();
        await using var portalDb = NewPortalDb();
        var (def, endpoint) = MakeTenantTarget();
        tenantDb.TenantApiDefinitions.Add(def);
        tenantDb.TenantApiEndpoints.Add(endpoint);
        await tenantDb.SaveChangesAsync();

        var executor = SuccessExecutor();
        await NewHandler(tenantDb, portalDb, executor.Object).ExecuteAsync(Node(endpoint.Id.ToString()), Ctx());

        executor.Verify(e => e.ExecuteAsync(
            It.Is<ApiDefinitionExecutionRequest>(r => r.HmacPayload == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Timeout override, credential dispatch by scope ─────────────────────────

    [Fact]
    public async Task TimeoutOverride_OnNode_TakesPrecedenceOverDefinitionDefault()
    {
        await using var tenantDb = NewTenantDb();
        await using var portalDb = NewPortalDb();
        var (def, endpoint) = MakeTenantTarget();
        tenantDb.TenantApiDefinitions.Add(def);
        tenantDb.TenantApiEndpoints.Add(endpoint);
        await tenantDb.SaveChangesAsync();

        var executor = SuccessExecutor();
        await NewHandler(tenantDb, portalDb, executor.Object).ExecuteAsync(Node(endpoint.Id.ToString(), timeoutSeconds: 5), Ctx());

        executor.Verify(e => e.ExecuteAsync(It.Is<ApiDefinitionExecutionRequest>(r => r.TimeoutSeconds == 5), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NoTimeoutOverride_UsesDefinitionDefault()
    {
        await using var tenantDb = NewTenantDb();
        await using var portalDb = NewPortalDb();
        var (def, endpoint) = MakeTenantTarget();
        tenantDb.TenantApiDefinitions.Add(def);
        tenantDb.TenantApiEndpoints.Add(endpoint);
        await tenantDb.SaveChangesAsync();

        var executor = SuccessExecutor();
        await NewHandler(tenantDb, portalDb, executor.Object).ExecuteAsync(Node(endpoint.Id.ToString(), timeoutSeconds: null), Ctx());

        executor.Verify(e => e.ExecuteAsync(It.Is<ApiDefinitionExecutionRequest>(r => r.TimeoutSeconds == 30), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TenantScope_UsesTenantCredentialStore_WithExplicitTenantSubdomain()
    {
        await using var tenantDb = NewTenantDb();
        await using var portalDb = NewPortalDb();
        var (def, endpoint) = MakeTenantTarget();
        tenantDb.TenantApiDefinitions.Add(def);
        tenantDb.TenantApiEndpoints.Add(endpoint);
        await tenantDb.SaveChangesAsync();

        var tenantCreds = new Mock<ITenantCredentialStore>();
        tenantCreds.Setup(c => c.GetForTenantAsync("test-tenant", "api_key", It.IsAny<CancellationToken>())).ReturnsAsync("secret-value");
        var portalCreds = new Mock<IPortalCredentialStore>();

        ApiDefinitionExecutionRequest? captured = null;
        var executor = new Mock<IApiDefinitionExecutor>();
        executor.Setup(e => e.ExecuteAsync(It.IsAny<ApiDefinitionExecutionRequest>(), It.IsAny<CancellationToken>()))
                .Callback<ApiDefinitionExecutionRequest, CancellationToken>((r, _) => captured = r)
                .ReturnsAsync(new ApiDefinitionExecutionResult(true, 200, "OK", new(), "{}", false, null));

        await NewHandler(tenantDb, portalDb, executor.Object, tenantCreds.Object, portalCreds.Object)
            .ExecuteAsync(Node(endpoint.Id.ToString(), scope: "tenant"), Ctx());

        Assert.NotNull(captured);
        var cred = await captured!.GetCredential("api_key", default);
        Assert.Equal("secret-value", cred);
        portalCreds.Verify(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PortalScope_ResolvesFromPortalTables_AndUsesPortalCredentialStore()
    {
        await using var tenantDb = NewTenantDb();
        await using var portalDb = NewPortalDb();
        var def = PortalApiDefinition.Create(ApiCategory.General, "Portal API", "GET", "https://portal.example.com");
        def.Activate();
        var endpoint = PortalApiEndpoint.Create(def.Id, ApiCategory.General, "", "Lookup", "info");
        portalDb.PortalApiDefinitions.Add(def);
        portalDb.PortalApiEndpoints.Add(endpoint);
        await portalDb.SaveChangesAsync();

        var portalCreds = new Mock<IPortalCredentialStore>();
        portalCreds.Setup(c => c.GetAsync("api_key", It.IsAny<CancellationToken>())).ReturnsAsync("portal-secret");
        var tenantCreds = new Mock<ITenantCredentialStore>();

        ApiDefinitionExecutionRequest? captured = null;
        var executor = new Mock<IApiDefinitionExecutor>();
        executor.Setup(e => e.ExecuteAsync(It.IsAny<ApiDefinitionExecutionRequest>(), It.IsAny<CancellationToken>()))
                .Callback<ApiDefinitionExecutionRequest, CancellationToken>((r, _) => captured = r)
                .ReturnsAsync(new ApiDefinitionExecutionResult(true, 200, "OK", new(), "{}", false, null));

        var result = await NewHandler(tenantDb, portalDb, executor.Object, tenantCreds.Object, portalCreds.Object)
            .ExecuteAsync(Node(endpoint.Id.ToString(), scope: "portal"), Ctx());

        Assert.Equal("success", result.TransitionTaken);
        Assert.NotNull(captured);
        var cred = await captured!.GetCredential("api_key", default);
        Assert.Equal("portal-secret", cred);
        tenantCreds.Verify(c => c.GetForTenantAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PortalScope_EndpointNotFound_DoesNotFallBackToTenantTables()
    {
        await using var tenantDb = NewTenantDb();
        await using var portalDb = NewPortalDb();
        // A tenant-scoped endpoint with the SAME id exists — proves scope="portal" never
        // accidentally reads across into the tenant store.
        var (def, endpoint) = MakeTenantTarget();
        tenantDb.TenantApiDefinitions.Add(def);
        tenantDb.TenantApiEndpoints.Add(endpoint);
        await tenantDb.SaveChangesAsync();

        var executor = new Mock<IApiDefinitionExecutor>();
        var result = await NewHandler(tenantDb, portalDb, executor.Object).ExecuteAsync(Node(endpoint.Id.ToString(), scope: "portal"), Ctx());

        Assert.Equal("error", result.TransitionTaken);
        executor.Verify(e => e.ExecuteAsync(It.IsAny<ApiDefinitionExecutionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Sensitive-field masking wiring ──────────────────────────────────────────

    [Fact]
    public async Task SensitiveResponseFields_AreMaskedBeforeReachingFlowVariables()
    {
        await using var tenantDb = NewTenantDb();
        await using var portalDb = NewPortalDb();
        var (def, endpoint) = MakeTenantTarget();
        endpoint.SetSensitiveResponseFields("[\"ssn\"]");
        tenantDb.TenantApiDefinitions.Add(def);
        tenantDb.TenantApiEndpoints.Add(endpoint);
        await tenantDb.SaveChangesAsync();

        var executor = SuccessExecutor("{\"ssn\":\"123-45-6789\",\"name\":\"Jane\"}");
        var ctx = Ctx();
        await NewHandler(tenantDb, portalDb, executor.Object).ExecuteAsync(Node(endpoint.Id.ToString()), ctx);

        Assert.DoesNotContain("123-45-6789", ctx.Vars["api_result"]);
        Assert.DoesNotContain("123-45-6789", ctx.Vars.GetValueOrDefault("api_result.response", ""));
    }
}
