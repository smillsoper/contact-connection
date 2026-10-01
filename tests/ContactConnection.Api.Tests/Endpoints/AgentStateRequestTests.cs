using ContactConnection.Api.Endpoints;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using Xunit;

namespace ContactConnection.Api.Tests.Endpoints;

/// <summary>
/// S171: custom unavailable codes were listed but never selectable — the softphone sent the code's
/// GUID as the state code, which overflowed agent_state_history.state_code (varchar 30) and the
/// error was swallowed. Custom codes now go through as "unavailable_custom", named by the server.
/// </summary>
public class AgentStateRequestTests
{
    private static readonly string AgentRole = Guid.NewGuid().ToString();

    [Fact]
    public void CustomCode_BecomesUnavailableCustom_NamedByTheServer()
    {
        var training = CustomUnavailableCode.Create(Guid.NewGuid(), "Training", [AgentRole]);
        var (state, error) = AgentStateEndpoints.ResolveRequestedState(
            new SetAgentStateRequest("unavailable_custom", training.Id, CustomLabel: "Spoofed"), training, AgentRole);

        Assert.Null(error);
        Assert.Equal(AgentStateCodes.UnavailableCustom, state!.Value.Code);
        Assert.Equal("Unavailable - Training", state.Value.Label);
        Assert.Equal(training.Id, state.Value.CustomCodeId);
        Assert.True(state.Value.Code.Length <= 30);   // fits agent_state_history.state_code
    }

    [Fact]
    public void CustomCode_ForAllRoles_IsAllowed()
    {
        var meeting = CustomUnavailableCode.Create(Guid.NewGuid(), "Meeting", []);
        var (state, _) = AgentStateEndpoints.ResolveRequestedState(new SetAgentStateRequest("unavailable_custom", meeting.Id), meeting, AgentRole);
        Assert.Equal("Unavailable - Meeting", state!.Value.Label);
    }

    [Fact]
    public void CustomCode_RejectedForOtherRoles_Inactive_OrMissing()
    {
        var supervisorsOnly = CustomUnavailableCode.Create(Guid.NewGuid(), "Coaching", [Guid.NewGuid().ToString()]);
        Assert.Null(AgentStateEndpoints.ResolveRequestedState(new SetAgentStateRequest("unavailable_custom", supervisorsOnly.Id), supervisorsOnly, AgentRole).State);

        var retired = CustomUnavailableCode.Create(Guid.NewGuid(), "Old", []);
        retired.Deactivate();
        Assert.Null(AgentStateEndpoints.ResolveRequestedState(new SetAgentStateRequest("unavailable_custom", retired.Id), retired, AgentRole).State);

        Assert.Null(AgentStateEndpoints.ResolveRequestedState(new SetAgentStateRequest("unavailable_custom", Guid.NewGuid()), null, AgentRole).State);
    }

    [Theory]
    [InlineData("available", "Available")]
    [InlineData("unavailable_break", "Unavailable - Break")]
    [InlineData("unavailable_lunch", "Unavailable - Lunch")]
    [InlineData("unavailable", "Unavailable")]
    [InlineData("logged_out", "Logged Out")]
    public void BuiltInStates_KeepTheirLabels(string code, string label)
    {
        var (state, _) = AgentStateEndpoints.ResolveRequestedState(new SetAgentStateRequest(code), null, AgentRole);
        Assert.Equal((code, label, (Guid?)null), state!.Value);
    }

    [Theory]
    [InlineData("on_call")]
    [InlineData("acw")]
    [InlineData("callback_pending")]
    [InlineData("3f2b0c1e-0000-0000-0000-000000000000")]
    public void EngineOwnedOrUnknownStates_AreRejected(string code) =>
        Assert.Null(AgentStateEndpoints.ResolveRequestedState(new SetAgentStateRequest(code), null, AgentRole).State);
}
