using System.Text;
using NCalc;

namespace ContactConnection.Infrastructure.Kpis;

/// <summary>One variable a formula KPI can use (the designer's click-to-insert sidebar).</summary>
public sealed record KpiVariable(string Name, string Label, string Group);

/// <summary>
/// Formula KPIs (S181, docs/client-dashboards-plan.md) — NCalc expressions over named KPI variables, e.g.
/// <c>Disp_Saved / (Disp_Saved + Disp_Cancelled)</c> or <c>RevenueExclTax / CallsHandled</c>. Admin-defined arithmetic over
/// numbers only (no functions with side effects exist in NCalc). A formula that divides by zero or fails evaluates to null
/// ("—") — it never breaks a dashboard.
/// </summary>
public static class KpiFormula
{
    /// <summary>The fixed variables, in sidebar order. Category and disposition counts are added per tenant.</summary>
    public static readonly IReadOnlyList<KpiVariable> Fixed =
    [
        new("CallsOffered", "Calls offered", "Calls"),
        new("CallsHandled", "Calls handled", "Calls"),
        new("CallsAbandoned", "Calls abandoned", "Calls"),
        new("Interactions", "Interactions", "Calls"),
        new("Opportunities", "Sales opportunities", "Calls"),
        new("Orders", "Orders", "Orders"),
        new("NetOrders", "Net orders (payment went through)", "Orders"),
        new("Declines", "Declines", "Orders"),
        new("Units", "Units sold", "Orders"),
        new("UpsellOrders", "Orders with an upsell", "Orders"),
        new("SaleWithoutOrder", "Sale dispositions with no order", "Orders"),
        new("RevenueGross", "Revenue — gross (incl. tax)", "Revenue"),
        new("RevenueExclTax", "Revenue — excluding tax", "Revenue"),
        new("RevenueMerch", "Revenue — merchandise only", "Revenue"),
        new("NetRevenueGross", "Net revenue — gross", "Revenue"),
        new("NetRevenueExclTax", "Net revenue — excluding tax", "Revenue"),
        new("NetRevenueMerch", "Net revenue — merchandise only", "Revenue"),
        new("TalkSeconds", "Talk time (seconds)", "Time"),
        new("AcwSeconds", "After-call work (seconds)", "Time"),
        new("HandleSeconds", "Interaction handle time (seconds)", "Time"),
        new("LoggedInSeconds", "Agent logged-in time (seconds)", "Time"),
    ];

    /// <summary>"Lead captured" → "Cat_LeadCaptured"; names that collide get a numeric suffix.</summary>
    public static Dictionary<Guid, string> Names(IEnumerable<(Guid Id, string Name)> items, string prefix)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<Guid, string>();
        foreach (var (id, name) in items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Id))
        {
            var sb = new StringBuilder(prefix);
            // Any run of non-alphanumerics separates words: "customer-service" → CustomerService.
            var words = new string(name.Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ').ToArray())
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var word in words) sb.Append(char.ToUpperInvariant(word[0])).Append(word[1..]);
            var candidate = sb.ToString();
            var n = 2;
            while (!used.Add(candidate)) candidate = $"{sb}{n++}";
            result[id] = candidate;
        }
        return result;
    }

    /// <summary>The variable values for one row of metrics.</summary>
    public static Dictionary<string, double> Values(KpiMetrics m, IReadOnlyDictionary<Guid, string> categoryNames, IReadOnlyDictionary<Guid, string> dispositionNames)
    {
        var raw = m.Raw;
        var v = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["CallsOffered"] = m.CallsOffered, ["CallsHandled"] = m.CallsHandled, ["CallsAbandoned"] = m.CallsAbandoned,
            ["Interactions"] = m.Interactions, ["Opportunities"] = m.Opportunities,
            ["Orders"] = m.Orders, ["NetOrders"] = m.NetOrders, ["Declines"] = m.Declines,
            ["Units"] = raw.Units, ["UpsellOrders"] = raw.UpsellOrders, ["SaleWithoutOrder"] = m.SaleWithoutOrder,
            ["RevenueGross"] = (double)m.Gross.Total, ["RevenueExclTax"] = (double)m.ExclTax.Total, ["RevenueMerch"] = (double)m.Merch.Total,
            ["NetRevenueGross"] = (double)m.Gross.Net, ["NetRevenueExclTax"] = (double)m.ExclTax.Net, ["NetRevenueMerch"] = (double)m.Merch.Net,
            ["TalkSeconds"] = raw.TalkSeconds, ["AcwSeconds"] = raw.AcwSeconds, ["HandleSeconds"] = raw.HandleSeconds,
            ["LoggedInSeconds"] = raw.LoggedInSeconds,
        };
        foreach (var (id, name) in categoryNames) v[name] = raw.CategoryCounts.GetValueOrDefault(id);
        foreach (var (id, name) in dispositionNames) v[name] = raw.DispositionCounts.GetValueOrDefault(id);
        return v;
    }

    /// <summary>Null when the formula is usable; otherwise what's wrong, in words.</summary>
    public static string? Validate(string formula, IEnumerable<string> knownNames)
    {
        if (string.IsNullOrWhiteSpace(formula)) return "Enter a formula.";
        var known = new HashSet<string>(knownNames, StringComparer.OrdinalIgnoreCase);
        try
        {
            var expression = new Expression(formula, ExpressionOptions.IgnoreCaseAtBuiltInFunctions);
            if (expression.HasErrors()) return $"The formula isn't valid: {expression.Error?.Message}";
            var unknown = expression.GetParameterNames().Where(p => !known.Contains(p)).ToList();
            if (unknown.Count > 0) return $"Unknown variable{(unknown.Count > 1 ? "s" : "")}: {string.Join(", ", unknown)}";
            foreach (var p in expression.GetParameterNames()) expression.Parameters[p] = 1.0;
            _ = Convert.ToDouble(expression.Evaluate(), System.Globalization.CultureInfo.InvariantCulture);
            return null;
        }
        catch (Exception ex)
        {
            return $"The formula isn't valid: {ex.Message}";
        }
    }

    /// <summary>The formula's value, or null when it can't be computed (division by zero, a bad formula…).</summary>
    public static double? Evaluate(string formula, IReadOnlyDictionary<string, double> values)
    {
        try
        {
            var expression = new Expression(formula, ExpressionOptions.IgnoreCaseAtBuiltInFunctions);
            if (expression.HasErrors()) return null;
            foreach (var p in expression.GetParameterNames())
                expression.Parameters[p] = values.TryGetValue(p, out var v) ? v : 0.0;
            var result = Convert.ToDouble(expression.Evaluate(), System.Globalization.CultureInfo.InvariantCulture);
            return double.IsFinite(result) ? result : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
