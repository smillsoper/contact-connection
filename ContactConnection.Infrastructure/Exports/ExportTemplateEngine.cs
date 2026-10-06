using System.Collections.Concurrent;
using System.Globalization;
using System.Security;
using System.Text;
using Fluid;
using Fluid.Values;

namespace ContactConnection.Infrastructure.Exports;

/// <summary>
/// Liquid for export files (S180) — Fluid, like the API request bodies, plus the filters files need:
///
///   format_time: "MM/dd/yyyy"        a timestamp in the export's time zone (.NET format; optional 2nd arg = another IANA zone)
///   pad_left: 6, "0" / pad_right: 12 exactly that wide — padded, then cut (fixed-width records)
///   digits                           only the digits ("(303) 555-0100" → "3035550100")
///   number: "000000"                 a number in a .NET format ("0.00", "000000")
///   money                            two decimals, no symbol
///   csv                              always quoted, inner quotes doubled
///   xml                              XML-escaped
///
/// Models are plain dictionaries/lists (see <see cref="ApiExecution.FluidLiquidTemplateRenderer.ToPlain"/>), so a template
/// can never reach into a .NET object. Per-row templates get the API's step limit; a Document template walks every call,
/// so its limit is much higher.
/// </summary>
public sealed class ExportTemplateEngine
{
    private const int RowMaxSteps = 200_000;
    private const int DocumentMaxSteps = 50_000_000;

    private static readonly FluidParser Parser = new();
    private static readonly ConcurrentDictionary<string, IFluidTemplate> Cache = new();
    private static readonly TemplateOptions RowOptions = CreateOptions(RowMaxSteps);
    private static readonly TemplateOptions DocumentOptions = CreateOptions(DocumentMaxSteps);

    private readonly TimeZoneInfo _timeZone;

    public ExportTemplateEngine(TimeZoneInfo timeZone) => _timeZone = timeZone;

    public static string? Validate(string template) =>
        Parser.TryParse(template, out _, out var error) ? null : error;

    /// <summary>The inside of an <c>{% if %}</c>, e.g. <c>call.disposition != "Junk"</c>.</summary>
    public static string ConditionTemplate(string condition) => "{% if " + condition + " %}1{% endif %}";

    public async ValueTask<string> RenderAsync(string template, IReadOnlyDictionary<string, object?> model, bool document = false)
    {
        var parsed = Get(template);
        var context = new TemplateContext(document ? DocumentOptions : RowOptions) { TimeZone = _timeZone };
        foreach (var (key, value) in model) context.SetValue(key, value);
        return await parsed.RenderAsync(context, NullEncoder.Default);
    }

    public async ValueTask<bool> TestAsync(string condition, IReadOnlyDictionary<string, object?> model) =>
        (await RenderAsync(ConditionTemplate(condition), model)).Trim() == "1";

    private static IFluidTemplate Get(string source)
    {
        if (Cache.TryGetValue(source, out var t)) return t;
        if (!Parser.TryParse(source, out t, out var error)) throw new ExportTemplateException($"Liquid template error: {error}");
        if (Cache.Count >= 500) Cache.Clear();
        Cache[source] = t;
        return t;
    }

    private static TemplateOptions CreateOptions(int maxSteps)
    {
        var o = new TemplateOptions
        {
            MaxSteps = maxSteps, MaxRecursion = 50, CultureInfo = CultureInfo.InvariantCulture, TimeZone = TimeZoneInfo.Utc,
        };

        o.Filters.AddFilter("money", (input, _, _) => new StringValue(
            Math.Round(input.ToNumberValue(), 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture)));

        o.Filters.AddFilter("number", (input, args, _) =>
        {
            var format = args.At(0).IsNil() ? "0.##" : args.At(0).ToStringValue();
            return new StringValue(input.ToNumberValue().ToString(format, CultureInfo.InvariantCulture));
        });

        o.Filters.AddFilter("format_time", (input, args, ctx) =>
        {
            var when = ToTime(input);
            if (when is null) return StringValue.Empty;
            var zone = ctx.TimeZone ?? TimeZoneInfo.Utc;
            if (!args.At(1).IsNil()) zone = TimeZoneInfo.FindSystemTimeZoneById(args.At(1).ToStringValue());
            var local = TimeZoneInfo.ConvertTime(when.Value, zone);
            var format = args.At(0).IsNil() ? "yyyy-MM-dd HH:mm:ss" : args.At(0).ToStringValue();
            return new StringValue(local.ToString(format, CultureInfo.InvariantCulture));
        });

        o.Filters.AddFilter("pad_left", (input, args, _) => new StringValue(
            Fit(input.ToStringValue(), (int)args.At(0).ToNumberValue(), PadChar(args), right: true)));
        o.Filters.AddFilter("pad_right", (input, args, _) => new StringValue(
            Fit(input.ToStringValue(), (int)args.At(0).ToNumberValue(), PadChar(args), right: false)));

        o.Filters.AddFilter("digits", (input, _, _) =>
            new StringValue(new string(input.ToStringValue().Where(char.IsAsciiDigit).ToArray())));

        o.Filters.AddFilter("csv", (input, _, _) =>
            new StringValue("\"" + input.ToStringValue().Replace("\"", "\"\"") + "\""));

        o.Filters.AddFilter("xml", (input, _, _) =>
            new StringValue(SecurityElement.Escape(input.ToStringValue()) ?? ""));

        return o;
    }

    private static char PadChar(FilterArguments args)
    {
        var s = args.At(1).IsNil() ? " " : args.At(1).ToStringValue();
        return s.Length > 0 ? s[0] : ' ';
    }

    private static DateTimeOffset? ToTime(FluidValue input)
    {
        switch (input.ToObjectValue())
        {
            case DateTimeOffset d: return d;
            case DateTime dt: return new DateTimeOffset(DateTime.SpecifyKind(dt, dt.Kind == DateTimeKind.Unspecified ? DateTimeKind.Utc : dt.Kind));
        }
        var s = input.ToStringValue();
        if (string.IsNullOrWhiteSpace(s)) return null;
        return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var p) ? p : null;
    }

    /// <summary>Exactly <paramref name="width"/> characters: padded (on the left when <paramref name="right"/>-aligned),
    /// then cut keeping the leftmost characters.</summary>
    public static string Fit(string value, int width, char pad, bool right)
    {
        if (width <= 0) return "";
        if (value.Length >= width) return value[..width];
        return right ? value.PadLeft(width, pad) : value.PadRight(width, pad);
    }

    /// <summary>Normalizes a rendered document to the chosen line ending, optionally dropping whitespace-only lines.</summary>
    public static string NormalizeLines(string text, string lineEnding, bool skipBlankLines)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var sb = new StringBuilder(text.Length);
        var nl = lineEnding == "lf" ? "\n" : "\r\n";
        var kept = skipBlankLines ? lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList() : lines.ToList();
        // A trailing newline in the template shouldn't produce an empty last line.
        if (!skipBlankLines && kept.Count > 0 && kept[^1].Length == 0) kept.RemoveAt(kept.Count - 1);
        foreach (var l in kept) sb.Append(l).Append(nl);
        return sb.ToString();
    }
}

public sealed class ExportTemplateException(string message) : Exception(message);
