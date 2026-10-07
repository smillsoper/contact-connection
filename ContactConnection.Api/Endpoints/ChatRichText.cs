using AngleSharp.Dom;
using Ganss.Xss;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// Cleans a formatted chat message (S183). Chat is person-to-person, so the composer's HTML is never trusted: only basic
/// formatting survives (bold / italic / underline / strike, colour, highlight, size, font, lists, links, line breaks),
/// images are kept only as references to uploaded chat files (any src is dropped — no external or data: images, no
/// tracking pixels), and mentions only as data-mention spans. Everything else — scripts, event handlers, iframes, styles
/// beyond the few allowed — is removed.
/// </summary>
public static class ChatRichText
{
    private static readonly HtmlSanitizer Sanitizer = Build();

    private static HtmlSanitizer Build()
    {
        var s = new HtmlSanitizer();
        s.AllowedTags.Clear();
        foreach (var t in new[] { "p", "br", "strong", "b", "em", "i", "u", "s", "span", "mark", "ul", "ol", "li", "a", "img", "code", "pre", "blockquote" })
            s.AllowedTags.Add(t);
        s.AllowedAttributes.Clear();
        foreach (var a in new[] { "style", "href", "data-chat-file", "data-mention", "data-color" })
            s.AllowedAttributes.Add(a);
        s.AllowedCssProperties.Clear();
        foreach (var c in new[] { "color", "background-color", "font-size", "font-family" })
            s.AllowedCssProperties.Add(c);
        s.AllowedSchemes.Clear();
        foreach (var sc in new[] { "http", "https", "mailto" }) s.AllowedSchemes.Add(sc);
        s.AllowDataAttributes = false;
        s.KeepChildNodes = true;   // a removed wrapper keeps its text…
        // …except code-like containers, whose contents are never text worth keeping.
        s.RemovingTag += (_, e) =>
        {
            if (e.Tag.LocalName is "script" or "style" or "template" or "noscript" or "iframe" or "object" or "textarea" or "svg" or "math")
                e.Tag.TextContent = "";
        };
        return s;
    }

    public sealed record Result(string Html, string Text, List<Guid> FileIds);

    /// <summary>The cleaned HTML, its words, and the chat files it references (in order).</summary>
    public static Result Clean(string html)
    {
        var fileIds = new List<Guid>();
        var doc = Sanitizer.SanitizeDom(html ?? "");
        foreach (var img in doc.QuerySelectorAll("img").ToList())
        {
            // Images exist only as references to our own uploads.
            img.RemoveAttribute("src");
            img.RemoveAttribute("style");
            if (Guid.TryParse(img.GetAttribute("data-chat-file"), out var id)) { if (!fileIds.Contains(id)) fileIds.Add(id); }
            else img.Remove();
        }
        foreach (var el in doc.QuerySelectorAll("[data-mention]").ToList())
            if (el.LocalName != "span" || !Guid.TryParse(el.GetAttribute("data-mention"), out _)) el.RemoveAttribute("data-mention");
        var body = doc.Body ?? (IElement)doc.DocumentElement;
        var sb = new System.Text.StringBuilder();
        Words(body, sb);
        var text = string.Join(" ", sb.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return new Result(body.InnerHtml.Trim(), text, fileIds);
    }

    private static readonly HashSet<string> Blocks = ["p", "li", "br", "ul", "ol", "blockquote", "pre"];

    /// <summary>The text, with a break between paragraphs / list items so their words don't run together.</summary>
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
