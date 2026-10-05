using System.Text.Json.Nodes;
using ContactConnection.Infrastructure.Data;
using Microsoft.Extensions.Configuration;

namespace ContactConnection.Infrastructure.Ai;

/// <summary>
/// AI call summary (AI step 2, S171): context (step 1) + allowed dispositions → one model call that must
/// answer through the <c>record_call_summary</c> tool → validated suggestion. Suggest-only: nothing is
/// saved here; the agent reviews it (step 3).
/// </summary>
public class CallSummarizer(AnthropicClient client, IConfiguration config)
{
    // Small, fast, cheap — summarizing a few hundred tokens of facts doesn't need a bigger model.
    // Override with Anthropic:SummaryModel.
    public const string DefaultModel = "claude-haiku-4-5-20251001";

    // Estimated price per million tokens (USD) for the cost readout — check Anthropic's pricing page and
    // override with Anthropic:InputPricePerMTok / Anthropic:OutputPricePerMTok.
    private const decimal DefaultInputPrice = 1.00m, DefaultOutputPrice = 5.00m;

    public static readonly string[] Outcomes = ["order_placed", "order_failed", "no_sale", "customer_service", "test_or_junk", "other"];

    public const string SystemPrompt = """
        You summarize contact center calls for the agent who handled the call.
        The agent reviews and corrects your output before anything is saved.

        How the call is described:
        - CALL FACTS are recorded facts. "Order placed" and "Payment status" are the real outcomes.
        - SCRIPT AS WORKED lists the script steps in order. "Question → answer" is the answer the agent
          recorded. "(step reached)" means the step ran but its result isn't shown — use CALL FACTS for
          the outcome.
        - "Captured:" lists customer details that were collected but are not shown to you.
        - "Payments:" are the recorded payment results — an approved authorization means payment did NOT fail.

        Rules:
        - Use only the information given. Never invent details, amounts, reasons or outcomes. If something
          isn't recorded, leave it out.
        - "Possible test call" in CALL FACTS means placeholder answers were found: set is_test_call to true
          and say in the summary that it appears to be a test call.
        - summary: 2–3 plain sentences, past tense, for a supervisor skimming later. No greetings, no
          personal details.
        - suggested_disposition: choose the best fit from the allowed values. If a disposition is already
          recorded and the facts support it, keep it.
        - confidence: 0 to 1, how well the facts support the disposition. Below 0.6 when facts conflict or
          are thin.
        - follow_up: one concrete next action if the call needs one, otherwise null.
        """;

    public record Summary(
        string Text, string ReasonForCall, string Outcome, string? SuggestedDisposition, bool DispositionValid,
        double Confidence, string? FollowUp, bool IsTestCall);

    public record Usage(string Model, int InputTokens, int OutputTokens, decimal EstimatedCostUsd, long ElapsedMs, int Attempts);

    /// <param name="PossibleTestCall">Detected by our own code (placeholder answers) — shown regardless of
    /// what the model says.</param>
    public record Result(Summary Summary, Usage Usage, List<string> AllowedDispositions, bool PossibleTestCall);

    /// <summary>The tool the model must "call" — its input schema IS the output format.</summary>
    internal static JsonObject Tool(IReadOnlyList<string> dispositions)
    {
        JsonObject Str(string description) => new() { ["type"] = "string", ["description"] = description };
        JsonObject Enum(IEnumerable<string> values, string description) =>
            new() { ["type"] = "string", ["enum"] = new JsonArray(values.Select(v => (JsonNode)v!).ToArray()), ["description"] = description };

        return new JsonObject
        {
            ["name"] = "record_call_summary",
            ["description"] = "Record the call summary and suggested disposition for the agent to review.",
            ["input_schema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["summary"] = Str("2–3 plain sentences, past tense."),
                    ["reason_for_call"] = Str("Why the caller called, in a few words."),
                    ["outcome"] = Enum(Outcomes, "How the call ended."),
                    ["suggested_disposition"] = dispositions.Count > 0
                        ? Enum(dispositions, "Best-fitting disposition from the allowed values.")
                        : Str("Best-fitting disposition."),
                    ["confidence"] = new JsonObject { ["type"] = "number", ["minimum"] = 0, ["maximum"] = 1, ["description"] = "How well the facts support the disposition." },
                    ["follow_up"] = new JsonObject { ["type"] = new JsonArray("string", "null"), ["description"] = "One concrete next action, or null." },
                    // A required yes/no forces an explicit decision — prose instructions alone get skipped.
                    ["is_test_call"] = new JsonObject { ["type"] = "boolean", ["description"] = "True if this appears to be a test call." },
                },
                ["required"] = new JsonArray("summary", "reason_for_call", "outcome", "suggested_disposition", "confidence", "follow_up", "is_test_call"),
            },
        };
    }

    public async Task<Result?> SummarizeAsync(TenantDbContext db, Guid callRecordId, CancellationToken ct, Guid? interactionId = null)
    {
        var context = await CallSummaryContextBuilder.BuildAsync(db, callRecordId, ct, interactionId);
        if (context is null) return null;
        var dispositions = await DispositionCatalog.ForCallAsync(db, callRecordId, ct, interactionId);

        var model = config["Anthropic:SummaryModel"] ?? DefaultModel;
        var reply = await client.CallToolAsync(model, SystemPrompt, context.Text, Tool(dispositions), maxTokens: 500, ct);

        var summary = Validate(reply.Input, dispositions);
        var inPrice = decimal.TryParse(config["Anthropic:InputPricePerMTok"], out var ip) ? ip : DefaultInputPrice;
        var outPrice = decimal.TryParse(config["Anthropic:OutputPricePerMTok"], out var op) ? op : DefaultOutputPrice;
        var cost = Math.Round((reply.InputTokens * inPrice + reply.OutputTokens * outPrice) / 1_000_000m, 6);
        return new Result(summary, new Usage(reply.Model, reply.InputTokens, reply.OutputTokens, cost, reply.ElapsedMs, reply.Attempts),
            dispositions, context.PossibleTestCall);
    }

    /// <summary>
    /// Never trust the model's output blindly — even with a schema. Missing text becomes a refusal to
    /// answer, an unknown outcome becomes "other", a disposition outside the allowed list is flagged (and
    /// not offered), and confidence is clamped to 0–1.
    /// </summary>
    internal static Summary Validate(JsonObject input, IReadOnlyList<string> dispositions)
    {
        string? S(string key) => input[key] is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;

        var text = S("summary") ?? throw new AnthropicClient.AiUnavailableException("The AI returned an empty summary.");
        var outcome = S("outcome") is { } o && Outcomes.Contains(o) ? o : "other";
        var disposition = S("suggested_disposition");
        var match = disposition is null ? null : dispositions.FirstOrDefault(d => string.Equals(d, disposition, StringComparison.OrdinalIgnoreCase));
        var valid = dispositions.Count == 0 ? disposition is not null : match is not null;
        var confidence = input["confidence"] is JsonValue c && c.TryGetValue<double>(out var x) ? Math.Clamp(x, 0, 1) : 0;

        var isTest = input["is_test_call"] is JsonValue t && t.TryGetValue<bool>(out var b) && b;
        return new Summary(text, S("reason_for_call") ?? "", outcome, valid ? match ?? disposition : disposition, valid, confidence, S("follow_up"), isTest);
    }
}
