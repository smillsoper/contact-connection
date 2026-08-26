using System.Text.Json;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;

namespace ContactConnection.Api.Endpoints;

// ── WebhookEndpoint.MappingConfig JSON shape ─────────────────────────────────
// {
//   "canonicalType": "order_line",                          // order | order_line | call_record
//   "rootMatch": { "sourcePath": "orderLineId", "matchField": "Id" },
//   "itemsArray": null,                                       // set only for the "one payload
//                                                               // updates several child records"
//                                                               // shape (canonicalType "order" only)
//   "operation": { "name": "Ship", "params": { "trackingNumber": "trackingNumber" } },
//   "onNoMatch": "skip_and_log"                                // | "ignore"
// }
// itemsArray (when set):
// { "arrayPath": "items", "itemMatch": { "sourcePath": "sku", "matchField": "Sku" },
//   "onNoMatch": "skip_and_log", "onMultipleMatches": "skip_and_log" }  // "update_all" also valid
//
// When itemsArray is set, operation.params source paths resolve relative to each array item, not
// the root payload — mirroring how AddressResponseMappingEvaluator's multipleMatchesConfig
// resolves itemMappings[].from relative to each array element, not the root body.

internal sealed class WebhookMatchRule
{
    public string SourcePath { get; set; } = "";
    public string MatchField { get; set; } = "";
}

internal sealed class WebhookItemsArrayRule
{
    public string ArrayPath { get; set; } = "";
    public WebhookMatchRule ItemMatch { get; set; } = new();
    public string OnNoMatch { get; set; } = "skip_and_log";
    public string OnMultipleMatches { get; set; } = "skip_and_log";
}

internal sealed class WebhookOperationRule
{
    public string Name { get; set; } = "";
    public Dictionary<string, string> Params { get; set; } = new();
}

internal sealed class WebhookMappingConfig
{
    public string CanonicalType { get; set; } = "";
    public WebhookMatchRule RootMatch { get; set; } = new();
    public WebhookItemsArrayRule? ItemsArray { get; set; }
    public WebhookOperationRule Operation { get; set; } = new();
    public string OnNoMatch { get; set; } = "skip_and_log";
}

internal sealed record ItemProcessingResult(string ItemDescription, bool Applied, string? Error);

internal sealed record WebhookEvaluationResult(bool Applied, string OutcomeSummary, string? Error, List<ItemProcessingResult> ItemResults)
{
    public static WebhookEvaluationResult Ok(string summary) => new(true, summary, null, []);
    public static WebhookEvaluationResult Fail(string error) => new(false, error, error, []);
}

