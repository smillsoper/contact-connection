using System.Globalization;
using System.Text.Json.Nodes;

namespace ContactConnection.Infrastructure.FlowDocs;

/// <summary>Names the document shows in place of ids.</summary>
public record FlowDocLookups(
    IReadOnlyDictionary<Guid, string> FlowNames,
    IReadOnlyDictionary<Guid, string> AudioNames,
    IReadOnlyDictionary<Guid, string> CampaignNames,
    IReadOnlyDictionary<Guid, string> GroupNames);

/// <summary>
/// Reads one flow definition into a chapter: every step in reading order (main path first), grouped by section (CRM)
/// or by entry point (call flows: the main path and each event handler), numbered, described in plain English. Steps
/// nothing leads to are kept too, marked "not connected" — the document is an audit, so nothing goes missing.
/// </summary>
public static class FlowDocExtractor
{
    // Exits on the main path are followed first, so numbering reads like the call goes.
    private static readonly string[] MainFirst =
        ["default", "success", "approved", "added", "true", "yes", "Yes", "transferred", "collected", "answered", "available",
         "business_hours", "end_of_stream", "found", "valid", "connected", "booked"];
    private static readonly string[] SadLast =
        ["error", "failed", "timeout", "declined", "no_match", "false", "no", "No", "invalid", "not_found", "unavailable",
         "caller_hung_up", "hangup", "busy", "no_answer"];

    private static int Rank(string handle)
    {
        var i = Array.IndexOf(MainFirst, handle);
        if (i >= 0) return i;
        return Array.IndexOf(SadLast, handle) >= 0 ? 1000 + Array.IndexOf(SadLast, handle) : 500;
    }

