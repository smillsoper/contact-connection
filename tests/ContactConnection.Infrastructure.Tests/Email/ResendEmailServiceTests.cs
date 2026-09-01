using System.Net;
using System.Text.Json;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Email;

/// <summary>
/// ResendEmailService.SendAsync(EmailMessage) — the payload mapping added for tf_voicemail
/// delivery: cc/bcc lists, an overridable From display name (address stays the verified sender),
/// and base64 attachments.
/// </summary>
public class ResendEmailServiceTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public JsonElement Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var json = await request.Content!.ReadAsStringAsync(ct);
            Body = JsonSerializer.Deserialize<JsonElement>(json);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"e1\"}") };
        }
    }

    private static (ResendEmailService svc, CapturingHandler handler) NewService()
    {
        var handler = new CapturingHandler();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("Resend")).Returns(() => new HttpClient(handler));

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Resend:ApiKey"]      = "re_test",
            ["Resend:FromAddress"] = "ContactConnection <noreply@contactconnection.io>",
        }).Build();

        return (new ResendEmailService(factory.Object, config, NullLogger<ResendEmailService>.Instance), handler);
    }

    [Fact]
    public async Task SendAsync_MapsRecipients_FromName_ReplyTo_AndAttachment()
    {
        var (svc, handler) = NewService();

        await svc.SendAsync(new EmailMessage
        {
            To          = ["to@x.com"],
            Cc          = ["cc1@x.com", "cc2@x.com"],
            Bcc         = ["bcc@x.com"],
            FromName    = "Support Voicemail",
            ReplyTo     = "reply@x.com",
            Subject     = "New voicemail",
            HtmlBody    = "<p>hi</p>",
            Attachments = [new EmailAttachment("vm.wav", [0xDE, 0xAD, 0xBE, 0xEF], "audio/wav")],
        });

        var b = handler.Body;
        Assert.Equal("Support Voicemail <noreply@contactconnection.io>", b.GetProperty("from").GetString());
        Assert.Equal("to@x.com", b.GetProperty("to")[0].GetString());
        Assert.Equal(2, b.GetProperty("cc").GetArrayLength());
        Assert.Equal("bcc@x.com", b.GetProperty("bcc")[0].GetString());
        Assert.Equal("reply@x.com", b.GetProperty("reply_to").GetString());

        var att = b.GetProperty("attachments")[0];
        Assert.Equal("vm.wav", att.GetProperty("filename").GetString());
        Assert.Equal(Convert.ToBase64String([0xDE, 0xAD, 0xBE, 0xEF]), att.GetProperty("content").GetString());
        Assert.Equal("audio/wav", att.GetProperty("content_type").GetString());
    }

    [Fact]
    public async Task SendAsync_NoFromName_KeepsConfiguredSender_AndOmitsEmptyOptionalFields()
    {
        var (svc, handler) = NewService();

        await svc.SendAsync(new EmailMessage { To = ["to@x.com"], Subject = "s", HtmlBody = "b" });

        var b = handler.Body;
        Assert.Equal("ContactConnection <noreply@contactconnection.io>", b.GetProperty("from").GetString());
        Assert.False(b.TryGetProperty("cc", out _));
        Assert.False(b.TryGetProperty("bcc", out _));
        Assert.False(b.TryGetProperty("reply_to", out _));
        Assert.False(b.TryGetProperty("attachments", out _));
    }

    [Fact]
    public async Task SendAsync_NoRecipients_Throws()
    {
        var (svc, _) = NewService();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.SendAsync(new EmailMessage { Subject = "s", HtmlBody = "b" }));
    }

    [Fact]
    public async Task LegacySendAsync_StillWorks_AsSingleRecipient()
    {
        var (svc, handler) = NewService();
        await svc.SendAsync("solo@x.com", "subj", "<p>body</p>");

        Assert.Equal("solo@x.com", handler.Body.GetProperty("to")[0].GetString());
        Assert.Equal("subj", handler.Body.GetProperty("subject").GetString());
    }
}
