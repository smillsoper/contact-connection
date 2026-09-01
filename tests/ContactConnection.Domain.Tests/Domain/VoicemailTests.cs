using ContactConnection.Domain.Entities;
using Xunit;

namespace ContactConnection.Domain.Tests.Domain;

/// <summary>
/// Voicemail entity — captured by a tf_voicemail telephony node, a child of the call record.
/// Covers the storage-key derivation, the optional email-delivery outcome stamp, and the
/// review lifecycle (new → heard → archived, and restore). See ARCHITECTURE.md §14.
/// </summary>
public class VoicemailTests
{
    private static Voicemail New(Guid? callId = null) =>
        Voicemail.Create(Guid.NewGuid(), callId ?? Guid.NewGuid(), Guid.NewGuid(), "+15551234567", 42);

    [Fact]
    public void Create_SetsDefaults_AndDerivesStorageKeyFromOwnId()
    {
        var callId = Guid.NewGuid();
        var vm = Voicemail.Create(Guid.NewGuid(), callId, Guid.NewGuid(), "+15551110000", 30);

        Assert.Equal(VoicemailStatus.New, vm.Status);
        Assert.Equal(30, vm.DurationSeconds);
        Assert.Equal($"voicemails/{callId}/{vm.Id}.wav", vm.StorageKey);
        Assert.Null(vm.EmailDeliveryStatus);
        Assert.Null(vm.HeardAt);
        Assert.Null(vm.ArchivedAt);
    }

    [Fact]
    public void Create_NegativeDuration_ClampsToZero()
    {
        var vm = Voicemail.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, -5);
        Assert.Equal(0, vm.DurationSeconds);
    }

    [Fact]
    public void RecordEmailDelivery_Sent_StampsDeliveredAt()
    {
        var vm = New();
        vm.RecordEmailDelivery(VoicemailEmailStatus.Sent, "a@x.com, b@y.com");

        Assert.Equal(VoicemailEmailStatus.Sent, vm.EmailDeliveryStatus);
        Assert.Equal("a@x.com, b@y.com", vm.EmailDeliveredTo);
        Assert.NotNull(vm.EmailDeliveredAt);
        Assert.Null(vm.EmailDeliveryError);
    }

    [Fact]
    public void RecordEmailDelivery_Failed_KeepsErrorAndNoTimestamp()
    {
        var vm = New();
        vm.RecordEmailDelivery(VoicemailEmailStatus.Failed, "a@x.com", "smtp 500");

        Assert.Equal(VoicemailEmailStatus.Failed, vm.EmailDeliveryStatus);
        Assert.Equal("smtp 500", vm.EmailDeliveryError);
        Assert.Null(vm.EmailDeliveredAt);
    }

    [Fact]
    public void RecordEmailDelivery_UnknownStatus_FallsBackToFailed()
    {
        var vm = New();
        vm.RecordEmailDelivery("weird", null);
        Assert.Equal(VoicemailEmailStatus.Failed, vm.EmailDeliveryStatus);
    }

    [Fact]
    public void MarkHeard_FirstListen_SetsHeard_RepeatDoesNotMoveTimestamp()
    {
        var vm = New();
        var agent1 = Guid.NewGuid();
        var agent2 = Guid.NewGuid();

        vm.MarkHeard(agent1);
        var firstAt = vm.HeardAt;

        Assert.Equal(VoicemailStatus.Heard, vm.Status);
        Assert.Equal(agent1, vm.HeardBy);
        Assert.NotNull(firstAt);

        vm.MarkHeard(agent2);
        Assert.Equal(firstAt, vm.HeardAt);
        Assert.Equal(agent1, vm.HeardBy);   // first listener sticks
    }

    [Fact]
    public void Archive_SetsStatusAndTimestamp()
    {
        var vm = New();
        vm.Archive();
        Assert.Equal(VoicemailStatus.Archived, vm.Status);
        Assert.NotNull(vm.ArchivedAt);
    }

    [Fact]
    public void Restore_ReturnsToNewIfNeverHeard_HeardOtherwise()
    {
        var neverHeard = New();
        neverHeard.Archive();
        neverHeard.Restore();
        Assert.Equal(VoicemailStatus.New, neverHeard.Status);

        var wasHeard = New();
        wasHeard.MarkHeard(Guid.NewGuid());
        wasHeard.Archive();
        wasHeard.Restore();
        Assert.Equal(VoicemailStatus.Heard, wasHeard.Status);
    }

    [Fact]
    public void SetTranscription_Stored()
    {
        var vm = New();
        vm.SetTranscription("hi this is a test");
        Assert.Equal("hi this is a test", vm.Transcription);
    }
}
