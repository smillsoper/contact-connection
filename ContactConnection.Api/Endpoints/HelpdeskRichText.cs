using AngleSharp.Dom;
using Ganss.Xss;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// Cleans a help desk topic's formatted content (S184) — the chat rules plus headings and rules: basic formatting,
/// colour, size, font, lists, links (http/https/mailto; agents get them in a new window), images only as references to
/// uploaded help desk files. Scripts, event handlers, iframes and outside images are removed.
/// </summary>
public static class HelpdeskRichText
{
    private static readonly HtmlSanitizer Sanitizer = Build();

    private static HtmlSanitizer Build()
    {
        var s = new HtmlSanitizer();
        s.AllowedTags.Clear();
        foreach (var t in new[] { "p", "br", "strong", "b", "em", "i", "u", "s", "span", "mark", "ul", "ol", "li", "a", "img",
                                  "code", "pre", "blockquote", "h2", "h3", "h4", "hr" })
            s.AllowedTags.Add(t);
        s.AllowedAttributes.Clear();
        foreach (var a in new[] { "style", "href", "data-hd-file", "data-color" })
            s.AllowedAttributes.Add(a);
        s.AllowedCssProperties.Clear();
        foreach (var c in new[] { "color", "background-color", "font-size", "font-family", "text-align" })
            s.AllowedCssProperties.Add(c);
        s.AllowedSchemes.Clear();
        foreach (var sc in new[] { "http", "https", "mailto" }) s.AllowedSchemes.Add(sc);
        s.AllowDataAttributes = false;
        s.KeepChildNodes = true;
        s.RemovingTag += (_, e) =>
        {
            if (e.Tag.LocalName is "script" or "style" or "template" or "noscript" or "iframe" or "object" or "textarea" or "svg" or "math")
                e.Tag.TextContent = "";
        };
        return s;
    }

    public sealed record Result(string Html, string Text, List<Guid> ImageIds);

    public static Result Clean(string html)
    {
        var images = new List<Guid>();
        var doc = Sanitizer.SanitizeDom(html ?? "");
        foreach (var img in doc.QuerySelectorAll("img").ToList())
        {
            img.RemoveAttribute("src");
            img.RemoveAttribute("style");
            if (Guid.TryParse(img.GetAttribute("data-hd-file"), out var id)) { if (!images.Contains(id)) images.Add(id); }
            else img.Remove();
        }
        var body = doc.Body ?? (IElement)doc.DocumentElement;
        var sb = new System.Text.StringBuilder();
        Words(body, sb);
        var text = string.Join(" ", sb.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return new Result(body.InnerHtml.Trim(), text, images);
    }

    private static readonly HashSet<string> Blocks = ["p", "li", "br", "ul", "ol", "blockquote", "pre", "h2", "h3", "h4", "hr"];

    private static void Words(INode node, System.Text.StringBuilder sb)
    {
        foreach (var child in node.ChildNodes)
        {
            if (child is IText t) sb.Append(t.Data);
            else if (child is IElement e)
            {
                Words(e, sb);
                if (Blocks.Contains(e.LocalName)) sb.Append(' ');
            }
        }
    }
}
