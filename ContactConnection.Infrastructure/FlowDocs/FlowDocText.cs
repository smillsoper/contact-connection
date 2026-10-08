using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace ContactConnection.Infrastructure.FlowDocs;

/// <summary>
/// Turns the designer's internals into words a client can read: <c>{{flow.billing_phone.value}}</c> becomes
/// "[Billing Phone]", <c>{{shared.CC_Capture_Success}} == true</c> becomes "[CC Capture Success] is true", and script
/// HTML becomes formatted paragraphs.
/// </summary>
public partial class FlowDocText(IReadOnlyDictionary<string, string> variableLabels, IReadOnlyDictionary<string, string> nodeLabels)
{
    [GeneratedRegex(@"\{\{\s*([^}]+?)\s*\}\}")]
    private static partial Regex Tag();

    private static readonly TextInfo Title = CultureInfo.InvariantCulture.TextInfo;

    /// <summary>"billing_phone" → "Billing Phone"; "CC_Capture_Success" stays readable.</summary>
    public static string Words(string key)
    {
        var s = key.Replace('_', ' ').Replace('-', ' ').Trim();
        // camelCase → spaced
        s = Regex.Replace(s, "(?<=[a-z])(?=[A-Z])", " ");
        return s.Length == 0 ? key : char.ToUpperInvariant(s[0]) + s[1..];
    }

    /// <summary>One <c>{{…}}</c> tag's inner text → "[Something]" (arithmetic kept: "[Attempts] + 1").</summary>
    public string Variable(string inner)
    {
        var m = Regex.Match(inner, @"^([A-Za-z_]+)\.([A-Za-z0-9_.\-]+)(.*)$");
        if (!m.Success) return $"[{Words(inner)}]";
        var (ns, path, rest) = (m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value.Trim());
        var first = path.Split('.')[0];
        string name = ns switch
        {
            "flow" or "shared" => variableLabels.TryGetValue(first, out var l) ? l : Words(first),
            "input" => nodeLabels.TryGetValue(first, out var nl) ? nl : Words(first),
            "api" => nodeLabels.TryGetValue(first, out var al) ? $"result of {al}" : $"result of {Words(first)}",
            "agent" => $"agent's {Words(path.Replace('.', ' ')).ToLowerInvariant()}",
            "caller" => $"caller's {Words(path.Replace('.', ' ')).ToLowerInvariant()}",
            "call_record" => $"call {Words(path.Replace('.', ' ')).ToLowerInvariant()}",
            "cart" => $"cart {Words(path.Replace('.', ' ')).ToLowerInvariant()}",
            "tenant" => $"company {Words(path.Replace('.', ' ')).ToLowerInvariant()}",
            _ => Words(path.Replace('.', ' ')),
        };
        return rest.Length == 0 ? $"[{name}]" : $"[{name}] {rest}";
    }

    /// <summary>Every tag in a string replaced.</summary>
    public string Text(string? s) => string.IsNullOrEmpty(s) ? "" : Tag().Replace(s, m => Variable(m.Groups[1].Value));

    /// <summary>A branch condition in words.</summary>
    public string Condition(string? condition)
    {
        if (string.IsNullOrWhiteSpace(condition)) return "(no condition set)";
        var parts = Regex.Split(condition, @"(\|\||&&)");
        return string.Join(" ", parts.Select(p => p.Trim() switch
        {
            "||" => "or",
            "&&" => "and",
            var clause => Clause(clause),
        }));
    }

