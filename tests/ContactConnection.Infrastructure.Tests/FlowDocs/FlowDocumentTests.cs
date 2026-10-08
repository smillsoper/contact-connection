using System.Text.Json.Nodes;
using ContactConnection.Infrastructure.FlowDocs;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.FlowDocs;

/// <summary>Flow document (S183): plain-English text, reading order / grouping, and a PDF that actually renders.</summary>
public class FlowDocumentTests
{
    private static readonly FlowDocLookups NoNames = new(new Dictionary<Guid, string>(), new Dictionary<Guid, string>(),
        new Dictionary<Guid, string>(), new Dictionary<Guid, string>());

    [Fact]
    public void Variables_ReadAsTheLabelOfTheStepThatCapturesThem()
    {
        var text = new FlowDocText(new Dictionary<string, string> { ["billing_phone"] = "Billing Phone" }, new Dictionary<string, string>());
        Assert.Equal("Call [Billing Phone] now", text.Text("Call {{flow.billing_phone.value}} now"));
        Assert.Equal("[agent's first name]", text.Text("{{agent.first_name}}"));
        Assert.Equal("[Attempts] + 1", text.Text("{{flow.attempts + 1}}"));
    }

    [Fact]
    public void Conditions_ReadAsSentences()
    {
        var text = new FlowDocText(new Dictionary<string, string>(), new Dictionary<string, string>());
        Assert.Equal("[CC Capture Success] is true or [call practice run] is true",
            text.Condition("{{shared.CC_Capture_Success}} == true || {{call_record.practice_run}} == true"));
        Assert.Equal("[Attempts] is at least 3", text.Condition("{{flow.attempts}} >= 3"));
    }

    [Fact]
    public void ScriptHtml_KeepsFormatting_DropsEmptySpacers()
    {
        var text = new FlowDocText(new Dictionary<string, string>(), new Dictionary<string, string>());
        var paras = text.Html("<p><strong>Hello</strong> <span style=\"color: #0000ff\">pause</span></p><p></p><ul><li>One</li></ul>");
        Assert.Equal(2, paras.Count);
        Assert.Contains(paras[0].Runs, r => r.Text == "Hello" && r.Bold);
        Assert.Contains(paras[0].Runs, r => r.Text == "pause" && r.Color == "#0000ff");
        Assert.True(paras[1].Bullet);
    }

    private static JsonObject Crm() => JsonNode.Parse("""
        {
          "entry_node": "init",
          "nodes": {
            "init":  { "type": "set_variable", "label": "Initialize", "assignments": [{ "variable": "{{flow.x}}", "value": "1" }], "transitions": { "default": "sec1" } },
            "sec1":  { "type": "section", "label": "Opening", "transitions": { "default": "ask" } },
            "ask":   { "type": "input", "label": "Interested?", "fieldType": "select", "scriptContent": "<p>Would you like it?</p>",
                       "options": [{ "label": "Yes", "value": "y" }, { "label": "No", "value": "n" }],
                       "transitions": { "n": "bye", "y": "sec2" } },
            "sec2":  { "type": "section", "label": "Order", "transitions": { "default": "api" } },
            "api":   { "type": "api_call", "label": "Submit order", "apiDefinitionName": "Vendor API", "apiEndpointName": "Add Order",
                       "url": "https://secret.example.com/orders?key=abc123", "transitions": { "success": "bye", "error": "ask" } },
            "bye":   { "type": "end", "label": "End", "transitions": {} },
            "lost":  { "type": "script", "label": "Old greeting", "content": "<p>Hi</p>", "transitions": { "default": "bye" } }
          }
        }
        """)!.AsObject();

    [Fact]
    public void Steps_AreNumberedInReadingOrder_GroupedBySection_UnreachableKept()
    {
        var ch = FlowDocExtractor.Build(Crm(), Guid.NewGuid(), "Script", telephony: false, version: 3, draft: false, NoNames);

        Assert.Equal(["Start", "Opening", "Order", "Not connected"], ch.Groups.Select(g => g.Title));
        Assert.Equal(1, ch.StepsById["init"].Number);
        Assert.Equal(2, ch.StepsById["sec1"].Number);
        Assert.Equal(3, ch.StepsById["ask"].Number);
        Assert.True(ch.StepsById["lost"].Unreachable);
        Assert.Contains(ch.StepsById["ask"].Exits, e => e.Label == "Yes" && e.TargetId == "sec2");
    }

    [Fact]
    public void ApiSteps_NameTheSystem_NeverTheAddress()
    {
        var ch = FlowDocExtractor.Build(Crm(), Guid.NewGuid(), "Script", false, 3, false, NoNames);
        var api = ch.StepsById["api"];
        Assert.Contains(api.Facts, f => f.Value == "Vendor API — Add Order");
        Assert.DoesNotContain(api.Facts, f => f.Value.Contains("secret.example.com") || f.Value.Contains("abc123"));
    }

    [Fact]
    public void Pdf_Renders_ForAScriptAndADraft()
    {
        var doc = new FlowDocument
        {
            TenantName = "Test", Title = "Script", Draft = true, GeneratedBy = "Tester", GeneratedAt = DateTimeOffset.UtcNow, TimeZone = "UTC",
        };
        var ch = FlowDocExtractor.Build(Crm(), Guid.NewGuid(), "Script", false, 3, true, NoNames);
        ch.Number = 1;
        doc.Chapters.Add(ch);

        var pdf = FlowDocumentPdf.Render(doc);

        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }
}
