using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Telephony.Outbound;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony;

/// <summary>Manual outbound Slice A (S179): number shape, caller ID choice, callee time zones, the calling window.</summary>
public class ManualOutboundRulesTests
{
    [Theory]
    [InlineData("(541) 641-3898", "+15416413898")]
    [InlineData("1-541-641-3898", "+15416413898")]
    [InlineData("+15416413898", "+15416413898")]
    [InlineData("541641389", null)]       // too short
    [InlineData("0416413898", null)]      // area code can't start with 0/1
    [InlineData("5411413898", null)]      // exchange can't start with 0/1
    [InlineData("", null)]
    public void Number_ToE164(string input, string? expected) => Assert.Equal(expected, OutboundNumber.ToE164(input));

    [Fact]
    public void CallerId_CampaignFirst_ThenTenantDefault()
    {
        var campaign = Campaign.Create(Guid.NewGuid(), Guid.NewGuid(), "NeuroQ CS", "neuroq-cs");
        campaign.Update("NeuroQ CS", null, CampaignDirection.Outbound, CampaignDialMode.Manual, 5, 30, "541-641-3898",
            50, 300, 30, 10, false, 60, 1, "ring_all", 3);

        Assert.Equal(new OutboundCallerId.Choice("+15416413898", false), OutboundCallerId.Resolve(campaign, "+18005550100"));
        Assert.Equal(new OutboundCallerId.Choice("+18005550100", true), OutboundCallerId.Resolve(null, "8005550100"));
        Assert.Equal(new OutboundCallerId.Choice(null, true), OutboundCallerId.Resolve(null, null));
    }

    [Fact]
    public void SplitStates_ListEveryZone()
    {
        Assert.Equal(["America/Los_Angeles", "America/Denver"], StateTimeZones.For("or"));
        Assert.Equal(["America/Chicago"], StateTimeZones.For("IL"));
        Assert.Null(StateTimeZones.For("XX"));
        Assert.Null(StateTimeZones.For(null));
    }

    private static DateTimeOffset Utc(int hour, int minute = 0) => new(2026, 10, 5, hour, minute, 0, TimeSpan.Zero);

    [Fact]
    public void Window_InsideAndOutside_CalleeLocalTime()
    {
        string[] pacific = ["America/Los_Angeles"];   // PDT = UTC-7 in October
        Assert.True(OutboundCallingWindow.Check(new(8, 0), new(21, 0), pacific, Utc(16)).Allowed);   // 9:00 AM
        var early = OutboundCallingWindow.Check(new(8, 0), new(21, 0), pacific, Utc(14, 30));        // 7:30 AM
        Assert.False(early.Allowed);
        Assert.Contains("7:30 AM", early.Reason);
        Assert.False(OutboundCallingWindow.Check(new(8, 0), new(21, 0), pacific, Utc(4)).Allowed);   // 9:00 PM — end is exclusive
    }

    [Fact]
    public void Window_SplitState_StrictestZoneWins()
    {
        // 14:30 UTC = 8:30 AM Mountain but 7:30 AM Pacific — Oregon could be either, so not yet.
        Assert.False(OutboundCallingWindow.Check(new(8, 0), new(21, 0), StateTimeZones.For("OR")!, Utc(14, 30)).Allowed);
        Assert.True(OutboundCallingWindow.Check(new(8, 0), new(21, 0), StateTimeZones.For("OR")!, Utc(15, 30)).Allowed);
    }

    [Fact]
    public void CampaignHours_CanNarrowTheTcpaWindow_NeverWidenIt()
    {
        var campaign = Campaign.Create(Guid.NewGuid(), Guid.NewGuid(), "C", "c");
        Assert.Equal(new TimeOnly(8, 0), campaign.EffectiveOutboundHoursStart);
        Assert.Equal(new TimeOnly(21, 0), campaign.EffectiveOutboundHoursEnd);

        campaign.SetOutboundHours(new(9, 0), new(20, 0));
        Assert.Equal(new TimeOnly(9, 0), campaign.EffectiveOutboundHoursStart);

        Assert.Throws<ArgumentException>(() => campaign.SetOutboundHours(new(7, 0), new(20, 0)));
        Assert.Throws<ArgumentException>(() => campaign.SetOutboundHours(new(9, 0), new(22, 0)));
        Assert.Throws<ArgumentException>(() => campaign.SetOutboundHours(new(12, 0), new(10, 0)));
        Assert.Throws<ArgumentException>(() => campaign.SetOutboundHours(new(9, 0), null));
        campaign.SetOutboundHours(null, null);
        Assert.Null(campaign.OutboundHoursStart);
    }

    [Fact]
    public void ManualOutboundRecord_CarriesClientCampaignAndCallerId()
    {
        var client = Guid.NewGuid();
        var campaign = Guid.NewGuid();
        var agent = Guid.NewGuid();
        var r = CallRecord.CreateManualOutbound(Guid.NewGuid(), agent, "+15416413898", "+18005550100", client, campaign);

        Assert.Equal((client, campaign, agent), (r.ClientId, r.CampaignId, r.AgentId!.Value));
        Assert.Equal("+15416413898", r.CallerId);
        Assert.Equal("+18005550100", r.Dnis);
        Assert.Equal(CallSource.Outbound, r.Source);
        Assert.True(r.IsProductionRun);
    }
}