    private string Clause(string clause)
    {
        var c = Text(clause);
        foreach (var (op, words) in new[] { ("!=", "is not"), ("==", "is"), (">=", "is at least"), ("<=", "is at most"), (">", "is more than"), ("<", "is less than") })
        {
            var i = c.IndexOf(op, StringComparison.Ordinal);
            if (i < 0) continue;
            var right = c[(i + op.Length)..].Trim().Trim('"', '\'');
            return $"{c[..i].Trim()} {words} {(right.Length == 0 ? "empty" : right)}";
        }
        var contains = Regex.Match(c, @"^(.*?)\s+contains\s+(.*)$", RegexOptions.IgnoreCase);
        if (contains.Success) return $"{contains.Groups[1].Value} contains {contains.Groups[2].Value.Trim('"', '\'')}";
        return $"{c} is set";
    }

    // ── Script HTML → paragraphs ──────────────────────────────────────────────

    private static readonly HtmlParser Parser = new();

    public List<DocParagraph> Html(string? html)
    {
        var result = new List<DocParagraph>();
        if (string.IsNullOrWhiteSpace(html)) return result;
        var doc = Parser.ParseDocument($"<body>{html}</body>");
        var body = doc.Body!;
        var current = new List<DocRun>();

        void Flush(bool bullet = false)
        {
            // Drop empty paragraphs (the editor leaves <p></p> spacers).
            if (current.Any(r => !string.IsNullOrWhiteSpace(r.Text)))
                result.Add(new DocParagraph(Trim(current), bullet));
            current = [];
        }

        void Walk(INode node, bool bold, bool italic, bool underline, string? color)
        {
            if (node is IText t)
            {
                var text = Regex.Replace(t.Data, @"\s+", " ");
                if (text.Length > 0) current.Add(new DocRun(Text(text), bold, italic, underline, color));
                return;
            }
            if (node is not IElement e) return;
            var tag = e.LocalName;
            if (tag is "script" or "style") return;
            if (tag == "img") { current.Add(new DocRun("[image]", Italic: true, Color: "#6b7280")); return; }
            if (tag == "br") { Flush(); return; }
            var b = bold || tag is "strong" or "b" || (e.GetAttribute("style") ?? "").Contains("font-weight: bold");
            var i = italic || tag is "em" or "i";
            var u = underline || tag == "u";
            var col = ColorOf(e.GetAttribute("style")) ?? color;
            var block = tag is "p" or "div" or "h1" or "h2" or "h3" or "h4" or "blockquote";
            if (tag == "li")
            {
                Flush();
                foreach (var ch in e.ChildNodes) Walk(ch, b, i, u, col);
                Flush(bullet: true);
                return;
            }
            if (block) Flush();
            foreach (var ch in e.ChildNodes) Walk(ch, b, i, u, col);
            if (block) Flush();
        }

        foreach (var ch in body.ChildNodes) Walk(ch, false, false, false, null);
        Flush();
        return result;
    }

    private static List<DocRun> Trim(List<DocRun> runs)
    {
        var list = runs.ToList();
        if (list.Count > 0) list[0] = list[0] with { Text = list[0].Text.TrimStart() };
        if (list.Count > 0) list[^1] = list[^1] with { Text = list[^1].Text.TrimEnd() };
        return list;
    }

    private static string? ColorOf(string? style)
    {
        if (string.IsNullOrEmpty(style)) return null;
        var m = Regex.Match(style, @"(?:^|;)\s*color\s*:\s*(#[0-9a-fA-F]{3,8}|rgb\([^)]+\))");
        if (!m.Success) return null;
        var v = m.Groups[1].Value;
        if (v.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            var n = Regex.Matches(v, @"\d+").Select(x => int.Parse(x.Value, CultureInfo.InvariantCulture)).ToArray();
            if (n.Length < 3) return null;
            v = $"#{n[0]:X2}{n[1]:X2}{n[2]:X2}";
        }
        if (v.Length == 4) v = $"#{v[1]}{v[1]}{v[2]}{v[2]}{v[3]}{v[3]}";
        return v.Length >= 7 ? v[..7] : null;
    }

    /// <summary>Plain text of script HTML (for a box subtitle).</summary>
    public string PlainText(string? html) =>
        string.Join(" ", Html(html).Select(p => string.Concat(p.Runs.Select(r => r.Text)))).Trim();

    public static string TitleCase(string s) => Title.ToTitleCase(s);
}
