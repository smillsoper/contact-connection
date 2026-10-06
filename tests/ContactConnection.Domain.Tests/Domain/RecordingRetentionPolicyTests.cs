using ContactConnection.Domain.Entities;
using Xunit;

namespace ContactConnection.Domain.Tests.Domain;

/// <summary>"Always record, retain by disposition" (S181) — Stephen's rules.</summary>
public class RecordingRetentionPolicyTests
{
    private const int Campaign = 90;
    private static InteractionRecordingRule Rule(string? action, int? days) => new(true, action, days);
    private static readonly InteractionRecordingRule Unmapped = new(false, null, null);

    [Fact]
    public void MappedWithNoRule_KeepsForTheCampaignsPeriod() =>
        Assert.Equal(new RecordingDecision(RecordingKeep.Keep, 90), RecordingRetentionPolicy.Decide(Campaign, null, [Rule(null, null)]));

    [Fact]
    public void KeepForNDays_OverridesTheCampaign() =>
        Assert.Equal(new RecordingDecision(RecordingKeep.Keep, 365), RecordingRetentionPolicy.Decide(Campaign, null, [Rule(RecordingKeep.Keep, 365)]));

    [Fact]
    public void Discard_AndConversationOnly()
    {
        Assert.Equal(RecordingKeep.Discard, RecordingRetentionPolicy.Decide(Campaign, null, [Rule(RecordingKeep.Discard, null)]).Action);
        Assert.Equal(new RecordingDecision(RecordingKeep.Conversation, 30),
            RecordingRetentionPolicy.Decide(Campaign, null, [Rule(RecordingKeep.Conversation, 30)]));
    }

    [Fact]
    public void MissingOrUnmapped_UsesItsOwnPeriod_ElseTheCampaigns()
    {
        Assert.Equal(new RecordingDecision(RecordingKeep.Keep, 14), RecordingRetentionPolicy.Decide(Campaign, 14, [Unmapped]));
        Assert.Equal(new RecordingDecision(RecordingKeep.Keep, 90), RecordingRetentionPolicy.Decide(Campaign, null, [Unmapped]));
        Assert.Equal(new RecordingDecision(RecordingKeep.Keep, 14), RecordingRetentionPolicy.Decide(Campaign, 14, []));   // abandoned before an agent
    }

    /// <summary>Transfers: keep if ANY interaction keeps (whole call beats conversation beats discard), for the LONGEST.</summary>
    [Fact]
    public void SeveralInteractions_AnyKeepWins_LongestPeriod()
    {
        Assert.Equal(new RecordingDecision(RecordingKeep.Keep, 30),
            RecordingRetentionPolicy.Decide(Campaign, null, [Rule(RecordingKeep.Discard, null), Rule(RecordingKeep.Keep, 30)]));
        Assert.Equal(new RecordingDecision(RecordingKeep.Keep, 365),
            RecordingRetentionPolicy.Decide(Campaign, null, [Rule(RecordingKeep.Conversation, 365), Rule(RecordingKeep.Keep, 30)]));
        Assert.Equal(new RecordingDecision(RecordingKeep.Conversation, 60),
            RecordingRetentionPolicy.Decide(Campaign, null, [Rule(RecordingKeep.Conversation, 60), Rule(RecordingKeep.Discard, null)]));
        Assert.Equal(RecordingKeep.Discard,
            RecordingRetentionPolicy.Decide(Campaign, null, [Rule(RecordingKeep.Discard, 400), Rule(RecordingKeep.Discard, null)]).Action);
    }

    [Fact]
    public void DispositionOverridesCategory_FieldByField()
    {
        var category = DispositionCategory.Create(Guid.NewGuid(), "Junk", null, false, false, 1);
        category.SetRecordingRule(RecordingKeep.Discard, 7);
        var plain = Disposition.Create(Guid.NewGuid(), "Wrong number", null, category.Id, null, null);
        var own = Disposition.Create(Guid.NewGuid(), "Prank", null, category.Id, null, null);
        own.SetRecordingRule(RecordingKeep.Keep, null);

        Assert.Equal(new InteractionRecordingRule(true, RecordingKeep.Discard, 7), RecordingRetentionPolicy.ForDisposition(plain, category));
        Assert.Equal(new InteractionRecordingRule(true, RecordingKeep.Keep, 7), RecordingRetentionPolicy.ForDisposition(own, category));
        Assert.Equal(new InteractionRecordingRule(false, null, null), RecordingRetentionPolicy.ForDisposition(null, null));
        Assert.Throws<ArgumentException>(() => own.SetRecordingRule("shred", null));
    }
}
