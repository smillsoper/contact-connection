using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.FlowEngine.NodeHandlers;

/// <summary>
/// Handles "input" nodes — agent captures a value (text, select, checkbox).
/// The captured value is stored in ctx.Inputs keyed by node_id for later {{input.node_id}} resolution.
///
/// Node schema:
/// {
///   "type": "input",
///   "label": "First Name",
///   "input_type": "text" | "select" | "checkbox",
///   "prompt": "Ask the caller for their first name.",
///   "options": [{ "value": "yes", "label": "Yes" }, ...],   // for select/radio
///   "required": true,
///   "minChars": 2,           // text only — minimum characters (omit for none)
///   "maxChars": 50,          // text only — maximum characters (omit for none)
///   "inputMask": "(000) 000-0000", // text only — WinForms mask; overrides min/max
///   "transitions": { "default": "node_002" }
/// }
/// </summary>
public class InputNodeHandler(IVariableResolver resolver) : NodeHandlerBase(resolver), INodeHandler
{
    public string NodeType => "input";

    public Task<NodeResult> ExecuteAsync(
        JsonObject node, FlowExecutionContext ctx,
        string? agentInput, string agentTransition, CancellationToken ct = default)
    {
        var varCtx    = ctx.ToVariableContext();
        var inputType = Str(node, "input_type") ?? Str(node, "fieldType") ?? "text";
        var prompt    = Resolver.Resolve(Str(node, "prompt") ?? string.Empty, varCtx);
        var outputVar = Str(node, "outputVariable")?.Trim();

        // If agent has submitted a value, store it and advance
        if (agentInput is not null)
        {
            // Guard: locked field check (field name = node label normalized)
            var fieldKey = Str(node, "field_key");
            if (fieldKey is not null && ctx.LockedFields.Contains(fieldKey))
                throw new InvalidOperationException(
                    $"Field '{fieldKey}' is locked by a commitment event and cannot be modified.");

            ctx.Inputs[ctx.CurrentNodeId] = agentInput;

            // Auto-store into flow variables if outputVariable is set
            if (!string.IsNullOrEmpty(outputVar))
                ctx.FlowVars[outputVar] = agentInput;

            // Select nodes use the chosen value as the transition key (one handle per option in the designer).
            // Fall back to agentTransition then "default" for all other input types.
            var effectiveTransition = inputType == "select" ? agentInput : agentTransition;
            var next = Transition(node, effectiveTransition) ?? Transition(node, agentTransition) ?? Transition(node, "default");
            AppendHistory(ctx, node, agentInput, next);

            var advancedState = BuildState(ctx, node, resolvedContent: prompt,
                inputType: inputType, options: ParseOptions(node));
            AttachInlineScript(node, ctx, advancedState);
            return Task.FromResult(new NodeResult(advancedState, next));
        }

        // No input yet — return the node for display (agent must submit before advancing)
        var displayState = BuildState(ctx, node, resolvedContent: prompt,
            inputType: inputType, options: ParseOptions(node));
        AttachInlineScript(node, ctx, displayState);
        AttachTextConstraints(node, inputType, displayState);

        // Pre-populate from existing flow variable
        if (!string.IsNullOrEmpty(outputVar) && ctx.FlowVars.TryGetValue(outputVar, out var existing))
            displayState.DefaultValue = existing;

        return Task.FromResult(new NodeResult(displayState, NextNodeId: null));
    }

    private static void AttachTextConstraints(JsonObject node, string inputType, FlowNodeState state)
    {
        if (inputType != "text") return;

        if (node["minChars"] is System.Text.Json.Nodes.JsonValue minV && minV.TryGetValue<int>(out var minInt))
            state.MinChars = minInt;
        if (node["maxChars"] is System.Text.Json.Nodes.JsonValue maxV && maxV.TryGetValue<int>(out var maxInt))
            state.MaxChars = maxInt;

        // Resolve mask: "__custom__" → use customMask field; otherwise use inputMask directly
        var inputMask = Str(node, "inputMask");
        if (!string.IsNullOrEmpty(inputMask))
        {
            state.InputMask = inputMask == "__custom__"
                ? Str(node, "customMask")
                : inputMask;
        }
    }

    private void AttachInlineScript(JsonObject node, FlowExecutionContext ctx, FlowNodeState state)
    {
        var varCtx = ctx.ToVariableContext();
        var scriptLabel   = Str(node, "scriptLabel");
        var scriptContent = Str(node, "scriptContent");
        if (!string.IsNullOrWhiteSpace(scriptLabel))
            state.NodeScriptLabel = Resolver.Resolve(scriptLabel, varCtx);
        if (!string.IsNullOrWhiteSpace(scriptContent))
            state.NodeScriptContent = Resolver.Resolve(scriptContent, varCtx);
    }

    private static List<FlowOption>? ParseOptions(JsonObject node)
    {
        var optionsNode = node["options"];
        if (optionsNode is null) return null;

        // Current format: [{value, label}] array
        if (optionsNode is JsonArray optionsArray)
        {
            return optionsArray
                .OfType<JsonObject>()
                .Select(o => new FlowOption
                {
                    Value = o["value"]?.GetValue<string>() ?? string.Empty,
                    Label = o["label"]?.GetValue<string>() ?? string.Empty
                })
                .ToList();
        }

        // Legacy format: comma-separated string (flows saved before array migration)
        var optionsStr = optionsNode.GetValue<string>();
        if (string.IsNullOrWhiteSpace(optionsStr)) return null;
        return optionsStr
            .Split(',')
            .Select(o => o.Trim())
            .Where(o => !string.IsNullOrEmpty(o))
            .Select(o => new FlowOption { Value = o, Label = o })
            .ToList();
    }
}
