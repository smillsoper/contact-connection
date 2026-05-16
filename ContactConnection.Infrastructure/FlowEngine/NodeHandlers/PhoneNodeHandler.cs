using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.FlowEngine.NodeHandlers;

/// <summary>
/// Handles "phone" nodes — agent captures a phone number with automatic formatting.
///
/// Node schema:
/// {
///   "type": "phone",
///   "label": "Customer Phone",
///   "outputVariable": "customer_phone",
///   "required": true,
///   "allowInternational": false,
///   "dncCheck": false,
///   "transitions": { "default": "node_002" }
/// }
///
/// Stores output as a JSON object in FlowVars under the outputVariable key.
/// Sub-properties resolved via {{flow.customer_phone.isTollFree}} etc.:
///   value           = digits only (domestic: 10 digits; international: cc digits + 10 local)
///   display_value   = formatted string as typed by agent
///   isMobile        = null (requires carrier lookup — see DNC/carrier integration backlog)
///   isTollFree      = true/false based on US toll-free area codes (800/833/844/855/866/877/888)
///   isInternal      = false (requires tenant configuration — future)
///   doNotCall       = false (placeholder — DNC registry integration pending)
///
/// Masks:
///   Domestic:      "(000) 000-0000"       — exactly 10 digits required
///   International: "+099 (000) 000-0000"  — 3 digits for country code (zero-padded) + 10 local
///                  Examples: 001=(+1 North America), 044=(+44 UK), 357=(+357 Cyprus)
///                  Leading zeros stripped for validation; stored value uses stripped code.
/// </summary>
public partial class PhoneNodeHandler(IVariableResolver resolver)
    : NodeHandlerBase(resolver), INodeHandler
{
    public string NodeType => "phone";

    private const string DomesticMask      = "(000) 000-0000";
    private const string InternationalMask = "+099 (000) 000-0000";

    private static readonly HashSet<string> TollFreeAreaCodes =
        ["800", "833", "844", "855", "866", "877", "888"];

    // Valid ITU-T E.164 country codes (1–3 digits, after stripping leading zeros)
    private static readonly HashSet<string> ValidCountryCodes = new(StringComparer.Ordinal)
    {
        // 1-digit
        "1", "7",
        // 2-digit
        "20","27","30","31","32","33","34","36","39",
        "40","41","43","44","45","46","47","48","49",
        "51","52","53","54","55","56","57","58",
        "60","61","62","63","64","65","66",
        "81","82","84","86",
        "90","91","92","93","94","95","98",
        // 3-digit (selected ITU-T allocations)
        "212","213","216","218",
        "220","221","222","223","224","225","226","227","228","229",
        "230","231","232","233","234","235","236","237","238","239",
        "240","241","242","243","244","245","246","247","248","249",
        "250","251","252","253","254","255","256","257","258",
        "260","261","262","263","264","265","266","267","268","269",
        "290","291","297","298","299",
        "350","351","352","353","354","355","356","357","358","359",
        "370","371","372","373","374","375","376","377","378",
        "380","381","382","385","386",
        "420","421","423",
        "500","501","502","503","504","505","506","507","508","509",
        "590","591","592","593","594","595","596","597","598","599",
        "670","672","673","674","675","676","677","678","679",
        "680","681","682","683","685","686","687","688","689",
        "850","852","853","855","856","880","886",
        "960","961","962","963","964","965","966","967","968",
        "970","971","972","973","974","975","976","977",
        "992","993","994","995","996","998",
    };

    [GeneratedRegex(@"[^\d]")]
    private static partial Regex NonDigits();

    private void AttachInlineScript(JsonObject node, FlowExecutionContext ctx, FlowNodeState state)
    {
        var varCtx = ctx.ToVariableContext();
        var label   = Str(node, "scriptLabel");
        var content = Str(node, "scriptContent");
        if (!string.IsNullOrWhiteSpace(label))
            state.NodeScriptLabel = Resolver.Resolve(label, varCtx);
        if (!string.IsNullOrWhiteSpace(content))
            state.NodeScriptContent = Resolver.Resolve(content, varCtx);
    }

    public Task<NodeResult> ExecuteAsync(
        JsonObject node, FlowExecutionContext ctx,
        string? agentInput, string agentTransition, CancellationToken ct = default)
    {
        var required           = node["required"]?.GetValue<bool>() ?? false;
        var allowInternational = node["allowInternational"]?.GetValue<bool>() ?? false;
        var outputVar          = Str(node, "outputVariable")?.Trim() ?? string.Empty;
        var mask               = allowInternational ? InternationalMask : DomesticMask;

        FlowNodeState MakeState()
        {
            var s = BuildState(ctx, node, resolvedContent: string.Empty, inputType: "text", required: required);
            s.InputMask = mask;
            return s;
        }

        FlowNodeState WithScript(FlowNodeState s) { AttachInlineScript(node, ctx, s); return s; }

        // First display — return state with mask, wait for input
        if (agentInput is null)
        {
            var firstState = WithScript(MakeState());
            if (!string.IsNullOrEmpty(outputVar) && ctx.FlowVars.TryGetValue(outputVar, out var existing))
            {
                try
                {
                    var obj = System.Text.Json.Nodes.JsonNode.Parse(existing)?.AsObject();
                    firstState.DefaultValue = obj?["display_value"]?.GetValue<string>();
                }
                catch { /* ignore malformed data */ }
            }
            return Task.FromResult(new NodeResult(firstState, NextNodeId: null));
        }

        var displayValue = agentInput.Trim();

        // Required guard — re-display if blank
        if (required && string.IsNullOrEmpty(displayValue))
            return Task.FromResult(new NodeResult(WithScript(MakeState()), NextNodeId: null));

        // Optional blank — store empty object and advance
        if (string.IsNullOrEmpty(displayValue))
        {
            if (!string.IsNullOrEmpty(outputVar))
                ctx.FlowVars[outputVar] = BuildPhoneObject(string.Empty, string.Empty, false).ToJsonString();

            var next = Transition(node, agentTransition) ?? Transition(node, "default");
            AppendHistory(ctx, node, displayValue, next);
            return Task.FromResult(new NodeResult(WithScript(MakeState()), next));
        }

        var digits = NonDigits().Replace(displayValue, string.Empty);

        // Validate digit count and country code
        string? validationError = null;
        string localDigits;
        string storedValue;

        if (allowInternational)
        {
            // International mask "+099 (000) 000-0000" → 3 cc digits + 10 local = 13 total
            if (digits.Length != 13)
            {
                validationError =
                    "Please enter a complete phone number including the 3-digit country code " +
                    "(e.g. 001 for North America, 044 for the UK, 357 for Cyprus).";
            }
            else
            {
                var paddedCc = digits[..3];
                var rawCc    = paddedCc.TrimStart('0');
                if (rawCc.Length == 0) rawCc = "0";

                if (!ValidCountryCodes.Contains(rawCc))
                    validationError = $"+{rawCc} is not a recognized country code.";
                else
                {
                    localDigits  = digits[3..];
                    storedValue  = rawCc + localDigits;
                    goto validated;
                }
            }
            localDigits = storedValue = string.Empty;
        }
        else
        {
            if (digits.Length != 10)
                validationError = "Please enter a complete 10-digit phone number.";
            localDigits = digits;
            storedValue = digits;
        }

        if (validationError is not null)
        {
            var errorState = WithScript(MakeState());
            errorState.ValidationError = validationError;
            return Task.FromResult(new NodeResult(errorState, NextNodeId: null));
        }

        validated:
        var areaCode   = localDigits.Length >= 3 ? localDigits[..3] : string.Empty;
        var isTollFree = TollFreeAreaCodes.Contains(areaCode);

        if (!string.IsNullOrEmpty(outputVar))
            ctx.FlowVars[outputVar] = BuildPhoneObject(storedValue, displayValue, isTollFree).ToJsonString();

        var advanceNext = Transition(node, agentTransition) ?? Transition(node, "default");
        AppendHistory(ctx, node, displayValue, advanceNext);
        return Task.FromResult(new NodeResult(WithScript(MakeState()), advanceNext));
    }

    private static JsonObject BuildPhoneObject(string value, string displayValue, bool isTollFree) => new()
    {
        ["value"]         = value,
        ["display_value"] = displayValue,
        ["isMobile"]      = (JsonNode?)null,  // requires carrier lookup API
        ["isTollFree"]    = isTollFree,
        ["isInternal"]    = false,
        ["doNotCall"]     = false,             // placeholder — DNC registry integration pending
    };
}
