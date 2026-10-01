using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.ApiExecution;
using ContactConnection.Infrastructure.Telephony.NodeHandlers;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.ApiExecution;

/// <summary>S169: the API request preview / trace record never exposes credentials or card data.</summary>
public class ApiRequestPreviewerTests
{
    private static ApiDefinitionExecutionRequest Request(Dictionary<string, string>? headers = null, Dictionary<string, string>? query = null, string? body = "{}")
        => new(
            HttpMethod: "post", Url: "https://api.example.com/orders", Headers: headers ?? [], QueryParams: query ?? [],
            Body: body, AuthConfigJson: """{"type":"bearer","credentialKey":"LS:Token"}""", TimeoutSeconds: 30,
            GetCredential: (_, _) => Task.FromResult<string?>("SHOULD-NEVER-APPEAR"), DefinitionId: Guid.NewGuid(),
            AllowRetryOnAmbiguousFailure: false, RateLimitPerMinute: null, HmacPayload: null);

    [Fact]
    public void Preview_MasksAuthLookingHeaders_AndNamesTheAuthType()
    {
        var p = ApiRequestPreviewer.From("LS → Add Order", Request(new()
        {
            ["Authorization"] = "Bearer abc123", ["X-Api-Key"] = "k-999", ["Content-Type"] = "application/json",
        }), "liquid", null);

        Assert.Equal("POST", p.Method);
        Assert.Equal("bearer", p.AuthType);
        Assert.Equal("application/json", p.Headers["Content-Type"]);
        Assert.DoesNotContain("abc123", p.Headers["Authorization"]);
        Assert.DoesNotContain("k-999", p.Headers["X-Api-Key"]);
    }

    [Fact]
    public void Preview_ShowsTheFullUrlWithQuery()
    {
        var p = ApiRequestPreviewer.From(null, Request(query: new() { ["campaign"] = "NeuroQ LF", ["v"] = "2" }), "simple", null);
        Assert.StartsWith("https://api.example.com/orders?", p.Url);
        Assert.Contains("v=2", p.Url);
        Assert.Contains("campaign=NeuroQ", p.Url);
    }

    [Fact]
    public void TelephonyRecord_IsRedacted_WhenBuiltFromCapturedCardData()
    {
        var target = new GeneralApiCallNodeHandler.CallTarget(
            Guid.NewGuid(), "POST", "https://tokenizer.example.com", "/tokens", "{}", "{}",
            """{"pan":"{{secure.pan}}"}""", "{}", 30, true, false, null, "[]", "simple", "");
        var record = GeneralApiCallNodeHandler.RecordRequest(target, Request(body: """{"pan":"4111111111111111"}"""), null);
        Assert.DoesNotContain("4111", record);
        Assert.Contains("redacted", record);
    }

    [Fact]
    public void TelephonyRecord_HoldsTheRenderedRequest_Otherwise()
    {
        var target = new GeneralApiCallNodeHandler.CallTarget(
            Guid.NewGuid(), "POST", "https://api.example.com", "/orders", "{}", "{}",
            """{"phone":"{{flow.ani}}"}""", "{}", 30, true, false, null, "[]", "simple", "");
        var record = GeneralApiCallNodeHandler.RecordRequest(target, Request(body: """{"phone":"5415551234"}"""), null);
        Assert.Contains("5415551234", record);
        Assert.Contains("\"method\":\"POST\"", record);
    }
}
