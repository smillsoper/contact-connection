using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.FlowEngine;
using ContactConnection.Infrastructure.Telephony;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony;

/// <summary>
/// tf_voicemail email delivery — recipient parsing and building the EmailMessage from the node's
/// delivery config, with every text field run through the real variable resolver.
/// </summary>
public class VoicemailEmailTests
{
    private static readonly IVariableResolver Resolver = new VariableResolver();

    private static VariableContext Ctx()
    {
        var c = new VariableContext();
        c.Caller["phone"] = "+15551234567";
        c.CallRecord["id"] = "call-123";
        c.FlowVars["queue_name"] = "Support";
        return c;
    }

    private static EmailAttachment Wav() => new("voicemail.wav", [1, 2, 3], "audio/wav");

    // ── ParseRecipients ────────────────────────────────────────────────────

    [Theory]
    [InlineData("a@x.com, b@y.com;c@z.com", 3)]
    [InlineData("  a@x.com \n b@y.com ", 2)]
    [InlineData("a@x.com, a@X.com", 1)]           // case-insensitive dedupe
    [InlineData("not-an-email, real@x.com", 1)]   // must contain '@'
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void ParseRecipients_SplitsDedupesAndFilters(string? raw, int expectedCount)
    {
        Assert.Equal(expectedCount, VoicemailEmail.ParseRecipients(raw).Count);
    }

    // ── Build ──────────────────────────────────────────────────────────────

    [Fact]
    public void Build_DeliveryDisabled_ReturnsNull()
    {
        var node = new JsonObject { ["deliveryEmailEnabled"] = false, ["deliveryEmailTo"] = "a@x.com" };
        Assert.Null(VoicemailEmail.Build(node, Resolver, Ctx(), Wav()));
    }

    [Fact]
    public void Build_NoRecipients_ReturnsNull()
    {
        var node = new JsonObject { ["deliveryEmailEnabled"] = true, ["deliveryEmailTo"] = "   " };
        Assert.Null(VoicemailEmail.Build(node, Resolver, Ctx(), Wav()));
    }

    [Fact]
    public void Build_ResolvesTemplatesAndSplitsAllRecipientLists()
    {
        var node = new JsonObject
        {
            ["deliveryEmailEnabled"]  = true,
            ["deliveryEmailTo"]       = "sales@x.com, lead@x.com",
            ["deliveryEmailCc"]       = "mgr@x.com",
            ["deliveryEmailBcc"]      = "audit@x.com",
            ["deliveryEmailFromName"] = "{{flow.queue_name}} Voicemail",
            ["deliveryEmailReplyTo"]  = "support@x.com",
            ["deliveryEmailSubject"]  = "VM from {{caller.phone}}",
            ["deliveryEmailBodyHtml"] = "<p>Call {{call_record.id}} — queue {{flow.queue_name}}</p>",
        };

        var msg = VoicemailEmail.Build(node, Resolver, Ctx(), Wav());

        Assert.NotNull(msg);
        Assert.Equal(new[] { "sales@x.com", "lead@x.com" }, msg!.To);
        Assert.Equal(new[] { "mgr@x.com" }, msg.Cc);
        Assert.Equal(new[] { "audit@x.com" }, msg.Bcc);
        Assert.Equal("Support Voicemail", msg.FromName);
        Assert.Equal("support@x.com", msg.ReplyTo);
        Assert.Equal("VM from +15551234567", msg.Subject);
        Assert.Equal("<p>Call call-123 — queue Support</p>", msg.HtmlBody);
        Assert.Single(msg.Attachments);
    }

    [Fact]
    public void Build_DefaultSubject_WhenNotConfigured()
    {
        var node = new JsonObject { ["deliveryEmailEnabled"] = true, ["deliveryEmailTo"] = "a@x.com" };
        var msg = VoicemailEmail.Build(node, Resolver, Ctx(), Wav());
        Assert.Equal("New voicemail from +15551234567", msg!.Subject);
    }

    [Fact]
    public void Build_AttachAudioFalse_OmitsAttachment()
    {
        var node = new JsonObject
        {
            ["deliveryEmailEnabled"] = true,
            ["deliveryEmailTo"]      = "a@x.com",
            ["deliveryAttachAudio"]  = false,
        };
        var msg = VoicemailEmail.Build(node, Resolver, Ctx(), Wav());
        Assert.Empty(msg!.Attachments);
    }

    [Fact]
    public void Build_CcOnly_NoTo_IsStillValid()
    {
        var node = new JsonObject
        {
            ["deliveryEmailEnabled"] = true,
            ["deliveryEmailCc"]      = "mgr@x.com",
        };
        var msg = VoicemailEmail.Build(node, Resolver, Ctx(), Wav());
        Assert.NotNull(msg);
        Assert.Empty(msg!.To);
        Assert.True(msg.HasRecipients);
    }
}
