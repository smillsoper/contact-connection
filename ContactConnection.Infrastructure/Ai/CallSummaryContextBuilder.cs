using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Commerce;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Ai;

/// <summary>
/// Builds the "context" for AI call summarization (AI step 1, S171) — the text the model will read.
/// The model has no access to the platform and no memory: this text is everything it knows about the
/// call, so what goes in here decides the quality, the cost and the safety of the summary.
///
/// What goes in — the call's facts, in plain labelled text:
/// <list type="bullet">
/// <item>campaign, how the call ended, handle time, whether an order was placed and what was in the cart;</item>
/// <item>the call's custom fields (call type, disposition, …);</item>
/// <item>the script as it was worked — each section, each question with the answer given, each order or
/// payment step reached. Our stand-in for a transcript until recordings are transcribed.</item>
/// </list>
/// What stays out (data minimization — every token costs, and nothing leaves that a summary doesn't
/// need): personal details (name, zip, address, phone, email, card capture) are only NAMED once as
/// "captured", never shown; navigation clicks ("Continue") and read-aloud script text are dropped;
/// timestamps are stripped; everything else is scrubbed by <see cref="AiRedactor"/>. The secure-collect
/// card blob on the call record is never read here at all.
/// </summary>
public static partial class CallSummaryContextBuilder
{
    public record Result(string Text, Dictionary<string, int> Redactions, int ScriptSteps);

    // Steps that capture personal data → the kind of detail, named once in CALL FACTS.
    private static readonly Dictionary<string, string> WithheldNodeTypes = new()
    {
        ["address"] = "address",
        ["phone"] = "phone",
        ["email"] = "email",
        ["tf_secure_collect"] = "payment card",
    };

    // Questions whose answer is personal data, recognized by their label.
    private static readonly (string LabelContains, string Kind)[] WithheldLabels =
    [
        ("name", "name"), ("zip", "zip"), ("postal", "zip"), ("birth", "date of birth"),
        ("ssn", "SSN"), ("social security", "SSN"),
    ];

    // Answers that only move the script along — not something the caller said or chose.
    private static readonly HashSet<string> NavigationAnswers = new(StringComparer.OrdinalIgnoreCase)
        { "continue", "next", "ok", "okay", "done", "proceed" };

    // Answers typed as placeholders on test calls. Detected here in code — reliable — and handed to the
    // model as a plain fact, instead of asking the model to notice the pattern (it often doesn't).
    private static readonly HashSet<string> PlaceholderAnswers = new(StringComparer.OrdinalIgnoreCase)
        { "test", "testing", "test test", "asdf", "asdfasdf", "qwerty", "xxx", "xx", "abc", "abc123", "123", "1234", "foo", "dummy" };

    // Script plumbing, plus read-aloud text and the end marker: the section headings already show how far
    // the call got, and the script wording is the same on every call.
    private static readonly HashSet<string> SkippedNodeTypes =
        ["set_variable", "set_custom_field", "branch", "start", "delay", "set_shared_variable", "script", "end"];

    private record Step(string NodeType, string Label, string? InputValue);

    public static async Task<Result?> BuildAsync(TenantDbContext db, Guid callRecordId, CancellationToken ct)
    {
        var record = await db.CallRecords.AsNoTracking().FirstOrDefaultAsync(r => r.Id == callRecordId, ct);
        if (record is null) return null;

        var campaign = await db.Campaigns.AsNoTracking().Where(c => c.Id == record.CampaignId).Select(c => c.Name).FirstOrDefaultAsync(ct);
        var client = await db.Clients.AsNoTracking().Where(c => c.Id == record.ClientId).Select(c => c.Name).FirstOrDefaultAsync(ct);
        var histories = await db.FlowSessions.AsNoTracking()
            .Where(s => s.CallRecordId == callRecordId).OrderBy(s => s.StartedAt)
            .Select(s => s.ExecutionHistory).ToListAsync(ct);

        return Build(record, client, campaign, histories);
    }

