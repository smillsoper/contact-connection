namespace ContactConnection.Infrastructure.FlowEngine;

/// <summary>
/// Branch conditions joined with && and || (S179) — shared by the CRM branch node and the telephony tf_branch.
///
/// The condition is split on the operators the script author TYPED, before any {{...}} tag is resolved, so a resolved
/// value that happens to contain "&&" or "||" (a customer note, a name) can never split the condition. Operators inside
/// a {{...}} tag or a "quoted" literal are ignored. && binds tighter than || (a || b && c = a || (b && c)); there are no
/// parentheses. Each clause is a simple comparison evaluated by the caller's existing single-clause evaluator.
/// </summary>
public static class CompoundCondition
{
    public static bool Evaluate(string condition, Func<string, bool> evaluateClause)
    {
        var orGroups = Split(condition, "||");
        if (orGroups.Count == 1)
        {
            var andClauses = Split(condition, "&&");
            if (andClauses.Count == 1) return evaluateClause(condition);   // plain condition — unchanged behaviour
        }

        return orGroups.Any(group => Split(group, "&&").All(clause =>
            !string.IsNullOrWhiteSpace(clause) && evaluateClause(clause.Trim())));
    }

    /// <summary>Splits on <paramref name="op"/> where it appears outside {{...}} tags and "quoted" literals.</summary>
    internal static List<string> Split(string text, string op)
    {
        var parts = new List<string>();
        int start = 0, tagDepth = 0;
        var inQuotes = false;
        for (var i = 0; i < text.Length; i++)
        {
            if (!inQuotes && i + 1 < text.Length && text[i] == '{' && text[i + 1] == '{') { tagDepth++; i++; continue; }
            if (!inQuotes && tagDepth > 0 && i + 1 < text.Length && text[i] == '}' && text[i + 1] == '}') { tagDepth--; i++; continue; }
            if (tagDepth > 0) continue;
            if (text[i] == '"') { inQuotes = !inQuotes; continue; }
            if (!inQuotes && string.CompareOrdinal(text, i, op, 0, op.Length) == 0)
            {
                parts.Add(text[start..i]);
                start = i + op.Length;
                i += op.Length - 1;
            }
        }
        parts.Add(text[start..]);
        return parts;
    }
}
