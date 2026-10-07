using ContactConnection.Api.Endpoints;
using ContactConnection.Domain.Entities;
using Xunit;

namespace ContactConnection.Api.Tests;

/// <summary>S183: formatted chat messages are cleaned before anyone else sees them.</summary>
public class ChatRichTextTests
{
    [Fact]
    public void Basic_formatting_survives()
    {
        var r = ChatRichText.Clean("<p><strong>Bold</strong> <em>it</em> <u>u</u> <span style=\"color: #dc2626; font-size: 18px\">red</span></p><ul><li>one</li></ul>");
        Assert.Contains("<strong>Bold</strong>", r.Html);
        Assert.Contains("color: rgba(220, 38, 38, 1)", r.Html.Replace("#dc2626", "rgba(220, 38, 38, 1)"));
        Assert.Contains("<li>one</li>", r.Html);
        Assert.Equal("Bold it u red one", r.Text);
    }

    [Fact]
    public void Scripts_handlers_and_frames_are_removed()
    {
        var r = ChatRichText.Clean("<p onclick=\"steal()\">hi<script>alert(1)</script><iframe src=\"https://evil\"></iframe></p><a href=\"javascript:alert(1)\">x</a>");
        Assert.DoesNotContain("script", r.Html);
        Assert.DoesNotContain("onclick", r.Html);
        Assert.DoesNotContain("iframe", r.Html);
        Assert.DoesNotContain("javascript", r.Html);
        Assert.DoesNotContain("alert", r.Html);     // the script's text goes too
        Assert.Equal("hi x", r.Text);
    }

    [Fact]
    public void Disallowed_styles_are_dropped()
    {
        var r = ChatRichText.Clean("<span style=\"position: fixed; top: 0; background-image: url(https://x)\">a</span>");
        Assert.DoesNotContain("position", r.Html);
        Assert.DoesNotContain("url(", r.Html);
    }

    [Fact]
    public void Images_are_only_kept_as_upload_references()
    {
        var id = Guid.NewGuid();
        var r = ChatRichText.Clean($"<p><img src=\"https://tracker/pixel.gif\"><img src=\"data:image/png;base64,AAAA\"><img src=\"blob:x\" data-chat-file=\"{id}\"></p>");
        Assert.Equal([id], r.FileIds);
        Assert.DoesNotContain("tracker", r.Html);
        Assert.DoesNotContain("base64", r.Html);
        Assert.DoesNotContain("src=", r.Html);
        Assert.Contains($"data-chat-file=\"{id}\"", r.Html);
    }

    [Fact]
    public void Mentions_survive_and_are_parsed()
    {
        var bob = Guid.NewGuid();
        var r = ChatRichText.Clean($"<p>hey <span data-mention=\"{bob}\">@Bob</span> and <span data-mention=\"nope\">x</span></p>");
        Assert.Equal([bob], ChatMessage.ParseHtmlMentions(r.Html));
        Assert.Contains("@Bob", r.Text);
    }

    [Fact]
    public void Rich_message_rules()
    {
        var m = ChatMessage.CreateRich(Guid.NewGuid(), Guid.NewGuid(), "<p><img data-chat-file=\"x\"></p>", "", hasImages: true, null);
        Assert.Equal(ChatMessageFormat.Html, m.Format);
        Assert.Equal("[image]", m.BodyText);
        Assert.Throws<ArgumentException>(() => ChatMessage.CreateRich(Guid.NewGuid(), Guid.NewGuid(), "<p></p>", "  ", false, null));
    }

    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "image/png")]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg")]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, "image/gif")]
    [InlineData(new byte[] { 0x3C, 0x73, 0x76, 0x67 }, null)]   // "<svg" — never served as an image
    public void Image_type_comes_from_the_bytes(byte[] head, string? expected) =>
        Assert.Equal(expected, ChatFile.SniffImageType(head));
}
