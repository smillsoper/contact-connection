using ContactConnection.Api.Telephony;
using ContactConnection.Application.Interfaces.Services;
using Xunit;

namespace ContactConnection.Api.Tests.Telephony;

/// <summary>Supervisor listen-in (S167): the modes map onto FreeSWITCH eavesdrop's DTMF controls, and
/// the agent's leg is found from the caller's telephony session.</summary>
public class MonitorModeTests
{
    [Theory]
    [InlineData(MonitorMode.Listen, "0")]   // restore eavesdrop — nobody hears the supervisor
    [InlineData(MonitorMode.Coach, "2")]    // speak with the eavesdropped (agent) leg only
    [InlineData(MonitorMode.Barge, "3")]    // three-way
    [InlineData("shout", null)]
    public void ModeDigits(string mode, string? digit) => Assert.Equal(digit, MonitorMode.Digit(mode));

    [Fact]
    public void AgentLeg_PrefersTheStableBridgedPeer()
    {
        var s = new TelephonyCallSession();
        Assert.Null(SupervisorCallService.AgentLeg(s));
        s.Vars["_agent_uuid"] = "agent-leg";
        Assert.Equal("agent-leg", SupervisorCallService.AgentLeg(s));
        s.Vars["_bridged_peer_uuid"] = "bridged-peer";
        Assert.Equal("bridged-peer", SupervisorCallService.AgentLeg(s));
    }
}
