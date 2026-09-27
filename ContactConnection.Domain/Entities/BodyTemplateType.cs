namespace ContactConnection.Domain.Entities;

/// <summary>How an API endpoint's RequestBodyTemplate is rendered.</summary>
public static class BodyTemplateType
{
    /// <summary>{{namespace.field}} substitution via the variable resolver (the original behavior).</summary>
    public const string Simple = "simple";
    /// <summary>Liquid template — loops, conditionals, filters, math.</summary>
    public const string Liquid = "liquid";

    public static bool IsValid(string type) => type is Simple or Liquid;
}
