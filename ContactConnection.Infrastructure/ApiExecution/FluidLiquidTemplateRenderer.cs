using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using Fluid;
using Fluid.Values;

namespace ContactConnection.Infrastructure.ApiExecution;

/// <summary>
/// ILiquidTemplateRenderer backed by Fluid. Parsed templates are cached by their source text, so a
/// template is parsed once no matter how many calls use it (the CRMPro "compile on save" idea,
/// minus the compiler). The model is converted to plain dictionaries/lists/values before
/// rendering — Fluid can read those without any member-access registration, so a template can
/// never reach into a .NET object.
/// </summary>
public class FluidLiquidTemplateRenderer : ILiquidTemplateRenderer
{
    /// <summary>Upper bound on template execution steps — generous for any real payload (a loop
    /// over a few hundred cart lines), fatal to an accidental or malicious runaway loop.</summary>
    internal const int MaxSteps = 200_000;
    private const int MaxCachedTemplates = 500;

    private static readonly FluidParser Parser = new();
    private static readonly ConcurrentDictionary<string, IFluidTemplate> Cache = new();

    private static readonly TemplateOptions Options = CreateOptions();

    private static TemplateOptions CreateOptions()
    {
        var options = new TemplateOptions
        {
            MaxSteps = MaxSteps,
            MaxRecursion = 50,
            CultureInfo = CultureInfo.InvariantCulture,
            TimeZone = TimeZoneInfo.Utc,
        };
        // {{ amount | money }} → "49.95" — always two decimals, invariant culture, no symbol.
        options.Filters.AddFilter("money", (input, _, _) =>
        {
            var value = input.ToNumberValue();
            return new StringValue(Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture));
        });
        return options;
    }

    public string? Validate(string template)
        => Parser.TryParse(template, out _, out var error) ? null : error;

    public async Task<LiquidRenderResult> RenderAsync(string template, JsonObject model, CancellationToken ct = default)
    {
        if (!TryGetTemplate(template, out var parsed, out var parseError))
            return new LiquidRenderResult(false, null, $"Liquid template error: {parseError}");

        var context = new TemplateContext(Options);
        foreach (var (key, value) in model)
            context.SetValue(key, ToPlain(value));

        try
        {
            ct.ThrowIfCancellationRequested();
            // NullEncoder: bodies are JSON/XML/text — HTML-encoding would turn quotes into &quot;.
            var output = await parsed!.RenderAsync(context, NullEncoder.Default);
            return new LiquidRenderResult(true, output, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new LiquidRenderResult(false, null, $"Liquid render error: {ex.Message}");
        }
    }

    private static bool TryGetTemplate(string source, out IFluidTemplate? template, out string? error)
    {
        if (Cache.TryGetValue(source, out template)) { error = null; return true; }
        if (!Parser.TryParse(source, out template, out error)) return false;
        if (Cache.Count >= MaxCachedTemplates) Cache.Clear(); // crude bound; templates are few and re-parse is cheap
        Cache[source] = template;
        return true;
    }

    /// <summary>JsonNode → plain CLR values Fluid understands natively (dictionary, list, decimal,
    /// bool, string). Numbers become decimal so money math is exact.</summary>
    internal static object? ToPlain(JsonNode? node) => node switch
    {
        null => null,
        JsonObject o => o.ToDictionary(kv => kv.Key, kv => ToPlain(kv.Value)),
        JsonArray a => a.Select(ToPlain).ToList(),
        JsonValue v => v.GetValueKind() switch
        {
            JsonValueKind.String => v.GetValue<string>(),
            // Via the JSON text: an in-memory JsonValue<int> won't TryGetValue<decimal>, and a
            // parsed one might not fit a decimal — the text form handles both.
            JsonValueKind.Number => decimal.TryParse(v.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                ? d
                : double.Parse(v.ToJsonString(), CultureInfo.InvariantCulture),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        },
        _ => null,
    };
}
