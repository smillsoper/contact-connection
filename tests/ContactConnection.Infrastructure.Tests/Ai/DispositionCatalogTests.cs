using ContactConnection.Infrastructure.Ai;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Ai;

/// <summary>AI step 2: the allowed dispositions come from the script — the AI can only suggest a real one.</summary>
public class DispositionCatalogTests
{
    private const string Flow = """
    { "nodes": {
        "cf":   { "type": "set_custom_field", "definitionFieldName": "disposition", "value": "{{flow.disposition}}" },
        "ct":   { "type": "set_custom_field", "definitionFieldName": "call_type",   "value": "{{flow.call_type}}" },
        "junk": { "type": "input", "outputVariable": "disposition",
                  "options": [ { "label": "Wrong Number", "value": "Wrong Number" }, { "label": "Test Call", "value": "Test Call" } ] },
        "type": { "type": "input", "outputVariable": "call_type", "options": [ { "label": "Order", "value": "Junk" } ] },
        "ord":  { "type": "set_variable", "assignments": [ { "variable": "{{flow.disposition}}", "value": "Order" },
                                                           { "variable": "{{flow.call_type}}",   "value": "Order" } ] },
        "dyn":  { "type": "set_variable", "assignments": [ { "variable": "{{flow.disposition}}", "value": "{{input.x}}" } ] }
    } }
    """;

    [Fact]
    public void FollowsTheScript_FromTheDispositionFieldBackToEveryValueItCanGet() =>
        Assert.Equal(["Order", "Test Call", "Wrong Number"], DispositionCatalog.FromDefinitions([Flow]));

    [Fact]
    public void NoDispositionField_NoValues() =>
        Assert.Empty(DispositionCatalog.FromDefinitions(["""{ "nodes": { "a": { "type": "input", "outputVariable": "disposition", "options": [ { "value": "X" } ] } } }"""]));

    [Fact]
    public void VariableNames_AreCaseSensitive_LikeTheEngine() =>
        // The field copies {{flow.disposition}}; a question saving into "Disposition" never reaches it.
        Assert.Equal(["Order"], DispositionCatalog.FromDefinitions(["""
        { "nodes": {
            "cf": { "type": "set_custom_field", "definitionFieldName": "disposition", "value": "{{flow.disposition}}" },
            "q":  { "type": "input", "outputVariable": "Disposition", "options": [ { "value": "Wrong Number" } ] },
            "o":  { "type": "set_variable", "assignments": [ { "variable": "{{flow.disposition}}", "value": "Order" } ] }
        } }
        """]));
}
