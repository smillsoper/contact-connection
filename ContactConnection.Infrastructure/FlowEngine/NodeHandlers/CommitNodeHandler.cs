using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.ValueObjects;

namespace ContactConnection.Infrastructure.FlowEngine.NodeHandlers;

/// <summary>
/// Handles "commit" nodes — a designer-placed point of no return (ARCHITECTURE §21 commitment
/// events). Passing it: (1) puts the session in committed mode — FlowEngine then refuses every
/// section jump except to <c>allowedSectionIds</c> and hides the rest of the jump dropdown; (2)
/// appends a CommitmentEvent to the call record (audit trail; the record's commitment events are the
/// lock registry future relaunch/supervisor-override work applies). Transparent to the agent.
///
/// Node schema:
/// {
///   "type": "commit",
///   "eventName": "order_submitted",
///   "lockLabel": "Order submitted — finalize this call as an order",
///   "allowedSectionIds": [],           // section node ids still jumpable afterwards
///   "transitions": { "default": "node_010" }
/// }
///
/// Passing a second commit point keeps the first event name and can only narrow the allowed
/// sections (intersection) — a later commit never reopens anything. In practice flows have one.
/// </summary>
public class CommitNodeHandler(IVariableResolver resolver, ICallRecordRepository callRecords)
    : NodeHandlerBase(resolver), INodeHandler
{
    public string NodeType => "commit";

    public async Task<NodeResult> ExecuteAsync(
        JsonObject node, FlowExecutionContext ctx,
        string? agentInput, string agentTransition, CancellationToken ct = default)
    {
        var eventName = Str(node, "eventName")?.Trim();
        if (string.IsNullOrEmpty(eventName)) eventName = "committed";
        var label = Resolver.ResolveForDisplay(Str(node, "lockLabel") ?? string.Empty, ctx.ToVariableContext()).Trim();
        var allowed = node["allowedSectionIds"] is JsonArray arr
            ? arr.Select(v => v?.GetValue<string>()).Where(v => !string.IsNullOrEmpty(v)).Select(v => v!).ToHashSet()
            : [];

        var firstTime = !ctx.IsCommitted;
        if (firstTime)
        {
            ctx.CommitEventName = eventName;
            ctx.CommitLabel = label;
            ctx.CommitAllowedSections = allowed;

            var record = await callRecords.GetByIdAsync(ctx.CallRecordId, ct);
            if (record is not null)
            {
                record.AddCommitmentEvent(new CommitmentEvent
                {
                    EventName  = eventName,
                    LockLabel  = label,
                    OccurredAt = DateTimeOffset.UtcNow,
                });
                await callRecords.SaveChangesAsync(ct);
            }
        }
        else
        {
            // A later commit point can only narrow what stays reachable, never reopen it.
            ctx.CommitAllowedSections.IntersectWith(allowed);
            if (!string.IsNullOrEmpty(label)) ctx.CommitLabel = label;
        }

        var next = Transition(node, agentTransition) ?? Transition(node, "default");
        AppendHistory(ctx, node, input: null, transition: next);
        return new NodeResult(BuildState(ctx, node, resolvedContent: string.Empty), next);
    }
}
