using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Ai;

/// <summary>
/// Minimal client for Anthropic's Messages API (AI step 2, S171) — one HTTPS POST, exactly like our other
/// vendor integrations. Runs on the server only: the API key comes from configuration (User Secrets
/// locally, Key Vault in production as <c>Anthropic--ApiKey</c>) and never reaches the browser.
///
/// Failure handling: a request times out after <see cref="Timeout"/>; overloaded / rate-limited / server
/// errors and timeouts are retried once after a short pause; anything else fails fast. Callers treat a
/// failure as "no AI this time" — nothing on a call ever depends on the AI answering.
/// </summary>
public class AnthropicClient(IHttpClientFactory httpFactory, IConfiguration config, ILogger<AnthropicClient> logger)
{
    private const string Endpoint = "https://api.anthropic.com/v1/messages";
    private const string ApiVersion = "2023-06-01";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1.5);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(config["Anthropic:ApiKey"]);

    /// <summary>What a tool-forced call returns: the tool's input (our structured answer) plus usage.</summary>
    public record ToolResult(JsonObject Input, string Model, int InputTokens, int OutputTokens, long ElapsedMs, int Attempts);

    public class AiUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

    /// <summary>
    /// Sends one request that forces the model to answer by "calling" <paramref name="tool"/> — so the
    /// answer comes back as JSON matching the tool's schema rather than free text.
    /// </summary>
    public async Task<ToolResult> CallToolAsync(
        string model, string system, string userContent, JsonObject tool, int maxTokens, CancellationToken ct)
    {
        var apiKey = config["Anthropic:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey)) throw new AiUnavailableException("AI isn't configured (no Anthropic API key).");

        var toolName = tool["name"]!.GetValue<string>();
        var body = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = maxTokens,
            ["temperature"] = 0,                     // consistency over creativity
            ["system"] = system,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = userContent }),
            ["tools"] = new JsonArray(tool.DeepClone()),
            ["tool_choice"] = new JsonObject { ["type"] = "tool", ["name"] = toolName },   // must answer via the tool
        };

        var clock = Stopwatch.StartNew();
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = JsonContent.Create(body) };
                request.Headers.Add("x-api-key", apiKey);
                request.Headers.Add("anthropic-version", ApiVersion);

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(Timeout);
                using var response = await httpFactory.CreateClient("Anthropic").SendAsync(request, timeout.Token);
                var text = await response.Content.ReadAsStringAsync(timeout.Token);

                if (!response.IsSuccessStatusCode)
                {
                    if (attempt == 1 && IsRetryable(response.StatusCode))
                    {
                        logger.LogWarning("Anthropic returned {Status}; retrying once", (int)response.StatusCode);
                        await Task.Delay(RetryDelay, ct);
                        continue;
                    }
                    // Log the status and Anthropic's error type, never the request (it holds call data).
                    logger.LogWarning("Anthropic request failed: {Status} {Error}", (int)response.StatusCode, ErrorType(text));
                    throw new AiUnavailableException($"The AI service returned an error ({(int)response.StatusCode}).");
                }

                var json = JsonNode.Parse(text)!.AsObject();
                var toolUse = json["content"]?.AsArray().OfType<JsonObject>()
                    .FirstOrDefault(c => c["type"]?.GetValue<string>() == "tool_use" && c["name"]?.GetValue<string>() == toolName);
                if (toolUse?["input"] is not JsonObject input)
                    throw new AiUnavailableException("The AI didn't return a structured answer.");

                var usage = json["usage"];
                return new ToolResult(
                    input,
                    json["model"]?.GetValue<string>() ?? model,
                    usage?["input_tokens"]?.GetValue<int>() ?? 0,
                    usage?["output_tokens"]?.GetValue<int>() ?? 0,
                    clock.ElapsedMilliseconds,
                    attempt);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Our timeout fired (not the caller cancelling).
                if (attempt == 1) { logger.LogWarning("Anthropic request timed out; retrying once"); continue; }
                throw new AiUnavailableException("The AI service didn't respond in time.");
            }
            catch (HttpRequestException ex)
            {
                if (attempt == 1) { await Task.Delay(RetryDelay, ct); continue; }
                throw new AiUnavailableException("Couldn't reach the AI service.", ex);
            }
        }
    }

    // 429 rate limited, 529 overloaded, 5xx server errors — worth one more try. 4xx means our request is
    // wrong (bad key, bad schema) and a retry would fail the same way.
    private static bool IsRetryable(HttpStatusCode s) => (int)s is 429 or 529 or >= 500;

    private static string ErrorType(string body)
    {
        try { return JsonNode.Parse(body)?["error"]?["type"]?.GetValue<string>() ?? "unknown"; }
        catch (JsonException) { return "unparseable"; }
    }
}
