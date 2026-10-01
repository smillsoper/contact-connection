using ContactConnection.Domain.Entities;
using Xunit;

namespace ContactConnection.Domain.Tests.Domain;

/// <summary>How a call's overall status is derived when it ends (S169: script-less outbound dials are complete).</summary>
public class OverallStatusTests
{
    [Fact]
    public void OutboundDial_WithNoScript_EndsComplete()
    {
        var record = CallRecord.CreateOutbound(Guid.NewGuid(), "+15551234567", Guid.NewGuid());
        record.Complete();
        Assert.Equal(CallRecordStatus.Complete, record.OverallStatus);
    }

    [Fact]
    public void OutboundDial_WithAnUnfinishedScript_EndsIncomplete()
    {
        var record = CallRecord.CreateOutbound(Guid.NewGuid(), "+15551234567", Guid.NewGuid());
        record.AddInteraction(InteractionType.OrderSale);
        record.Complete();
        Assert.Equal(CallRecordStatus.Incomplete, record.OverallStatus);
    }

    [Fact]
    public void InboundCall_WithNoScript_StillEndsIncomplete()
    {
        var record = CallRecord.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CallSource.Inbound, "+15551234567");
        record.Complete();
        Assert.Equal(CallRecordStatus.Incomplete, record.OverallStatus);
    }
}
