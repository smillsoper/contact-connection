using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.FlowEngine.NodeHandlers;

/// <summary>
/// Handles "address" nodes — agent captures and validates a mailing address.
///
/// Node schema:
/// {
///   "type": "address",
///   "label": "Billing Address",
///   "outputVariable": "billing_address",
///   "allowInternational": false,
///   "showMiddleInitial": false,
///   "showCompany": false,
///   "requiredFields": ["firstName","lastName","address1","zip","city","state"],
///   "scriptContent": "&lt;p&gt;Collect billing address&lt;/p&gt;",
///   "fieldScripts": { "zip": "&lt;p&gt;Enter 5-digit ZIP code&lt;/p&gt;" },
///   "transitions": { "default": "node_003" }
/// }
///
/// agentInput is a JSON string matching AddressSubmission — the frontend serialises the
/// entire address form to JSON and sends it as inputValue.
///
/// Output object stored in FlowVars[outputVariable] — all properties accessible via
/// {{flow.billing_address.city}}, {{flow.billing_address.isPOBox}}, etc.:
///   firstName, middleInitial, lastName, company
///   address1Prefix, address1, address2Prefix, address2
///   formattedAddress1, formattedAddress2, fullAddress
///   city, state, zip, zip4, country
///   isPOBox       — true when prefix is "PO Box"/"PMB" or address text starts with a PO Box pattern
///   isCanada      — true when ZIP matches Canadian A1A 1A1 postal code format
///   isMilitary    — true when state is AA / AE / AP (APO/FPO)
///   isOutlyingUS  — true when state is PR / GU / VI / AS / MP / UM
///   isAKHI        — true when state is AK or HI
///   isForeign     — true when country is not US and not Canadian postal code
///   isVerified    — always false (address validation API pending — wire up after API designer)
/// </summary>
public partial class AddressNodeHandler(IVariableResolver resolver)
    : NodeHandlerBase(resolver), INodeHandler
{
    public string NodeType => "address";

    private static readonly HashSet<string> MilitaryStates  = new(StringComparer.OrdinalIgnoreCase) { "AA", "AE", "AP" };
    private static readonly HashSet<string> TerritoryStates = new(StringComparer.OrdinalIgnoreCase) { "PR", "GU", "VI", "AS", "MP", "UM" };
    private static readonly HashSet<string> AkHiStates      = new(StringComparer.OrdinalIgnoreCase) { "AK", "HI" };

    // Prefixes that identify a PO Box when they appear at the start of the address1 text
    private static readonly string[] PoBoxTextPrefixes =
        ["PO BOX ", "P.O. BOX ", "PMB ", "BOX "];

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    [GeneratedRegex(@"^[A-Za-z]\d[A-Za-z]\d[A-Za-z]\d$")]
    private static partial Regex CanadaPostalCodeRegex();

    public Task<NodeResult> ExecuteAsync(
        JsonObject node, FlowExecutionContext ctx,
        string? agentInput, string agentTransition, CancellationToken ct = default)
    {
        var outputVar          = Str(node, "outputVariable")?.Trim() ?? string.Empty;
        var allowInternational = node["allowInternational"]?.GetValue<bool>() ?? false;
        var showMiddleInitial  = node["showMiddleInitial"]?.GetValue<bool>()  ?? false;
        var showCompany        = node["showCompany"]?.GetValue<bool>()        ?? false;
        var useValidation      = node["useValidation"]?.GetValue<bool>()      ?? true;
        var requiredFields     = ParseStringList(node, "requiredFields");
        var varCtx             = ctx.ToVariableContext();

        FlowNodeState MakeState()
        {
            var s = BuildState(ctx, node, resolvedContent: string.Empty);
            s.UseValidation      = useValidation;
            s.AllowInternational = allowInternational;
            s.ShowMiddleInitial  = showMiddleInitial;
            s.ShowCompany        = showCompany;
            s.RequiredFields     = requiredFields;
            s.FieldScripts       = ResolveFieldScripts(node, varCtx);

            // Inline node script (label + rich-text content shown above the form)
            var scriptLabel   = Str(node, "scriptLabel");
            var scriptContent = Str(node, "scriptContent");
            if (!string.IsNullOrWhiteSpace(scriptLabel))
                s.NodeScriptLabel = Resolver.Resolve(scriptLabel, varCtx);
            if (!string.IsNullOrWhiteSpace(scriptContent))
                s.NodeScriptContent = Resolver.Resolve(scriptContent, varCtx);

            // Pre-populate form: prefer FlowVars (normalized, has computed fields stripped)
            // then fall back to the raw last submission stored in execution history.
            string? existingJson = null;
            if (!string.IsNullOrEmpty(outputVar) && ctx.FlowVars.TryGetValue(outputVar, out var fv))
                existingJson = fv;
            if (string.IsNullOrEmpty(existingJson))
                existingJson = ctx.ExecutionHistory.LastOrDefault(h => h.NodeId == ctx.CurrentNodeId)?.InputValue;

            if (!string.IsNullOrEmpty(existingJson))
            {
                try
                {
                    var pre = JsonSerializer.Deserialize<AddressSubmission>(existingJson, JsonOpts);
                    if (pre is not null)
                    {
                        s.PrefilledAddress = new Dictionary<string, string>
                        {
                            ["firstName"]      = pre.FirstName      ?? string.Empty,
                            ["middleInitial"]  = pre.MiddleInitial  ?? string.Empty,
                            ["lastName"]       = pre.LastName       ?? string.Empty,
                            ["company"]        = pre.Company        ?? string.Empty,
                            ["address1Prefix"] = pre.Address1Prefix ?? string.Empty,
                            ["address1"]       = pre.Address1       ?? string.Empty,
                            ["address2Prefix"] = pre.Address2Prefix ?? string.Empty,
                            ["address2"]       = pre.Address2       ?? string.Empty,
                            ["zip"]            = pre.Zip            ?? string.Empty,
                            ["zip4"]           = pre.Zip4           ?? string.Empty,
                            ["city"]           = pre.City           ?? string.Empty,
                            ["state"]          = pre.State          ?? string.Empty,
                            ["country"]        = pre.Country        ?? "US",
                        };
                    }
                }
                catch { /* ignore malformed data — form starts empty */ }
            }

            return s;
        }

        // First display — return form config, wait for submission
        if (agentInput is null)
            return Task.FromResult(new NodeResult(MakeState(), NextNodeId: null));

        // Parse address JSON submitted by the frontend
        AddressSubmission? sub;
        try   { sub = JsonSerializer.Deserialize<AddressSubmission>(agentInput, JsonOpts); }
        catch { sub = null; }

        if (sub is null)
        {
            var errState = MakeState();
            errState.ValidationError = "Invalid address submission. Please try again.";
            return Task.FromResult(new NodeResult(errState, NextNodeId: null));
        }

        // Required field validation
        var errors = new List<string>();
        if (Contains(requiredFields, "firstName")  && IsBlank(sub.FirstName))     errors.Add("First name is required.");
        if (Contains(requiredFields, "lastName")   && IsBlank(sub.LastName))      errors.Add("Last name is required.");
        if (Contains(requiredFields, "company")    && IsBlank(sub.Company))       errors.Add("Company name is required.");
        if (Contains(requiredFields, "address1")   && IsBlank(sub.Address1))      errors.Add("Address line 1 is required.");
        if (Contains(requiredFields, "zip")        && IsBlank(sub.Zip))           errors.Add("ZIP code is required.");
        if (Contains(requiredFields, "city")       && IsBlank(sub.City))          errors.Add("City is required.");
        if (Contains(requiredFields, "state")      && IsBlank(sub.State))         errors.Add("State is required.");
        if (Contains(requiredFields, "country")    && IsBlank(sub.Country))       errors.Add("Country is required.");

        if (errors.Count > 0)
        {
            var errState = MakeState();
            errState.ValidationError = string.Join(" ", errors);
            return Task.FromResult(new NodeResult(errState, NextNodeId: null));
        }

        if (!string.IsNullOrEmpty(outputVar))
            ctx.FlowVars[outputVar] = BuildAddressObject(sub).ToJsonString();

        var next = Transition(node, agentTransition) ?? Transition(node, "default");
        AppendHistory(ctx, node, agentInput, next);
        return Task.FromResult(new NodeResult(MakeState(), next));
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static List<string> ParseStringList(JsonObject node, string key)
    {
        if (node[key] is not JsonArray arr) return [];
        return [.. arr.Select(x => x?.GetValue<string>() ?? string.Empty).Where(s => s.Length > 0)];
    }

    private Dictionary<string, string> ResolveFieldScripts(JsonObject node, VariableContext varCtx)
    {
        var result = new Dictionary<string, string>();
        if (node["fieldScripts"] is not JsonObject fs) return result;
        foreach (var kv in fs)
        {
            var raw = kv.Value?.GetValue<string>() ?? string.Empty;
            result[kv.Key] = string.IsNullOrWhiteSpace(raw) ? raw : Resolver.Resolve(raw, varCtx);
        }
        return result;
    }

    private static bool Contains(List<string> list, string key) =>
        list.Contains(key, StringComparer.OrdinalIgnoreCase);

    private static bool IsBlank(string? s) => string.IsNullOrWhiteSpace(s);

    private static string T(string? s) => s?.Trim() ?? string.Empty;

    // ── Address object builder ─────────────────────────────────────────────

    private static JsonObject BuildAddressObject(AddressSubmission s)
    {
        var firstName   = T(s.FirstName);
        var mi          = T(s.MiddleInitial);
        var lastName    = T(s.LastName);
        var company     = T(s.Company);
        var addr1Prefix = T(s.Address1Prefix);
        var addr1       = T(s.Address1);
        var addr2Prefix = T(s.Address2Prefix);
        var addr2       = T(s.Address2);
        var city        = T(s.City);
        var state       = T(s.State).ToUpperInvariant();
        var zip         = T(s.Zip).ToUpperInvariant();
        var zip4        = T(s.Zip4);
        var country     = T(s.Country).ToUpperInvariant();
        if (country.Length == 0) country = "US";

        var fmtAddr1 = addr1Prefix.Length > 0 ? $"{addr1Prefix} {addr1}" : addr1;
        var fmtAddr2 = addr2Prefix.Length > 0 ? $"{addr2Prefix} {addr2}" : addr2;

        // Build full single-line address
        var lines = new List<string>();
        if (fmtAddr1.Length > 0) lines.Add(fmtAddr1);
        if (fmtAddr2.Length > 0) lines.Add(fmtAddr2);
        var cityStateZip = BuildCityStateZip(city, state, zip, zip4);
        if (cityStateZip.Length > 0) lines.Add(cityStateZip);
        if (country.Length > 0 && country != "US") lines.Add(country);
        var fullAddress = string.Join(", ", lines);

        var isPOBox    = IsPOBox(addr1Prefix, addr1);
        var isCanada   = IsCanadianPostalCode(zip);
        var isMilitary = MilitaryStates.Contains(state);
        var isOutlying = TerritoryStates.Contains(state);
        var isAKHI     = AkHiStates.Contains(state);
        var isForeign  = country.Length > 0 && country != "US" && !isCanada;

        return new JsonObject
        {
            ["firstName"]         = firstName,
            ["middleInitial"]     = mi,
            ["lastName"]          = lastName,
            ["company"]           = company,
            ["address1Prefix"]    = addr1Prefix,
            ["address1"]          = addr1,
            ["address2Prefix"]    = addr2Prefix,
            ["address2"]          = addr2,
            ["formattedAddress1"] = fmtAddr1,
            ["formattedAddress2"] = fmtAddr2,
            ["fullAddress"]       = fullAddress,
            ["city"]              = city,
            ["state"]             = state,
            ["zip"]               = zip,
            ["zip4"]              = zip4,
            ["country"]           = country,
            ["isPOBox"]           = isPOBox,
            ["isCanada"]          = isCanada,
            ["isMilitary"]        = isMilitary,
            ["isOutlyingUS"]      = isOutlying,
            ["isForeign"]         = isForeign,
            ["isAKHI"]            = isAKHI,
            ["isVerified"]        = false,  // address validation API pending
        };
    }

    private static string BuildCityStateZip(string city, string state, string zip, string zip4)
    {
        var zipFull  = zip4.Length > 0 ? $"{zip}-{zip4}" : zip;
        var stateZip = string.Join(" ", new[] { state, zipFull }.Where(s => s.Length > 0));
        if (city.Length > 0 && stateZip.Length > 0) return $"{city}, {stateZip}";
        if (city.Length > 0)      return city;
        if (stateZip.Length > 0)  return stateZip;
        return string.Empty;
    }

    private static bool IsPOBox(string prefix, string addr1)
    {
        if (string.Equals(prefix, "PO Box", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(prefix, "PMB",    StringComparison.OrdinalIgnoreCase))
            return true;
        var upper = addr1.ToUpperInvariant();
        return PoBoxTextPrefixes.Any(p => upper.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsCanadianPostalCode(string zip) =>
        CanadaPostalCodeRegex().IsMatch(zip.Replace(" ", string.Empty));
}

/// <summary>Address form values submitted by the frontend as a JSON string.</summary>
public class AddressSubmission
{
    public string? FirstName      { get; set; }
    public string? MiddleInitial  { get; set; }
    public string? LastName       { get; set; }
    public string? Company        { get; set; }
    public string? Address1Prefix { get; set; }
    public string? Address1       { get; set; }
    public string? Address2Prefix { get; set; }
    public string? Address2       { get; set; }
    public string? City           { get; set; }
    public string? State          { get; set; }
    public string? Zip            { get; set; }
    public string? Zip4           { get; set; }
    public string? Country        { get; set; }
}
