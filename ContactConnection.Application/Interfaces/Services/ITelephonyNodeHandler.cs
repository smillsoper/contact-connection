using System.Text.Json.Nodes;

namespace ContactConnection.Application.Interfaces.Services;

public interface ITelephonyNodeHandler
{
    string NodeType { get; }
    Task<TelephonyNodeResult> ExecuteAsync(
        JsonObject node,
        TelephonyFlowContext ctx,
        CancellationToken ct = default);
}

/// <summary>NextNodeId null means terminal (no further nodes to execute).</summary>
public record TelephonyNodeResult(
    string? NextNodeId,
    string TransitionTaken = "default");
