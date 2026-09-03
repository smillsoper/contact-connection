using System.Reflection;
using Xunit;
using CrmFlowEngine = ContactConnection.Infrastructure.FlowEngine.FlowEngine;

namespace ContactConnection.Infrastructure.Tests.FlowEngine;

/// <summary>
/// The CRM FlowEngine only auto-advances node types listed in its private AutoAdvanceTypes set;
/// anything else stops and is pushed to the agent UI as an interactive node. A "transparent"
/// handler (books/computes something then returns a next node with no content) that is missing
/// from the set renders as a dead node with no controls — the agent can't proceed.
/// </summary>
public class FlowEngineAutoAdvanceTests
{
    private static readonly HashSet<string> AutoAdvanceTypes =
        (HashSet<string>)typeof(CrmFlowEngine)
            .GetField("AutoAdvanceTypes", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

    [Theory]
    [InlineData("branch")]
    [InlineData("set_variable")]
    [InlineData("section")]
    [InlineData("execute_flow")]
    [InlineData("transition_to_flow")]
    [InlineData("api_call")]
    [InlineData("scheduled_callback")]
    public void TransparentNodeType_IsAutoAdvanced(string nodeType) =>
        Assert.Contains(nodeType, AutoAdvanceTypes);
}
