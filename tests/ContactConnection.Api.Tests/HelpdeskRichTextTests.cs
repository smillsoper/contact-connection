using ContactConnection.Api.Endpoints;
using Xunit;

namespace ContactConnection.Api.Tests;

/// <summary>S184: help desk topics are cleaned before agents see them.</summary>
public class HelpdeskRichTextTests
{
    [Fact]
    public void Headings_lists_rules_and_links_survive()
    {
        var r = HelpdeskRichText.Clean("<h2>Returns</h2><p>Within <strong>30 days</strong>, <a href=\"https://example.com/r\">see policy</a></p><ol><li>Box it</li></ol><hr>");
        Assert.Contains("<h2>Returns</h2>", r.Html);
        Assert.Contains("href=\"https://example.com/r\"", r.Html);
        Assert.Contains("<hr>", r.Html);
        Assert.Equal("Returns Within 30 days, see policy Box it", r.Text);
    }

    [Fact]
    public void Scripts_frames_handlers_and_unsafe_links_are_removed()
    {
        var r = HelpdeskRichText.Clean("<p onclick=\"x()\">hi <a href=\"javascript:alert(1)\">bad</a></p><script>alert(2)</script><iframe src=\"https://evil\"></iframe>");
        Assert.DoesNotContain("onclick", r.Html);
        Assert.DoesNotContain("javascript", r.Html);
        Assert.DoesNotContain("script", r.Html);
        Assert.DoesNotContain("iframe", r.Html);
        Assert.DoesNotContain("alert", r.Text);
    }

    [Fact]
    public void Images_are_kept_only_as_uploaded_file_references()
    {
        var id = Guid.NewGuid();
        var r = HelpdeskRichText.Clean($"<img src=\"https://evil/x.png\"><img src=\"blob:local\" data-hd-file=\"{id}\"><img data-hd-file=\"nope\">");
        Assert.Equal([id], r.ImageIds);
        Assert.Contains($"data-hd-file=\"{id}\"", r.Html);
        Assert.DoesNotContain("src=", r.Html);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(r.Html, "<img"));
    }
}