/// <summary>
/// Evaluates a WebhookEndpoint's MappingConfig against an inbound payload and dispatches to the
/// matched canonical domain object's own named mutation methods — never raw reflection. Dispatch
/// is a plain switch over canonicalType + operation.Name; 3 types × ≤3 operations doesn't warrant
/// a plugin abstraction, matching this codebase's existing style (see ApplyAuth's switch,
/// AddressResponseMappingEvaluator). See API_HARDENING_CHECKLIST.md Tier 2.
/// </summary>
internal static class CanonicalWebhookMappingEvaluator
{
    public static async Task<WebhookEvaluationResult> EvaluateAndDispatchAsync(
        WebhookEndpoint webhook, JsonElement body,
        IOrderRepository orderRepo, ICallRecordRepository callRecordRepo, ICustomFieldService customFieldService,
        Guid tenantId, CancellationToken ct)
    {
        WebhookMappingConfig? config;
        // Case-insensitive: the mapping config is authored/serialized by the frontend as
        // camelCase JSON, but this deserializes with JsonSerializer's own default options (not
        // ASP.NET Core's request-pipeline JsonOptions, which aren't in scope here) — those
        // default to case-sensitive matching against the PascalCase C# properties below, which
        // would silently fail to bind anything without this.
        try
        {
            config = JsonSerializer.Deserialize<WebhookMappingConfig>(
                webhook.MappingConfig, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return WebhookEvaluationResult.Fail("Webhook mapping config is not valid JSON."); }

        if (config is null || string.IsNullOrEmpty(config.CanonicalType))
            return WebhookEvaluationResult.Fail("Webhook has no mapping configured.");

        return config.CanonicalType switch
        {
            CanonicalWebhookType.OrderLine => await EvaluateOrderLineAsync(config, body, orderRepo, ct),
            CanonicalWebhookType.Order => config.ItemsArray is null
                ? await EvaluateOrderFlatAsync(config, body, orderRepo, ct)
                : await EvaluateOrderWithItemsAsync(config, body, orderRepo, ct),
            CanonicalWebhookType.CallRecord => await EvaluateCallRecordAsync(config, body, callRecordRepo, customFieldService, ct),
            _ => WebhookEvaluationResult.Fail($"Unknown canonical type '{config.CanonicalType}'."),
        };
    }

    // ── order_line (flat) — the direct generalization of the original fulfillment-tracking case ──

    private static async Task<WebhookEvaluationResult> EvaluateOrderLineAsync(
        WebhookMappingConfig config, JsonElement body, IOrderRepository orderRepo, CancellationToken ct)
    {
        var keyValue = Str(ResolvePath(body, config.RootMatch.SourcePath));
        if (string.IsNullOrEmpty(keyValue) || !Guid.TryParse(keyValue, out var lineId))
            return NoMatchResult(config.OnNoMatch, $"Root match path '{config.RootMatch.SourcePath}' did not resolve to a valid OrderLine id.");

        var order = await orderRepo.GetByLineIdAsync(lineId, ct);
        var line = order?.Lines.FirstOrDefault(l => l.Id == lineId);
        if (order is null || line is null)
            return NoMatchResult(config.OnNoMatch, $"No order line found for id '{lineId}'.");

        var applied = ApplyOrderLineOperation(line, config.Operation, body);
        if (!applied.Success) return WebhookEvaluationResult.Fail(applied.Error!);

        order.RefreshStatus();
        await orderRepo.SaveChangesAsync(ct);
        return WebhookEvaluationResult.Ok($"{config.Operation.Name} applied to order line {lineId}.");
    }

    // ── order (flat) — Order-level operations, no nested items ──────────────────────────────────

    private static async Task<WebhookEvaluationResult> EvaluateOrderFlatAsync(
        WebhookMappingConfig config, JsonElement body, IOrderRepository orderRepo, CancellationToken ct)
    {
        var order = await ResolveOrderAsync(config.RootMatch, body, orderRepo, ct);
        if (order is null)
            return NoMatchResult(config.OnNoMatch, $"No order found matching '{config.RootMatch.MatchField}' = '{Str(ResolvePath(body, config.RootMatch.SourcePath))}'.");

        if (!string.Equals(config.Operation.Name, "Cancel", StringComparison.OrdinalIgnoreCase))
            return WebhookEvaluationResult.Fail($"Operation '{config.Operation.Name}' is not valid for canonical type 'order' without an items array.");

        order.Cancel();
        await orderRepo.SaveChangesAsync(ct);
        return WebhookEvaluationResult.Ok($"Order {order.Id} cancelled.");
    }

    // ── order + itemsArray — parent Order matched once, each payload array item matched against
    // Order.Lines by a key (Sku or Id) and updated independently. The real-world case this exists
    // for: a fulfillment agency's payload carries multiple line-item status updates for one order. ──

    private static async Task<WebhookEvaluationResult> EvaluateOrderWithItemsAsync(
        WebhookMappingConfig config, JsonElement body, IOrderRepository orderRepo, CancellationToken ct)
    {
        var itemsRule = config.ItemsArray!;

        var order = await ResolveOrderAsync(config.RootMatch, body, orderRepo, ct);
        if (order is null)
            return NoMatchResult(config.OnNoMatch, $"No order found matching '{config.RootMatch.MatchField}' = '{Str(ResolvePath(body, config.RootMatch.SourcePath))}'.");

        var arrayEl = ResolvePath(body, itemsRule.ArrayPath);
        if (arrayEl is not JsonElement arr || arr.ValueKind != JsonValueKind.Array)
            return WebhookEvaluationResult.Fail($"Items array path '{itemsRule.ArrayPath}' did not resolve to an array.");

        var results = new List<ItemProcessingResult>();
        var anyApplied = false;

        foreach (var item in arr.EnumerateArray())
        {
            var keyValue = Str(ResolvePath(item, itemsRule.ItemMatch.SourcePath));
            var description = $"item[{itemsRule.ItemMatch.SourcePath}={keyValue}]";

            if (string.IsNullOrEmpty(keyValue))
            {
                results.Add(new ItemProcessingResult(description, false, "Item match source path did not resolve to a value."));
                continue;
            }

            var matches = itemsRule.ItemMatch.MatchField switch
            {
                "Sku" => order.Lines.Where(l => l.Sku == keyValue).ToList(),
                "Id" => Guid.TryParse(keyValue, out var lineGuid)
                    ? order.Lines.Where(l => l.Id == lineGuid).ToList()
                    : [],
                _ => [],
            };

            if (matches.Count == 0)
            {
                if (itemsRule.OnNoMatch == "ignore")
                {
                    results.Add(new ItemProcessingResult(description, false, null));
                }
                else
                {
                    results.Add(new ItemProcessingResult(description, false, $"No order line matched {itemsRule.ItemMatch.MatchField} '{keyValue}'."));
                }
                continue;
            }

            if (matches.Count > 1 && itemsRule.OnMultipleMatches != "update_all")
            {
                results.Add(new ItemProcessingResult(description, false,
                    $"{matches.Count} order lines matched {itemsRule.ItemMatch.MatchField} '{keyValue}' — ambiguous, no update applied."));
                continue;
            }

            var itemFailed = false;
            foreach (var line in matches)
            {
                var applied = ApplyOrderLineOperation(line, config.Operation, item);
                if (!applied.Success)
                {
                    results.Add(new ItemProcessingResult(description, false, applied.Error));
                    itemFailed = true;
                    break;
                }
            }
            if (!itemFailed)
            {
                anyApplied = true;
                results.Add(new ItemProcessingResult(description, true, null));
            }
        }

        if (anyApplied)
        {
            order.RefreshStatus();
            await orderRepo.SaveChangesAsync(ct);
        }

        var appliedCount = results.Count(r => r.Applied);
        var skippedCount = results.Count - appliedCount;
        var summary = $"{appliedCount} line(s) updated, {skippedCount} skipped.";
        return new WebhookEvaluationResult(anyApplied, summary, anyApplied ? null : "No line items were updated.", results);
    }

    private static async Task<Order?> ResolveOrderAsync(
        WebhookMatchRule rootMatch, JsonElement body, IOrderRepository orderRepo, CancellationToken ct)
    {
        var keyValue = Str(ResolvePath(body, rootMatch.SourcePath));
        if (string.IsNullOrEmpty(keyValue)) return null;

        return rootMatch.MatchField switch
        {
            "Id" => Guid.TryParse(keyValue, out var id) ? await orderRepo.GetByIdAsync(id, ct) : null,
            "CallRecordId" => Guid.TryParse(keyValue, out var crId) ? await orderRepo.GetByCallRecordIdAsync(crId, ct) : null,
            _ => null,
        };
    }

    private static (bool Success, string? Error) ApplyOrderLineOperation(OrderLine line, WebhookOperationRule operation, JsonElement contextForParams)
    {
        switch (operation.Name)
        {
            case "Ship":
                if (!operation.Params.TryGetValue("trackingNumber", out var trackingPath))
                    return (false, "Ship operation requires a 'trackingNumber' param mapping.");
                var trackingNumber = Str(ResolvePath(contextForParams, trackingPath));
                if (string.IsNullOrWhiteSpace(trackingNumber))
                    return (false, $"Tracking number path '{trackingPath}' did not resolve to a value.");
                line.Ship(trackingNumber);
                return (true, null);
            case "MarkDelivered":
                line.MarkDelivered();
                return (true, null);
            case "Cancel":
                line.Cancel();
                return (true, null);
            default:
                return (false, $"Operation '{operation.Name}' is not valid for canonical type 'order_line'.");
        }
    }

    // ── call_record — matches an existing record, sets a value via the existing custom-fields
    // system (ICustomFieldService). No new domain/repository code needed for this type. ─────────

    private static async Task<WebhookEvaluationResult> EvaluateCallRecordAsync(
        WebhookMappingConfig config, JsonElement body,
        ICallRecordRepository callRecordRepo, ICustomFieldService customFieldService, CancellationToken ct)
    {
        var keyValue = Str(ResolvePath(body, config.RootMatch.SourcePath));
        if (string.IsNullOrEmpty(keyValue))
            return NoMatchResult(config.OnNoMatch, $"Root match path '{config.RootMatch.SourcePath}' did not resolve to a value.");

        var callRecord = config.RootMatch.MatchField switch
        {
            "Id" => Guid.TryParse(keyValue, out var id) ? await callRecordRepo.GetByIdAsync(id, ct) : null,
            "ContactIdExternal" => await callRecordRepo.GetByContactIdExternalAsync(keyValue, ct),
            _ => null,
        };
        if (callRecord is null)
            return NoMatchResult(config.OnNoMatch, $"No call record found matching '{config.RootMatch.MatchField}' = '{keyValue}'.");

        if (!string.Equals(config.Operation.Name, "SetCustomField", StringComparison.OrdinalIgnoreCase))
            return WebhookEvaluationResult.Fail($"Operation '{config.Operation.Name}' is not valid for canonical type 'call_record'.");

        if (!config.Operation.Params.TryGetValue("definitionId", out var definitionIdStr) || !Guid.TryParse(definitionIdStr, out var definitionId))
            return WebhookEvaluationResult.Fail("SetCustomField operation requires a valid 'definitionId' param.");
        if (!config.Operation.Params.TryGetValue("valueSourcePath", out var valuePath))
            return WebhookEvaluationResult.Fail("SetCustomField operation requires a 'valueSourcePath' param.");

        var rawValue = Str(ResolvePath(body, valuePath));
        if (rawValue is null)
            return WebhookEvaluationResult.Fail($"Value path '{valuePath}' did not resolve to a value.");

        await customFieldService.SetValueAsync(callRecord.Id, definitionId, rawValue, ct);
        return WebhookEvaluationResult.Ok($"Custom field set on call record {callRecord.Id}.");
    }

    private static WebhookEvaluationResult NoMatchResult(string onNoMatch, string message) =>
        onNoMatch == "ignore" ? WebhookEvaluationResult.Ok($"Ignored: {message}") : WebhookEvaluationResult.Fail(message);

    // ── Shared JSON path helpers — same dot+bracket-index convention as
    // AddressResponseMappingEvaluator/ZipLookupResponseEvaluator/AutocompleteResponseEvaluator
    // (each of which keeps its own copy; this is a 4th, kept self-contained rather than risking a
    // shared-extraction touch to those three stable, already-tested classes). ────────────────────

    private static object? ResolvePath(JsonElement element, string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        JsonElement current = element;
        foreach (var segment in path.Split('.'))
        {
            var arrMatch = System.Text.RegularExpressions.Regex.Match(segment, @"^(\w*)\[(\d+)\]$");
            if (arrMatch.Success)
            {
                var key = arrMatch.Groups[1].Value;
                var idx = int.Parse(arrMatch.Groups[2].Value);
                if (!string.IsNullOrEmpty(key))
                {
                    if (!current.TryGetProperty(key, out current)) return null;
                }
                if (current.ValueKind != JsonValueKind.Array || idx >= current.GetArrayLength()) return null;
                current = current[idx];
            }
            else
            {
                if (!current.TryGetProperty(segment, out current)) return null;
            }
        }
        return current;
    }

    private static string? Str(object? resolved)
    {
        if (resolved is not JsonElement je) return resolved?.ToString();
        return je.ValueKind switch
        {
            JsonValueKind.String => je.GetString(),
            JsonValueKind.Number => je.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }
}
