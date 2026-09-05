using ContactConnection.Infrastructure.Telephony;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony;

/// <summary>
/// TelephonyAudioResolver.ResolvePlatformPhraseArg — the "__platform:{voice}/{phrase}" branch that
/// maps a platform phrase-library reference to a committed OGG under _platform/ on the shared
/// sounds volume (no DB, no tenant schema segment).
/// </summary>
public class TelephonyAudioResolverTests
{
    private static IConfiguration Config(string? soundsContainerPath = null)
    {
        var dict = new Dictionary<string, string?>();
        if (soundsContainerPath is not null)
            dict["FreeSWITCH:SoundsContainerPath"] = soundsContainerPath;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public void ResolvePlatformPhraseArg_ValidRef_ExpandsToPlatformOggPath()
    {
        var result = TelephonyAudioResolver.ResolvePlatformPhraseArg(Config(), "will/hold_please_hold");

        Assert.Equal(
            "/usr/share/freeswitch/sounds/contactconnection/_platform/will/hold_please_hold.ogg",
            result);
    }

    [Fact]
    public void ResolvePlatformPhraseArg_HonoursConfiguredContainerBase()
    {
        var result = TelephonyAudioResolver.ResolvePlatformPhraseArg(
            Config("/custom/sounds"), "annie/callback_greeting");

        Assert.Equal("/custom/sounds/_platform/annie/callback_greeting.ogg", result);
    }

    [Fact]
    public void ResolvePlatformPhraseArg_ToleratesLeadingOrTrailingSlash()
    {
        var result = TelephonyAudioResolver.ResolvePlatformPhraseArg(Config(), "/effy/vm_message_received/");

        Assert.Equal(
            "/usr/share/freeswitch/sounds/contactconnection/_platform/effy/vm_message_received.ogg",
            result);
    }

    [Theory]
    [InlineData("will")]                        // missing phrase segment
    [InlineData("will/phrase/extra")]           // too many segments
    [InlineData("../etc/passwd")]               // traversal — '.' and '/' not in [a-z0-9_]
    [InlineData("will/../../secret")]           // traversal
    [InlineData("Will/Hold")]                   // uppercase rejected
    [InlineData("will/hold please")]            // space rejected
    [InlineData("")]                            // empty
    public void ResolvePlatformPhraseArg_RejectsMalformedOrUnsafeRef(string voiceAndPhrase)
    {
        Assert.Null(TelephonyAudioResolver.ResolvePlatformPhraseArg(Config(), voiceAndPhrase));
    }
}
