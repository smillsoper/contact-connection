using System.Globalization;
using System.Text;
using System.Text.Json;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Commerce;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Ai;

/// <summary>
/// Builds the "context" for AI call summarization (AI step 1, S171) — the text the model will read.
/// The model has no access to the platform and no memory: this text is everything it knows about the
/// call, so what goes in here decides the quality (and the safety) of the summary.
///
/// What goes in — the call's facts, in plain labelled text:
/// <list type="bullet">
/// <item>campaign, how the call ended, handle time, whether an order was placed and what was in the cart;</item>
/// <item>the call's custom fields (call type, disposition, …);</item>
/// <item>the script as it was worked — each section, each question with the agent's answer, each order or
/// payment step with its result. This is our stand-in for a transcript until recordings are transcribed.</item>
/// </list>
/// What stays out (data minimization): caller name, phone, email and address are withheld entirely; card
/// and payment-capture steps show only that they happened; everything else is scrubbed by
/// <see cref="AiRedactor"/>. The secure-collect card blob on the call record is never read here at all.
/// </summary>
public static class CallSummaryContextBuilder
{
    public record Result(string Text, Dictionary<string, int> Redactions, int ScriptSteps);

    // Steps whose captured value is personal data — show the step happened, never the value.
    private static readonly Dictionary<string, string> WithheldNodeTypes = new()
    {
        ["address"] = "[address captured — withheld]",
        ["phone"] = "[phone captured — withheld]",
        ["email"] = "[email captured — withheld]",
        ["tf_secure_collect"] = "[payment details captured securely — withheld]",
    };

    // Script plumbing the model doesn't need (it would only add noise and tokens).
    private static readonly HashSet<string> SkippedNodeTypes =
        ["set_variable", "set_custom_field", "branch", "start", "delay", "set_shared_variable"];

    private record Step(string NodeType, string Label, string? InputValue, string? TransitionTaken);

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
        var sb = new StringBuilder();

        sb.AppendLine("CALL FACTS");
        sb.AppendLine($"Client / campaign: {client ?? "unknown"} / {campaign ?? "unknown"}");
        sb.AppendLine($"Direction: {record.Source}");
        if (record.HandleTimeSeconds is { } secs) sb.AppendLine($"Handle time: {secs / 60}m {secs % 60}s");
        sb.AppendLine($"Call status: {record.OverallStatus}");
        sb.AppendLine($"Order placed: {(record.OrderSubmittedAt is null ? "no" : "yes")}");
        if (!string.IsNullOrWhiteSpace(record.PaymentStatus)) sb.AppendLine($"Payment status: {record.PaymentStatus}");
        if (record.RoutedTierLabel is { } tier) sb.AppendLine($"Routed through tier: {tier}");

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
            foreach (var (name, value) in fields) sb.AppendLine($"- {name}: {r.Scrub(value)}");
        }

        var steps = 0;
        sb.AppendLine();
        sb.AppendLine("SCRIPT AS WORKED (each step in order; \"→\" is the agent's answer or the step's result)");
        foreach (var json in executionHistories)
        {
            foreach (var step in Parse(json))
            {
                if (SkippedNodeTypes.Contains(step.NodeType)) continue;
                var line = FormatStep(step, r);
                if (line is null) continue;
                sb.AppendLine(line);
                steps++;
            }
        }
        if (steps == 0) sb.AppendLine("(no script steps recorded)");

        return new Result(sb.ToString().TrimEnd(), r.Counts, steps);
    }

    private static string? FormatStep(Step s, AiRedactor r)
    {
        var label = r.Scrub(s.Label);
        if (s.NodeType == "section") return $"== {label} ==";
        if (WithheldNodeTypes.TryGetValue(s.NodeType, out var placeholder))
            return $"- {label} → {r.Withhold("personal details", placeholder)}";
        // The history records which step came next, not the result — so only say the step was reached;
        // the real outcome is in CALL FACTS (order placed, payment status).
        if (s.NodeType is "authorize_payment" or "void_payment" or "api_call") return $"- {label} (step reached)";

        if (s.InputValue is { } value && !string.IsNullOrWhiteSpace(value))
        {
            // A question whose answer is the caller's name: withhold it (the summary doesn't need it).
            if (s.Label.Contains("name", StringComparison.OrdinalIgnoreCase) && s.NodeType == "input")
                return $"- {label} → {r.Withhold("names", "[name withheld]")}";
            return $"- {label} → {r.Scrub(value.Trim())}";
        }
        return s.NodeType is "input" or "script" or "end" ? $"- {label}" : null;
    }

    private static IEnumerable<Step> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) yield break;
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); } catch (JsonException) { yield break; }
        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array) yield break;
            foreach (var e in doc.RootElement.EnumerateArray())
                yield return new Step(Str(e, "NodeType") ?? "", Str(e, "Label") ?? "", Str(e, "InputValue"), Str(e, "TransitionTaken"));
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string Money(decimal d) => "$" + d.ToString("0.00", CultureInfo.InvariantCulture);
}
