using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.FlowEngine.NodeHandlers;

/// <summary>
/// Handles "email" nodes — agent captures and validates an email address.
///
/// Node schema:
/// {
///   "type": "email",
///   "label": "Customer Email",
///   "outputVariable": "customer_email",
///   "required": true,
///   "checkARecord": true,
///   "checkMX": true,
///   "checkDisposable": true,
///   "transitions": { "default": "node_002" }
/// }
///
/// Stores output as a JSON object in FlowVars under the outputVariable key.
/// Sub-properties resolved via {{flow.customer_email.isDeliverable}} etc.:
///   value           = the raw email string
///   isFormatValid   = true/false
///   domainExists    = true/false/null (null when checkARecord=false)
///   mxExists        = true/false/null (null when checkMX=false)
///   isDisposable    = true/false/null (null when checkDisposable=false)
///   isDeliverable   = true/false
/// </summary>
public class EmailNodeHandler(IVariableResolver resolver, IEmailValidationService emailValidator)
    : NodeHandlerBase(resolver), INodeHandler
{
    public string NodeType => "email";

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

    private static string? GetValidationError(
        EmailValidationResult r, bool checkARecord, bool checkMX, bool checkDisposable)
    {
        if (!r.IsFormatValid)
            return "Invalid email format. Please check and try again.";
        if (checkARecord && r.DomainExists == false)
            return "That email domain does not exist. Please check and try again.";
        if (checkMX && r.MXExists == false)
            return "That email domain has no mail server. Please use a valid email address.";
        if (checkDisposable && r.IsDisposable == true)
            return "Disposable email addresses are not accepted. Please use a permanent email address.";
        return null;
    }

    public async Task<NodeResult> ExecuteAsync(
        JsonObject node, FlowExecutionContext ctx,
        string? agentInput, string agentTransition, CancellationToken ct = default)
    {
        var required        = node["required"]?.GetValue<bool>() ?? false;
        var checkARecord    = node["checkARecord"]?.GetValue<bool>() ?? false;
        var checkMX         = node["checkMX"]?.GetValue<bool>() ?? false;
        var checkDisposable = node["checkDisposable"]?.GetValue<bool>() ?? false;
        var outputVar       = Str(node, "outputVariable")?.Trim() ?? string.Empty;
        var prompt          = Resolver.Resolve(Str(node, "prompt") ?? string.Empty, ctx.ToVariableContext());

        FlowNodeState MakeState() => BuildState(ctx, node, resolvedContent: prompt,
            inputType: "email", required: required);

        FlowNodeState WithScript(FlowNodeState s) { AttachInlineScript(node, ctx, s); return s; }

        // First display (no input yet)
        if (agentInput is null)
        {
            var firstState = WithScript(MakeState());
            if (!string.IsNullOrEmpty(outputVar) && ctx.FlowVars.TryGetValue(outputVar, out var existing))
            {
                try
                {
                    // Full email object stored by the email node
                    if (System.Text.Json.Nodes.JsonNode.Parse(existing) is System.Text.Json.Nodes.JsonObject obj)
                        firstState.DefaultValue = obj["value"]?.GetValue<string>();
                    else
                        firstState.DefaultValue = existing; // plain string set via set_variable
                }
                catch
                {
                    firstState.DefaultValue = existing; // not JSON — use as-is
                }
            }
            return new NodeResult(firstState, NextNodeId: null);
        }

        var email = agentInput.Trim();

        // Required guard — re-display if blank
        if (required && string.IsNullOrEmpty(email))
            return new NodeResult(WithScript(MakeState()), NextNodeId: null);

        // Blank optional — store empty object and advance
        if (string.IsNullOrEmpty(email))
        {
            if (!string.IsNullOrEmpty(outputVar))
                ctx.FlowVars[outputVar] = BuildEmailObject(
                    string.Empty, false, null, null, null, false).ToJsonString();

            var next = Transition(node, agentTransition) ?? Transition(node, "default");
            AppendHistory(ctx, node, email, next);
            return new NodeResult(WithScript(MakeState()), next);
        }

        // Non-blank email — validate and block on any failed check
        var validationResult = await emailValidator.ValidateAsync(
            email, checkARecord, checkMX, checkDisposable, ct);

        // Store as JSON object regardless so downstream branch nodes can use sub-properties
        if (!string.IsNullOrEmpty(outputVar))
            ctx.FlowVars[outputVar] = BuildEmailObject(
                email,
                validationResult.IsFormatValid,
                validationResult.DomainExists,
                validationResult.MXExists,
                validationResult.IsDisposable,
                validationResult.IsDeliverable).ToJsonString();

        // Determine error message for the first failing check
        var validationError = GetValidationError(validationResult, checkARecord, checkMX, checkDisposable);
        if (validationError is not null)
        {
            var errorState = WithScript(MakeState());
            errorState.ValidationError = validationError;
            return new NodeResult(errorState, NextNodeId: null);
        }

        // All checks passed — advance
        var advanceNext = Transition(node, agentTransition) ?? Transition(node, "default");
        AppendHistory(ctx, node, email, advanceNext);
        return new NodeResult(WithScript(MakeState()), advanceNext);
    }

    private static JsonObject BuildEmailObject(
        string value, bool isFormatValid, bool? domainExists,
        bool? mxExists, bool? isDisposable, bool isDeliverable) => new()
    {
        ["value"]         = value,
        ["isFormatValid"] = isFormatValid,
        ["domainExists"]  = domainExists is bool d ? (JsonNode)JsonValue.Create(d) : null,
        ["mxExists"]      = mxExists    is bool m ? (JsonNode)JsonValue.Create(m) : null,
        ["isDisposable"]  = isDisposable is bool s ? (JsonNode)JsonValue.Create(s) : null,
        ["isDeliverable"] = isDeliverable,
    };
}
