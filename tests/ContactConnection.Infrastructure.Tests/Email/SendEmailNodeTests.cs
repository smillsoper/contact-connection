using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Common;
using ContactConnection.Infrastructure.Telephony.NodeHandlers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Email;

/// <summary>FlowEmail (shared by tf_voicemail delivery, tf_send_email and CRM send_email) and the
/// telephony send-email node: templating, recipient parsing, non-blocking send.</summary>
public class SendEmailNodeTests
{
    [Fact]
    public void Compose_ResolvesEveryField_AndSplitsRecipients()
    {
        var node = new JsonObject
        {
            ["emailTo"] = "mod@client.com, {{flow.ops}}", ["emailCc"] = "", ["emailBcc"] = "audit@client.com",
            ["emailFromName"] = "NeuroQ Line", ["emailReplyTo"] = " team@client.com ",
            ["emailSubject"] = "Queued: {{caller.ani}}", ["emailBodyHtml"] = "<p>{{caller.ani}}</p>",
        };
        string Resolve(string t) => t.Replace("{{flow.ops}}", "ops@client.com; mod@client.com").Replace("{{caller.ani}}", "5035551234");

        var m = FlowEmail.Compose(node, "email", Resolve, "default")!;

        Assert.Equal(["mod@client.com", "ops@client.com"], m.To);   // de-duplicated, case-insensitive
        Assert.Equal(["audit@client.com"], m.Bcc);
        Assert.Equal(("NeuroQ Line", "team@client.com"), (m.FromName, m.ReplyTo));
        Assert.Equal("Queued: 5035551234", m.Subject);
        Assert.Equal("<p>5035551234</p>", m.HtmlBody);
        Assert.Empty(m.Attachments);
    }

    [Fact]
    public void Compose_NoRecipients_ReturnsNull_AndBlankSubjectUsesDefault()
    {
        Assert.Null(FlowEmail.Compose(new JsonObject { ["emailTo"] = "{{flow.unset}}" }, "email", _ => "", "d"));
        var m = FlowEmail.Compose(new JsonObject { ["emailTo"] = "a@b.com" }, "email", t => t, "Default subject");
        Assert.Equal("Default subject", m!.Subject);
    }

    [Fact]
    public async Task TelephonyNode_SendsInBackground_AndContinuesOnDefault()
    {
        var sent = new TaskCompletionSource<EmailMessage>();
        var email = new Mock<IEmailService>();
        email.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Callback<EmailMessage, CancellationToken>((m, _) => sent.TrySetResult(m))
            .Returns(Task.CompletedTask);
        var handler = new SendEmailNodeHandler(email.Object, NullLogger<SendEmailNodeHandler>.Instance);

        var ctx = new TelephonyFlowContext
        {
            ChannelUuid = "u1", CallerNumber = "+15035551234", DestinationNumber = "+18005550142",
            TenantId = Guid.NewGuid(), CampaignId = Guid.NewGuid(), CallRecordId = Guid.NewGuid(),
            TenantSubdomain = "t", TenantSchemaName = "tenant_t", TenantTimezone = "America/Chicago",
        };
        ctx.Vars["mod_email"] = "mod@client.com";
        var node = new JsonObject
        {
            ["type"] = "tf_send_email", ["emailTo"] = "{{flow.mod_email}}", ["emailSubject"] = "No agents — {{caller.ani}}",
            ["transitions"] = new JsonObject { ["default"] = "next" },
        };

        var result = await handler.ExecuteAsync(node, ctx);
        Assert.Equal(("next", "default"), (result.NextNodeId, result.TransitionTaken));

        var message = await sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(["mod@client.com"], message.To);
        Assert.Contains("5035551234", message.Subject);
    }

    [Fact]
    public async Task TelephonyNode_SendFailure_NeverBreaksTheCall()
    {
        var email = new Mock<IEmailService>();
        email.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("provider down"));
        var handler = new SendEmailNodeHandler(email.Object, NullLogger<SendEmailNodeHandler>.Instance);
        var ctx = new TelephonyFlowContext
        {
            ChannelUuid = "u2", CallerNumber = "1", DestinationNumber = "2", TenantId = Guid.NewGuid(),
            CampaignId = Guid.NewGuid(), CallRecordId = Guid.NewGuid(), TenantSubdomain = "t",
            TenantSchemaName = "tenant_t", TenantTimezone = "UTC",
        };
        var node = new JsonObject { ["emailTo"] = "a@b.com", ["transitions"] = new JsonObject { ["default"] = "next" } };

        Assert.Equal("next", (await handler.ExecuteAsync(node, ctx)).NextNodeId);
    }
}