    public static DocChapter Build(JsonObject definition, Guid flowId, string flowName, bool telephony, int version, bool draft,
        FlowDocLookups lookups)
    {
        var chapter = new DocChapter { FlowId = flowId, FlowName = flowName, IsTelephony = telephony, Version = version, IsDraft = draft };
        var nodes = definition["nodes"] as JsonObject ?? [];
        var entry = definition["entry_node"]?.GetValue<string>();

        // Variable names → the label of the step that captures them, for "[Billing Phone]" instead of "[billing phone]".
        var varLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var nodeLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, n) in nodes)
        {
            if (n is not JsonObject o) continue;
            var label = Str(o, "label");
            if (!string.IsNullOrWhiteSpace(label)) nodeLabels[id] = label!;
            var outVar = Str(o, "outputVariable");
            if (!string.IsNullOrWhiteSpace(outVar) && !string.IsNullOrWhiteSpace(label)) varLabels.TryAdd(outVar!, label!);
        }
        var text = new FlowDocText(varLabels, nodeLabels);

        var steps = new Dictionary<string, DocStep>();
        foreach (var (id, n) in nodes)
            if (n is JsonObject o) steps[id] = Describe(id, o, text, lookups, telephony);

        // ── Walk from each entry point, assigning steps to groups in reading order ──
        var visited = new HashSet<string>();
        var groups = new List<DocGroup>();
        DocGroup NewGroup(string key, string title, string? subtitle = null)
        {
            var g = new DocGroup { Key = key, Title = title, Subtitle = subtitle };
            groups.Add(g);
            return g;
        }

        void Walk(string startId, DocGroup group, bool unreachable)
        {
            var stack = new Stack<(string Id, DocGroup Group)>();
            stack.Push((startId, group));
            while (stack.Count > 0)
            {
                var (id, g) = stack.Pop();
                if (!steps.TryGetValue(id, out var step) || !visited.Add(id)) continue;
                // A CRM section starts its own group (its own chart) — except among unconnected leftovers.
                if (!telephony && step.Type == "section" && !unreachable)
                {
                    if (g.Steps.Count == 0) groups.Remove(g);   // a script that opens with a section has no "Start" group
                    g = NewGroup(id, step.Title, "Section");
                }
                step.GroupKey = g.Key;
                step.Unreachable = unreachable;
                g.Steps.Add(step);
                foreach (var exit in step.Exits.OrderByDescending(e => Rank(e.Handle)))   // stack: last pushed runs first
                    stack.Push((exit.TargetId, g));
            }
        }

        if (entry is not null && steps.ContainsKey(entry))
            Walk(entry, NewGroup("start", telephony ? "Call flow" : "Start", telephony ? "From the moment the call arrives" : null), false);

        // Event handlers (call flows): their own entry points.
        foreach (var (id, step) in steps.Where(s => s.Value.Kind == StepKind.Event && !visited.Contains(s.Key)).ToList())
            Walk(id, NewGroup(id, step.Title, "Runs when this happens during the call"), false);

        // Anything still unvisited: steps nothing leads to (start from the ones with no incoming exits).
        var incoming = steps.Values.SelectMany(s => s.Exits).Select(e => e.TargetId).ToHashSet();
        var leftovers = steps.Keys.Where(k => !visited.Contains(k)).ToList();
        if (leftovers.Count > 0)
        {
            var g = NewGroup("unconnected", "Not connected", "Steps nothing in the script leads to — agents and callers never reach them");
            foreach (var id in leftovers.OrderBy(k => incoming.Contains(k)))
                Walk(id, g, true);
        }

        groups.RemoveAll(g => g.Steps.Count == 0);
        var number = 1;
        foreach (var g in groups)
            foreach (var s in g.Steps) s.Number = number++;
        chapter.Groups.AddRange(groups);
        foreach (var s in steps.Values) chapter.StepsById[s.Id] = s;
        return chapter;
    }

    // ── One step ────────────────────────────────────────────────────────────

    private static string? Str(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : o[key]?.ToString();

    private static bool Bool(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    private static int? Int(JsonObject o, string key) =>
        o[key] is JsonValue v && (v.TryGetValue<int>(out var i) ? i : v.TryGetValue<double>(out var d) ? (int)d : (int?)null) is { } n ? n : null;

    private static string Name(IReadOnlyDictionary<Guid, string> names, string? id, string fallback) =>
        Guid.TryParse(id, out var g) && names.TryGetValue(g, out var n) ? n : fallback;

    private static string AudioName(FlowDocLookups l, string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "a recording";
        if (id.StartsWith("__platform:", StringComparison.Ordinal))
            return $"standard phrase \"{FlowDocText.Words(id.Split('/').Last())}\"";
        return Name(l.AudioNames, id, "a recording") is var n && n != "a recording" ? $"recording \"{n}\"" : n;
    }

    private static DocStep Describe(string id, JsonObject n, FlowDocText text, FlowDocLookups lookups, bool telephony)
    {
        var type = Str(n, "type") ?? "script";
        var label = Str(n, "label");
        var (kind, typeLabel) = Classify(type);
        Guid? calls = type is "execute_flow" or "transition_to_flow" && Guid.TryParse(Str(n, "targetFlowId"), out var tf) ? tf
            : type == "tf_script_pop" && Guid.TryParse(Str(n, "flowId"), out var sp) ? sp : null;
        var step = new DocStep
        {
            Id = id, Type = type, Kind = kind, TypeLabel = typeLabel, CallsFlowId = calls,
            Title = string.IsNullOrWhiteSpace(label) ? (type == "section" ? Str(n, "name") ?? typeLabel : typeLabel) : text.Text(label),
        };

        // Exits, with readable labels.
        var optionLabels = new Dictionary<string, string>(StringComparer.Ordinal);
        if (n["options"] is JsonArray opts)
            foreach (var o in opts.OfType<JsonObject>())
            {
                if (Str(o, "value") is { } v) optionLabels[v] = Str(o, "label") ?? v;
                if (Str(o, "transition") is { } tr)
                {
                    var say = o["phrases"] is JsonArray ph && ph.Count > 0 ? $" or say \"{ph[0]}\"" : "";
                    optionLabels[tr] = Str(o, "digit") is { Length: > 0 } d ? $"Press {d}{say}" : FlowDocText.Words(tr);
                }
            }
        if (n["transitions"] is JsonObject transitions)
            foreach (var (handle, target) in transitions)
            {
                if (target is null || target.ToString() is not { Length: > 0 } t) continue;
                string? exitLabel = handle switch
                {
                    "default" => null,
                    "true" when type == "branch" || type == "tf_branch" => "Yes",
                    "false" when type == "branch" || type == "tf_branch" => "No",
                    _ when optionLabels.TryGetValue(handle, out var ol) => ol,
                    "no_match" => "No match",
                    "end_of_stream" => null,
                    _ => FlowDocText.Words(handle),
                };
                step.Exits.Add(new DocExit(exitLabel, t, handle));
            }

        // What the agent reads / the caller hears.
        foreach (var key in new[] { "content", "scriptContent" })
            if (Str(n, key) is { Length: > 0 } html) step.Script.AddRange(text.Html(html));

        void Fact(string l, string? v) { if (!string.IsNullOrWhiteSpace(v)) step.Facts.Add(new DocFact(l, v)); }

        switch (type)
        {
            case "script":
                if (Str(n, "waitForTelephonyEventName") is { Length: > 0 } ev)
                    Fact("Waits for", $"the phone system to finish \"{FlowDocText.Words(ev)}\" before the agent can continue");
                break;
            case "input":
                Fact("Agent records", $"{step.Title} ({FieldType(Str(n, "fieldType"))}){(Bool(n, "required") ? ", required" : "")}");
                if (n["options"] is JsonArray io && io.Count > 0)
                    Fact("Choices", string.Join(", ", io.OfType<JsonObject>().Select(o => Str(o, "label") ?? Str(o, "value"))));
                break;
            case "email":
                Fact("Agent records", $"email address{(Bool(n, "required") ? ", required" : "")}");
                Fact("Checks", string.Join(", ", new[] { Bool(n, "checkMX") ? "the domain accepts mail" : null, Bool(n, "checkDisposable") ? "not a throwaway address" : null }.OfType<string>()));
                break;
            case "phone":
                Fact("Agent records", $"{FlowDocText.Words(Str(n, "phoneRole") ?? "")} phone number{(Bool(n, "required") ? ", required" : "")}".Trim());
                if (Bool(n, "dncCheck")) Fact("Checks", "the Do Not Call list");
                break;
            case "address":
                Fact("Agent records", $"{FlowDocText.Words(Str(n, "addressRole") ?? "")} address — verified against the postal database".Trim());
                break;
            case "branch":
                Fact("Checks", text.Condition(Str(n, "condition")));
                break;
            case "section":
                if (Bool(n, "allowJumpFromAnywhere")) Fact("Agents can", "jump to this section from anywhere in the script");
                break;
            case "set_variable" when n["assignments"] is JsonArray asg:
                Fact("Remembers", string.Join(", ", asg.OfType<JsonObject>().Select(a => text.Text(Str(a, "variable")).Trim('[', ']')).Distinct()));
                break;
            case "set_custom_field":
                Fact("Saves", "a field on the call record");
                break;
            case "api_call":
                Fact("Sends to", $"{Str(n, "apiDefinitionName") ?? "an external system"}{(Str(n, "apiEndpointName") is { Length: > 0 } ep ? $" — {ep}" : "")}");
                if (Bool(n, "releasesCardData")) Fact("Note", "this is the order submission — card details are deleted once it succeeds");
                if (Bool(n, "oncePerCall")) Fact("Runs", "once per call");
                break;
            case "tf_general_api_call":
                Fact("Sends to", "an external system");
                break;
            case "add_to_cart":
                Fact("Adds", $"{Str(n, "offerDisplayName") ?? "an offer"} × {Int(n, "quantity") ?? 1}");
                break;
            case "remove_cart_item": Fact("Removes", Str(n, "offerDisplayName") ?? "an item from the cart"); break;
            case "reset_cart": Fact("Clears", "the cart"); break;
            case "authorize_payment":
                Fact("Authorizes", $"the card for the {(Str(n, "amountMode") == "cart_total" ? "cart total" : "amount due")}");
                break;
            case "void_payment": Fact("Voids", "the payment authorization"); break;
            case "commit": Fact("Locks", "the order so it can't be changed by mistake"); break;
            case "set_disposition":
                Fact("Call outcome", Str(n, "dispositionName") is { Length: > 0 } dn ? dn : text.Text(Str(n, "value")));
                break;
            case "trigger_telephony_event":
                Fact("Signals the phone system", FlowDocText.Words(Str(n, "eventName") ?? Str(n, "event") ?? "an event"));
                break;
            case "send_email" or "tf_send_email":
                Fact("Sends an email", text.Text(Str(n, "subject") ?? Str(n, "emailSubject")) is { Length: > 0 } subj ? $"\"{subj}\"" : "a notification");
                break;
            case "execute_flow":
                Fact("Runs", $"\"{Name(lookups.FlowNames, Str(n, "targetFlowId"), Str(n, "targetFlowName") ?? "another script")}\", then continues here");
                break;
            case "transition_to_flow":
                Fact("Continues in", $"\"{Name(lookups.FlowNames, Str(n, "targetFlowId"), Str(n, "targetFlowName") ?? "another script")}\"");
                break;
            case "end":
                Fact("Ends", $"the script{(Str(n, "status") is { Length: > 0 } es && es != "complete" ? $" ({FlowDocText.Words(es).ToLowerInvariant()})" : "")}");
                break;

            // ── Call flows ──
            case "tf_play":
                if (Str(n, "audioSource") == "tts" || !string.IsNullOrWhiteSpace(Str(n, "ttsText")))
                    step.Script.Add(new DocParagraph([new DocRun($"\"{text.Text(Str(n, "ttsText"))}\"")]));
                else Fact("Plays", AudioName(lookups, Str(n, "audioFileId")));
                if (Bool(n, "autoRestart")) Fact("Repeats", "until something else happens (hold music)");
                if (n["periodicAnnouncements"] is JsonArray pa && pa.Count > 0)
                    Fact("Every", $"{Int(n, "periodicAnnouncementIntervalSeconds") ?? 30} seconds, plays an announcement over it");
                break;
            case "tf_ivr_menu":
                Fact("Plays", AudioName(lookups, Str(n, "promptAudioFileId")));
                if (n["options"] is JsonArray mo)
                    Fact("Caller can", string.Join("; ", mo.OfType<JsonObject>().Select(o =>
                        $"{(Str(o, "digit") is { Length: > 0 } d ? $"press {d}" : "")}{(o["phrases"] is JsonArray p && p.Count > 0 ? $"{(Str(o, "digit") is { Length: > 0 } ? " or " : "")}say \"{string.Join("\" / \"", p.Take(3))}\"" : "")}: {FlowDocText.Words(Str(o, "transition") ?? "")}")));
                Fact("Tries", $"{Int(n, "maxTries") ?? 3}");
                break;
            case "tf_data_collect":
                Fact("Caller enters", $"{FlowDocText.Words(Str(n, "outputVariable") ?? "a value")} by keypad{(Bool(n, "allowVoice") ? " or voice" : "")}");
                break;
            case "tf_dtmf": Fact("Caller presses", "keys on their phone"); break;
            case "tf_secure_collect":
                Fact("Secure capture", "the caller keys their card details on the phone keypad — the agent never hears or sees them (PCI)");
                if (n["fields"] is JsonArray sf)
                    Fact("Collects", string.Join(", ", sf.OfType<JsonObject>().Select(f => Str(f, "key") switch
                    {
                        "pan" => "card number", "expiry" => "expiration date", "cvv" => "security code", var k => FlowDocText.Words(k ?? ""),
                    })));
                break;
            case "tf_route_to_queue":
                Fact("Waits for", Name(lookups.GroupNames, Str(n, "agentGroupId"), "") is { Length: > 0 } gname
                    ? $"an agent in the {gname} group" : "the next available agent");
                break;
            case "tf_transfer":
                Fact("Transfers to", Str(n, "destinationType") switch
                {
                    "campaign_queue" => $"the {Name(lookups.CampaignNames, Str(n, "targetCampaignId"), "another campaign")} queue",
                    "external" or "external_number" => "an outside phone number",
                    "agent" => "a specific agent",
                    var d => FlowDocText.Words(d ?? "another destination").ToLowerInvariant(),
                });
                break;
            case "tf_time_of_day" when n["windows"] is JsonArray tw:
                Fact("Hours", string.Join("; ", tw.OfType<JsonObject>().Select(w =>
                    $"{FlowDocText.Words(Str(w, "name") ?? "window")}: {DaysText(w["days"] as JsonArray)} {Str(w, "start")}–{Str(w, "end")}")));
                Fact("Time zone", Str(n, "timezone"));
                break;
            case "tf_queue_callback": Fact("Offers", "a callback — the caller hangs up and keeps their place in line"); break;
            case "tf_scheduled_callback": Fact("Books", "a callback for a later time"); break;
            case "tf_voicemail": Fact("Takes", "a voicemail message"); break;
            case "tf_record": Fact("Starts", "recording the call"); break;
            case "tf_whisper": Fact("Agent hears", text.Text(Str(n, "ttsText")) is { Length: > 0 } wt ? $"\"{wt}\" before the caller is connected" : "a message before the caller is connected"); break;
            case "tf_script_pop":
                // No script named on the step: the agent gets the campaign's script (the service fills that in).
                if (Guid.TryParse(Str(n, "flowId"), out _))
                    Fact("Opens the agent script", Name(lookups.FlowNames, Str(n, "flowId"), "another script"));
                break;
            case "tf_check_block_list": Fact("Checks", "the caller against the block list"); break;
            case "tf_check_agent_availability": Fact("Checks", "whether any agent is free or logged in"); break;
            case "tf_delay": Fact("Waits", $"{Int(n, "seconds") ?? Int(n, "delaySeconds") ?? 0} seconds"); break;
            case "tf_repeat": Fact("Repeats", $"{Int(n, "repeatCount") ?? 1} times, then moves on"); break;
            case "tf_branch": Fact("Checks", text.Condition(Str(n, "condition"))); break;
            case "tf_reject": Fact("Rejects", "the call"); break;
            case "tf_hangup": Fact("Hangs up", "the call"); break;
            case "tf_end": Fact("Ends", "this part of the call flow"); break;
        }
        return step;
    }

    private static string DaysText(JsonArray? days)
    {
        if (days is null) return "";
        var d = days.Select(x => x is JsonValue v && v.TryGetValue<int>(out var i) ? i : -1).Where(i => i is >= 0 and <= 6).Order().ToArray();
        string[] names = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
        if (d.Length == 7) return "every day";
        if (d.Length >= 3 && d[^1] - d[0] == d.Length - 1) return $"{names[d[0]]}–{names[d[^1]]}";
        return string.Join(", ", d.Select(i => names[i]));
    }

    private static string FieldType(string? t) => t switch
    {
        null or "" or "text" => "text",
        "select" => "pick one",
        "checkbox" => "yes / no",
        "date" => "date",
        "number" => "number",
        var x => FlowDocText.Words(x).ToLowerInvariant(),
    };

    private static (StepKind, string) Classify(string type) => type switch
    {
        "section" => (StepKind.Section, "Section"),
        "script" => (StepKind.Speak, "Script"),
        "input" => (StepKind.Speak, "Agent input"),
        "email" => (StepKind.Speak, "Email address"),
        "phone" => (StepKind.Speak, "Phone number"),
        "address" => (StepKind.Speak, "Address"),
        "branch" or "tf_branch" => (StepKind.Decision, "Decision"),
        "tf_ivr_menu" => (StepKind.Decision, "Phone menu"),
        "tf_time_of_day" => (StepKind.Decision, "Business hours"),
        "tf_check_block_list" or "tf_check_agent_availability" => (StepKind.Decision, "Check"),
        "add_to_cart" or "remove_cart_item" or "reset_cart" or "authorize_payment" or "void_payment" or "commit" => (StepKind.Commerce, type switch
        {
            "add_to_cart" => "Add to cart", "remove_cart_item" => "Remove from cart", "reset_cart" => "Clear cart",
            "authorize_payment" => "Payment", "void_payment" => "Void payment", _ => "Lock order",
        }),
        "api_call" or "tf_general_api_call" => (StepKind.Integration, "Sends data"),
        "send_email" or "tf_send_email" => (StepKind.Integration, "Email"),
        "execute_flow" => (StepKind.Integration, "Runs a sub-script"),
        "transition_to_flow" => (StepKind.Integration, "Continues in another script"),
        "trigger_telephony_event" => (StepKind.Integration, "Phone system signal"),
        "tf_play" or "tf_answer" or "tf_record" or "tf_whisper" => (StepKind.Audio, type switch
        {
            "tf_play" => "Plays audio", "tf_answer" => "Answers the call", "tf_record" => "Recording", _ => "Whisper to agent",
        }),
        "tf_data_collect" or "tf_dtmf" or "tf_secure_collect" => (StepKind.Audio, type == "tf_secure_collect" ? "Secure card capture" : "Caller input"),
        "tf_route_to_queue" or "tf_transfer" or "tf_queue_callback" or "tf_scheduled_callback" or "tf_voicemail" or "tf_script_pop"
            or "tf_set_caller_id" or "tf_cancel_dial" => (StepKind.Routing, type switch
        {
            "tf_route_to_queue" => "Queue", "tf_transfer" => "Transfer", "tf_queue_callback" => "Callback (keep place)",
            "tf_scheduled_callback" => "Scheduled callback", "tf_voicemail" => "Voicemail", "tf_script_pop" => "Agent script",
            "tf_set_caller_id" => "Caller ID", _ => "Cancel dial",
        }),
        _ when type.StartsWith("tf_on_", StringComparison.Ordinal) => (StepKind.Event, EventLabel(type)),
        "end" or "tf_end" or "tf_hangup" or "tf_reject" => (StepKind.End, type switch
        {
            "end" => "End", "tf_hangup" => "Hang up", "tf_reject" => "Reject call", _ => "End",
        }),
        "tf_delay" or "tf_repeat" => (StepKind.System, type == "tf_delay" ? "Pause" : "Repeat"),
        _ => (StepKind.System, FlowDocText.Words(type.StartsWith("tf_", StringComparison.Ordinal) ? type[3..] : type)),
    };

    private static string EventLabel(string type) => type switch
    {
        "tf_on_agent_answer" => "When the agent answers",
        "tf_on_agent_selected" => "When an agent is chosen",
        "tf_on_call_disconnected" => "When the call ends",
        "tf_on_custom_event" => "When the agent's script signals",
        _ => "When " + FlowDocText.Words(type["tf_on_".Length..]).ToLowerInvariant(),
    };

    internal static string Plural(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n.ToString(CultureInfo.InvariantCulture)} {many}";
}
