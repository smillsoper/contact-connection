using System.Text.Json;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;

namespace ContactConnection.Api.Endpoints;

public static class FlowSessionsEndpoints
{
    public static void MapFlowSessionsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/flow-sessions").RequireAuthorization();

        // Start a new flow session against a call record + interaction
        group.MapPost("/", async (
            StartSessionRequest req,
            IFlowEngine engine,
            TenantContext tenantContext,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (tenantContext.Current is null) return Results.Unauthorized();

            var agentIdClaim = http.User.FindFirst("sub")?.Value;
            if (!Guid.TryParse(agentIdClaim, out var agentId))
                return Results.Unauthorized();

            try
            {
                var state = await engine.StartAsync(new StartFlowRequest
                {
                    FlowId        = req.FlowId,
                    CallRecordId  = req.CallRecordId,
                    InteractionId = req.InteractionId,
                    AgentId       = agentId,
                    TenantId      = tenantContext.Current.Id
                }, ct);

                return Results.Ok(state);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // Get current node state for an active session (reconnect / refresh)
        group.MapGet("/{id:guid}", async (
            Guid id,
            IFlowEngine engine,
            CancellationToken ct) =>
        {
            var state = await engine.GetCurrentStateAsync(id, ct);
            return state is null ? Results.NotFound() : Results.Ok(state);
        });

        // Validate an address via the tenant's address_validation API definition
        group.MapPost("/{id:guid}/validate-address", ValidateAddress);

        // Advance the session — submit agent input and get next node state
        group.MapPost("/{id:guid}/advance", async (
            Guid id,
            AdvanceSessionRequest req,
            IFlowEngine engine,
            CancellationToken ct) =>
        {
            try
            {
                var state = await engine.AdvanceAsync(new AdvanceFlowRequest
                {
                    SessionId            = id,
                    InputValue           = req.InputValue,
                    Transition           = req.Transition ?? "default",
                    JumpToSectionNodeId  = req.JumpToSectionNodeId,
                }, ct);

                return Results.Ok(state);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
    }

    private static async Task<IResult> ValidateAddress(
        Guid id,
        ValidateAddressRequest req,
        ITenantApiDefinitionRepository tenantDefRepo,
        ITenantApiEndpointRepository tenantEndpointRepo,
        ITenantApiPreferenceRepository tenantPrefRepo,
        ITenantCredentialStore tenantCredStore,
        IPortalApiDefinitionRepository portalDefRepo,
        IPortalApiEndpointRepository portalEndpointRepo,
        IPortalCredentialStore portalCredStore,
        IHttpClientFactory httpFactory,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (tenantContext.Current is null) return Results.Unauthorized();

        // Resolution order: tenant-preferred own endpoint → any tenant endpoint →
        //   tenant-chosen portal endpoint → portal-preferred endpoint → any portal endpoint
        string? baseUrl = null;
        string? authConfig = null;
        string? endpointPath = null;
        string? httpMethod = null;
        string? queryParams = null;
        string? headers = null;
        string? requestBodyTemplate = null;
        string? responseMapping = null;
        Func<string, CancellationToken, Task<string?>>? getCredential = null;

        var tenantEp = await tenantEndpointRepo.GetPreferredBySubTypeAsync(ApiSubType.AddressValidation, ct)
                       ?? (await tenantEndpointRepo.GetBySubTypeAsync(ApiSubType.AddressValidation, ct)).FirstOrDefault();
        if (tenantEp is not null)
        {
            var tenantDef = await tenantDefRepo.GetByIdAsync(tenantEp.DefinitionId, ct);
            if (tenantDef is not null && tenantDef.IsActive)
            {
                baseUrl             = tenantDef.BaseUrl;
                authConfig          = tenantDef.AuthConfig;
                endpointPath        = tenantEp.Path;
                httpMethod          = tenantEp.HttpMethod ?? tenantDef.HttpMethod;
                queryParams         = tenantEp.QueryParams;
                headers             = tenantEp.Headers;
                requestBodyTemplate = tenantEp.RequestBodyTemplate;
                responseMapping     = tenantEp.ResponseMapping;
                getCredential       = (key, token) => tenantCredStore.GetAsync(key, token);
            }
        }

        // Fall back to portal-level: tenant preference → platform preferred → any active
        if (getCredential is null)
        {
            PortalApiEndpoint? portalEp = null;
            var tenantPref = await tenantPrefRepo.GetBySubTypeAsync(ApiSubType.AddressValidation, ct);
            if (tenantPref is not null)
                portalEp = await portalEndpointRepo.GetByIdAsync(tenantPref.PortalApiEndpointId, ct);
            portalEp ??= await portalEndpointRepo.GetPreferredBySubTypeAsync(ApiSubType.AddressValidation, ct)
                         ?? (await portalEndpointRepo.GetBySubTypeAsync(ApiSubType.AddressValidation, ct)).FirstOrDefault();

            if (portalEp is not null)
            {
                var portalDef = await portalDefRepo.GetByIdAsync(portalEp.DefinitionId, ct);
                if (portalDef is not null && portalDef.IsActive)
                {
                    baseUrl             = portalDef.BaseUrl;
                    authConfig          = portalDef.AuthConfig;
                    endpointPath        = portalEp.Path;
                    httpMethod          = portalEp.HttpMethod ?? portalDef.HttpMethod;
                    queryParams         = portalEp.QueryParams;
                    headers             = portalEp.Headers;
                    requestBodyTemplate = portalEp.RequestBodyTemplate;
                    responseMapping     = portalEp.ResponseMapping;
                    getCredential       = (key, token) => portalCredStore.GetAsync(key, token);
                }
            }
        }

        if (getCredential is null)
            return Results.Ok(new AddressValidationResult("error", "No Address Validation API", "No active address validation API is configured at the tenant or platform level.", null, null));

        // Build address data as namespace variables (namespace = "address" by convention).
        // Use case-insensitive comparer so templates with {{address.Address1}} or {{address.address1}} both resolve.
        // Also compute combined fields the template might reference (formattedAddress1, streetAddress, fullName, etc.)
        var addrData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in req.Address ?? new())
            addrData[kv.Key] = kv.Value;

        // Pre-compute combined variants that API templates commonly use
        var addr1Prefix = addrData.TryGetValue("address1Prefix", out var p1) ? p1 : "";
        var addr1       = addrData.TryGetValue("address1", out var a1) ? a1 : "";
        var addr2Prefix = addrData.TryGetValue("address2Prefix", out var p2) ? p2 : "";
        var addr2       = addrData.TryGetValue("address2", out var a2) ? a2 : "";
        var firstName   = addrData.TryGetValue("firstName", out var fn) ? fn : "";
        var lastName    = addrData.TryGetValue("lastName", out var ln) ? ln : "";
        var mi          = addrData.TryGetValue("middleInitial", out var miVal) ? miVal : "";

        addrData["formattedAddress1"] = addr1Prefix.Length > 0 ? $"{addr1Prefix} {addr1}" : addr1;
        addrData["formattedAddress2"] = addr2Prefix.Length > 0 ? $"{addr2Prefix} {addr2}" : addr2;
        addrData["streetAddress"]     = addrData["formattedAddress1"];
        addrData["address"]           = addrData["formattedAddress1"];
        addrData["fullName"]          = string.Join(" ", new[] { firstName, mi, lastName }.Where(s => s.Length > 0));
        addrData["name"]              = addrData["fullName"];
        // Override raw address1/address2 with the formatted (prefix-combined) values so
        // templates using {{address.address1}} behave identically to the source test tab,
        // where Address Line 1 is a single combined field with no separate prefix.
        addrData["address1"] = addrData["formattedAddress1"];
        addrData["address2"] = addrData["formattedAddress2"];

        var testReq = new RunEndpointTestRequest(
            Path: endpointPath!,
            HttpMethod: httpMethod,
            QueryParams: queryParams,
            Headers: headers,
            RequestBodyTemplate: requestBodyTemplate,
            Namespace: "address",
            TestData: addrData);

        var response = await ApiEndpointTestHelper.RunTestAsync(
            baseUrl!, authConfig!, testReq, getCredential, httpFactory, ct);

        if (response.Error is not null)
            return Results.Ok(new AddressValidationResult("error", "API Error", response.Error, null, null));

        // Parse response body
        JsonElement body;
        try { body = JsonDocument.Parse(response.Body ?? "{}").RootElement; }
        catch { return Results.Ok(new AddressValidationResult("error", "Invalid Response", "The API returned a non-JSON response.", null, null)); }

        // Evaluate response mapping outcomes against the endpoint's response mapping config
        var result = AddressResponseMappingEvaluator.Evaluate(responseMapping!, body);
        return Results.Ok(result);
    }
}

public record StartSessionRequest(
    Guid FlowId,
    Guid CallRecordId,
    Guid InteractionId);

public record AdvanceSessionRequest(
    string? InputValue,
    string? Transition,
    string? JumpToSectionNodeId);

public record ValidateAddressRequest(
    Dictionary<string, string>? Address);

public record AddressValidationResult(
    string OutcomeKey,
    string OutcomeLabel,
    string? Message,
    Dictionary<string, string>? CorrectedFields,
    List<Dictionary<string, string>>? Matches);

/// <summary>
/// Evaluates a response mapping JSON config against an actual API response body
/// and returns an AddressValidationResult describing the matched outcome.
/// </summary>
internal static class AddressResponseMappingEvaluator
{
    public static AddressValidationResult Evaluate(string responseMappingJson, JsonElement body)
    {
        try
        {
            var config = JsonDocument.Parse(responseMappingJson).RootElement;
            if (!config.TryGetProperty("outcomes", out var outcomesEl) || outcomesEl.ValueKind != JsonValueKind.Array)
                return Unrecognized();

            foreach (var outcome in outcomesEl.EnumerateArray())
            {
                var name  = Str(outcome, "name");
                var label = Str(outcome, "label");

                bool matched;
                if (outcome.TryGetProperty("conditions", out var conditionsEl) && conditionsEl.ValueKind == JsonValueKind.Array)
                {
                    var logic = Str(outcome, "conditionLogic");
                    var isOr  = string.Equals(logic, "or", StringComparison.OrdinalIgnoreCase);
                    var conds = conditionsEl.EnumerateArray().ToList();
                    matched = conds.Count > 0 && (isOr
                        ? conds.Any(c => EvaluateCondition(c, body))
                        : conds.All(c => EvaluateCondition(c, body)));
                }
                else if (outcome.TryGetProperty("condition", out var condEl) && condEl.ValueKind != JsonValueKind.Undefined)
                {
                    matched = EvaluateCondition(condEl, body);
                }
                else
                {
                    matched = false;
                }

                if (!matched) continue;

                var messagePath = Str(outcome, "messagePath");
                var message     = string.IsNullOrEmpty(messagePath) ? null : ResolveMessage(body, messagePath);

                // Resolve field mappings → corrected address fields
                Dictionary<string, string>? correctedFields = null;
                if (outcome.TryGetProperty("fieldMappings", out var fmEl) && fmEl.ValueKind == JsonValueKind.Array)
                {
                    correctedFields = new();
                    foreach (var fm in fmEl.EnumerateArray())
                    {
                        var source = Str(fm, "from");
                        var target = Str(fm, "to");
                        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target)) continue;
                        // Strip "address." namespace prefix so targets like "address.address1"
                        // become plain form field keys like "address1" the frontend can merge directly.
                        var dot = target.IndexOf('.');
                        if (dot >= 0) target = target[(dot + 1)..];
                        if (source.Contains("{{"))
                        {
                            correctedFields[target] = ResolveTemplate(body, source);
                        }
                        else
                        {
                            var val = ResolvePath(body, source);
                            if (val is not null) correctedFields[target] = Normalize(val) ?? string.Empty;
                        }
                    }
                    // Always parse prefix/remainder for address1 and address2 regardless of how they were
                    // mapped — ParseAddress extracts the prefix type (PO Box, Apt, etc.) into address1Prefix/
                    // address2Prefix which the form's prefix dropdown depends on and is not an explicit mapping target.
                    if (correctedFields.TryGetValue("address1", out var a1Full))
                    {
                        var (pfx1, rem1) = ParseAddress1(a1Full);
                        correctedFields["address1Prefix"] = pfx1;
                        correctedFields["address1"]       = rem1;
                    }
                    if (correctedFields.TryGetValue("address2", out var a2Full))
                    {
                        var (pfx2, rem2) = ParseAddress2(a2Full);
                        correctedFields["address2Prefix"] = pfx2;
                        correctedFields["address2"]       = rem2;
                    }
                    if (correctedFields.Count == 0) correctedFields = null;
                }

                // Resolve multiple matches array
                List<Dictionary<string, string>>? matches = null;
                if (outcome.TryGetProperty("multipleMatchesConfig", out var mmcEl) && mmcEl.ValueKind == JsonValueKind.Object)
                {
                    var arrayPath = Str(mmcEl, "arrayPath");
                    if (!string.IsNullOrEmpty(arrayPath))
                    {
                        var arrayEl = ResolvePath(body, arrayPath);
                        if (arrayEl is JsonElement ae && ae.ValueKind == JsonValueKind.Array &&
                            mmcEl.TryGetProperty("itemMappings", out var imEl) && imEl.ValueKind == JsonValueKind.Array)
                        {
                            matches = new();
                            foreach (var item in ae.EnumerateArray())
                            {
                                var itemDict = new Dictionary<string, string>();
                                foreach (var im in imEl.EnumerateArray())
                                {
                                    var src = Str(im, "from");
                                    var tgt = Str(im, "to");
                                    if (string.IsNullOrEmpty(src) || string.IsNullOrEmpty(tgt)) continue;
                                    var dot = tgt.IndexOf('.');
                                    if (dot >= 0) tgt = tgt[(dot + 1)..];
                                    if (src.Contains("{{"))
                                    {
                                        itemDict[tgt] = ResolveTemplate(item, src);
                                    }
                                    else
                                    {
                                        var val = ResolvePath(item, src);
                                        if (val is not null) itemDict[tgt] = Normalize(val) ?? string.Empty;
                                    }
                                }
                                if (itemDict.TryGetValue("address1", out var im1Full))
                                {
                                    var (pfx1, rem1) = ParseAddress1(im1Full);
                                    itemDict["address1Prefix"] = pfx1;
                                    itemDict["address1"]       = rem1;
                                }
                                if (itemDict.TryGetValue("address2", out var im2Full))
                                {
                                    var (pfx2, rem2) = ParseAddress2(im2Full);
                                    itemDict["address2Prefix"] = pfx2;
                                    itemDict["address2"]       = rem2;
                                }
                                matches.Add(itemDict);
                            }
                        }
                    }
                }

                return new AddressValidationResult(name, label, message, correctedFields, matches);
            }
        }
        catch (Exception ex)
        {
            return new AddressValidationResult("error", "Evaluator Error", ex.Message, null, null);
        }

        return Unrecognized();
    }

    private static AddressValidationResult Unrecognized() =>
        new("error", "Unrecognized Response", "The API response did not match any configured outcome.", null, null);

    private static bool EvaluateCondition(JsonElement condition, JsonElement body)
    {
        var op    = Str(condition, "op");
        var path  = Str(condition, "path");
        var value = Str(condition, "value");

        var resolved = string.IsNullOrEmpty(path) ? null : ResolvePath(body, path);
        var resolvedStr = Normalize(resolved);

        return op switch
        {
            "exists"       => resolved is not null,
            "not_exists"   => resolved is null,
            "eq"           => resolvedStr == value,
            "neq"          => resolvedStr != value,
            "contains"     => resolvedStr?.Contains(value, StringComparison.OrdinalIgnoreCase) == true,
            "not_contains" => resolvedStr?.Contains(value, StringComparison.OrdinalIgnoreCase) != true,
            "gt"           => double.TryParse(resolvedStr, out var a) && double.TryParse(value, out var b) && a > b,
            "lt"           => double.TryParse(resolvedStr, out var a2) && double.TryParse(value, out var b2) && a2 < b2,
            "gte"          => double.TryParse(resolvedStr, out var a3) && double.TryParse(value, out var b3) && a3 >= b3,
            "lte"          => double.TryParse(resolvedStr, out var a4) && double.TryParse(value, out var b4) && a4 <= b4,
            "length_eq"    => GetLength(resolved) == (int.TryParse(value, out var n) ? n : -1),
            "length_gt"    => GetLength(resolved) > (int.TryParse(value, out var n2) ? n2 : int.MaxValue),
            "length_lt"    => GetLength(resolved) < (int.TryParse(value, out var n3) ? n3 : int.MinValue),
            _              => false,
        };
    }

    // Converts a resolved JsonElement to a normalized string.
    // JsonElement.ToString() returns "True"/"False" for booleans (C# bool.TrueString),
    // which never matches stored condition values like "true"/"false".
    private static string? Normalize(object? resolved)
    {
        if (resolved is not JsonElement je) return resolved?.ToString();
        return je.ValueKind switch
        {
            JsonValueKind.True   => "true",
            JsonValueKind.False  => "false",
            JsonValueKind.String => je.GetString(),
            JsonValueKind.Number => je.GetRawText(),
            _                    => null,
        };
    }

    private static int GetLength(object? resolved)
    {
        if (resolved is JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.Array) return el.GetArrayLength();
            if (el.ValueKind == JsonValueKind.String) return el.GetString()?.Length ?? 0;
        }
        return resolved?.ToString()?.Length ?? 0;
    }

    /// Resolves a message path, supporting a [*] wildcard to collect all values from an array
    /// and join them with a space. E.g. "results.0.notices[*].message" joins every notice message.
    private static string? ResolveMessage(JsonElement body, string messagePath)
    {
        var wildcardIdx = messagePath.IndexOf("[*]", StringComparison.Ordinal);
        if (wildcardIdx >= 0)
        {
            var arrayPath  = messagePath[..wildcardIdx].TrimEnd('.');
            var fieldPath  = messagePath[(wildcardIdx + 3)..].TrimStart('.');
            var arrayEl    = ResolvePath(body, arrayPath);
            if (arrayEl is JsonElement ae && ae.ValueKind == JsonValueKind.Array)
            {
                var parts = ae.EnumerateArray()
                    .Select(item => string.IsNullOrEmpty(fieldPath)
                        ? item.ToString()
                        : ResolvePath(item, fieldPath)?.ToString())
                    .Where(m => !string.IsNullOrEmpty(m))
                    .ToList();
                return parts.Count > 0 ? string.Join("\n", parts) : null;
            }
            return null;
        }
        return ResolvePath(body, messagePath)?.ToString();
    }

    /// <summary>
    /// Resolves a template string containing {{path}} placeholders against a JsonElement.
    /// e.g. "{{number}} {{pre_directional}} {{street}}" → "111 SE RIFLE RANGE"
    /// </summary>
    private static string ResolveTemplate(JsonElement body, string template)
    {
        var result = System.Text.RegularExpressions.Regex.Replace(
            template,
            @"\{\{([^}]+)\}\}",
            m =>
            {
                var path     = m.Groups[1].Value.Trim();
                var resolved = ResolvePath(body, path);
                return Normalize(resolved) ?? string.Empty;
            }
        );
        // Collapse runs of whitespace left behind by empty/missing segments.
        return System.Text.RegularExpressions.Regex.Replace(result.Trim(), @"\s{2,}", " ");
    }

    /// <summary>
    /// Resolves a dot-notation path (e.g. "error.message" or "matches[0].city") against a JsonElement.
    /// Returns the resolved value as object (JsonElement or null).
    /// </summary>
    private static object? ResolvePath(JsonElement element, string path)
    {
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

    private static string Str(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v)) return "";
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True   => "true",
            JsonValueKind.False  => "false",
            _                    => ""
        };
    }

    // Address1 prefixes — longest/most-specific first to avoid partial matches.
    private static readonly (string Token, string Prefix)[] Addr1Prefixes =
    [
        ("P.O. BOX ",    "PO Box"),
        ("PO BOX ",      "PO Box"),
        ("RURAL ROUTE ", "RR"),
        ("STATE ROUTE ", "SR"),
        ("HIGHWAY ",     "Hwy"),
        ("BOX ",         "PO Box"),
        ("PMB ",         "PMB"),
        ("HWY ",         "Hwy"),
        ("RR ",          "RR"),
        ("SR ",          "SR"),
    ];

    private static readonly (string Token, string Prefix)[] Addr2Prefixes =
    [
        ("APARTMENT ", "Apt"),
        ("FLOOR ",     "Floor"),
        ("LEVEL ",     "Level"),
        ("SPACE ",     "Space"),
        ("UNIT ",      "Unit"),
        ("APT ",       "Apt"),
        ("STE ",       "Ste"),
        ("SUITE ",     "Ste"),
        ("FLR ",       "Floor"),
        ("LVL ",       "Level"),
        ("LOT ",       "Lot"),
        ("SPC ",       "Space"),
        ("FL ",        "Floor"),
        ("LV ",        "Level"),
        ("# ",         "Unit"),
    ];

    private static (string Prefix, string Address) ParseAddress1(string address)
    {
        var trimmed = address.Trim();
        var upper   = trimmed.ToUpperInvariant();

        // Standard case: prefix is at the start — "PO BOX 100"
        foreach (var (token, prefix) in Addr1Prefixes)
            if (upper.StartsWith(token)) return (prefix, trimmed[token.Length..].Trim());

        // Template-constructed case: number comes first — "100 PO BOX" (zip-codes.com puts
        // box number in address_components.number and "PO BOX" in address_components.street).
        // Search for the prefix token mid-string with whole-word boundary check so "BOXER ST"
        // does not accidentally match the "BOX" token.
        foreach (var (token, prefix) in Addr1Prefixes)
        {
            var needle   = " " + token.TrimEnd(); // e.g., " PO BOX"
            var idx      = upper.IndexOf(needle, StringComparison.Ordinal);
            if (idx <= 0) continue;
            var afterIdx = idx + needle.Length;
            // Accept only if the token ends at the end of the string or is followed by a space.
            if (afterIdx < upper.Length && upper[afterIdx] != ' ') continue;
            return (prefix, trimmed[..idx].Trim());
        }

        return ("", trimmed);
    }

    private static (string Prefix, string Address) ParseAddress2(string address)
    {
        var upper = address.ToUpperInvariant();
        foreach (var (token, prefix) in Addr2Prefixes)
            if (upper.StartsWith(token)) return (prefix, address[token.Length..].Trim());
        return ("", address);
    }
}