    /// <summary>Pure formatting — everything the model will see is decided here.</summary>
    internal static Result Build(CallRecord record, string? client, string? campaign, IEnumerable<string> executionHistories)
    {
        var r = new AiRedactor();
        var withheld = new List<string>();
        var placeholders = new List<(string Label, string Value)>();

        // The script first, so CALL FACTS can name the customer details that were captured.
        var script = new StringBuilder();
        var steps = 0;
        string? previous = null;
        foreach (var json in executionHistories)
        {
            foreach (var step in Parse(json))
            {
                if (SkippedNodeTypes.Contains(step.NodeType)) continue;
                var line = FormatStep(step, r, withheld);
                if (line is not null && step.NodeType == "input" && step.InputValue?.Trim() is { } answer && PlaceholderAnswers.Contains(answer))
                    placeholders.Add((r.Scrub(step.Label), answer));
                // The engine can record a step twice (shown, then continued) — collapse identical
                // back-to-back lines: repeats cost tokens and could read as "asked twice".
                if (line is null || line == previous) continue;
                previous = line;
                script.AppendLine(line);
                steps++;
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("CALL FACTS");
        sb.AppendLine($"Client / campaign: {client ?? "unknown"} / {campaign ?? "unknown"}");
        sb.AppendLine($"Direction: {record.Source}");
        if (record.HandleTimeSeconds is { } secs) sb.AppendLine($"Handle time: {secs / 60}m {secs % 60}s");
        sb.AppendLine($"Call status: {record.OverallStatus}");
        sb.AppendLine($"Order placed: {(record.OrderSubmittedAt is null ? "no" : "yes")}");
        if (!string.IsNullOrWhiteSpace(record.PaymentStatus)) sb.AppendLine($"Payment status: {record.PaymentStatus}");
        if (record.RoutedTierLabel is { } tier) sb.AppendLine($"Routed through tier: {tier}");
        if (withheld.Count > 0) sb.AppendLine($"Captured: {string.Join(", ", withheld.Distinct())}");
        if (placeholders.Count > 0)
            sb.AppendLine($"Possible test call: placeholder answers ({string.Join(", ", placeholders.Select(p => $"\"{p.Value}\"").Distinct())}) at {string.Join(", ", placeholders.Select(p => p.Label).Distinct())}");

        if (record.Cart is { Items.Count: > 0 } cart)
        {
            sb.AppendLine();
            sb.AppendLine("CART");
            foreach (var item in cart.Items)
                sb.AppendLine($"- {item.Quantity} x {r.Scrub(item.Description)} @ {Money(item.FullPrice)}{(item.AutoShip ? " (auto-ship)" : "")}");
            sb.AppendLine($"Subtotal {Money(cart.CartSubtotal)}, shipping {Money(cart.Shipping)}, tax {Money(cart.SalesTax)}, total {Money(cart.CartTotal)}");
        }

        var fields = CommissionLedger.CustomFieldValues(record.CustomFields).Where(kv => !string.IsNullOrWhiteSpace(kv.Value)).ToList();
        if (fields.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("RECORDED FIELDS");
            foreach (var (name, value) in fields) sb.AppendLine($"- {name}: {r.Scrub(StripTimestamps(value))}");
        }

        sb.AppendLine();
        sb.AppendLine("SCRIPT AS WORKED");
        sb.Append(script);
        if (steps == 0) sb.AppendLine("(no script steps recorded)");

        return new Result(sb.ToString().TrimEnd(), r.Counts, steps);
    }

    private static string? FormatStep(Step s, AiRedactor r, List<string> withheld)
    {
        var label = r.Scrub(s.Label);
        if (s.NodeType == "section") return $"== {label} ==";
        if (WithheldNodeTypes.TryGetValue(s.NodeType, out var kind))
        {
            withheld.Add(r.Withhold("personal details", kind));
            return null;
        }
        // The history records which step came next, not the result — so only say the step was reached;
        // the real outcome is in CALL FACTS (order placed, payment status).
        if (s.NodeType is "authorize_payment" or "void_payment" or "api_call") return $"- {label} (step reached)";

        var value = s.InputValue?.Trim();
        if (string.IsNullOrEmpty(value) || NavigationAnswers.Contains(value)) return null;
        if (s.NodeType == "input")
            foreach (var (contains, personal) in WithheldLabels)
                if (s.Label.Contains(contains, StringComparison.OrdinalIgnoreCase))
                {
                    withheld.Add(r.Withhold("personal details", personal));
                    return null;
                }
        return $"- {label} → {r.Scrub(value)}";
    }

    /// <summary>"Yes at 2026-10-01T23:53:04.147+00:00" → "Yes" — the time adds tokens, not meaning.</summary>
    internal static string StripTimestamps(string value) => Timestamp().Replace(value, "").Trim();

    [GeneratedRegex(@"\s*(?:at\s+)?\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?(?:Z|[+-]\d{2}:\d{2})?")]
    private static partial Regex Timestamp();

    private static IEnumerable<Step> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) yield break;
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); } catch (JsonException) { yield break; }
        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array) yield break;
            foreach (var e in doc.RootElement.EnumerateArray())
                yield return new Step(Str(e, "NodeType") ?? "", Str(e, "Label") ?? "", Str(e, "InputValue"));
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string Money(decimal d) => "$" + d.ToString("0.00", CultureInfo.InvariantCulture);
}
