using ContactConnection.Infrastructure.CallTrace;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.CallTrace;

/// <summary>tf_secure_collect exposes captured digits as <c>secure.&lt;key&gt;</c> flow vars and
/// keeps working state under <c>_sc_</c>; neither may land in a stored telephony call-trace
/// snapshot.</summary>
public class CallTraceSnapshotRedactionTests
{
    [Fact]
    public void BuildTelephonySnapshot_RedactsSecureAndScVars_KeepsOthers()
    {
        var vars = new Dictionary<string, string>
        {
            ["caller_intent"]   = "sales",
            ["secure.pan"]      = "4242424242424242",
            ["secure.cvv"]      = "123",
            ["_sc_field_index"] = "1",
            ["_sc_fields_json"] = "[{\"k\":\"pan\"}]",
        };

        var json = CallTraceSnapshot.BuildTelephonySnapshot(
            vars, new Dictionary<string, string>(), "+15551110000", "+15552220000", "uuid-1");

        Assert.Contains("\"caller_intent\":\"sales\"", json);
        Assert.DoesNotContain("4242424242424242", json);
        Assert.DoesNotContain("\"123\"", json);
        Assert.DoesNotContain("\"k\\u0022:\\u0022pan", json); // the raw fields json is gone too
        Assert.Contains("[REDACTED]", json);
    }

    // S178: {{shared.*}} variables now appear in both engines' snapshots, with the same PCI redaction.
    [Fact]
    public void Snapshots_IncludeSharedVars_Redacted()
    {
        var shared = new Dictionary<string, string>
        {
            ["outside_hours"] = "true",
            ["secure.pan"]    = "4242424242424242",
        };

        var tel = CallTraceSnapshot.BuildTelephonySnapshot(
            new Dictionary<string, string>(), new Dictionary<string, string>(), "+15551110000", "+15552220000", "uuid-1", shared);
        var crm = CallTraceSnapshot.BuildCrmSnapshot(
            new ContactConnection.Infrastructure.FlowEngine.FlowExecutionContext { SharedVars = shared }, []);

        foreach (var json in new[] { tel, crm })
        {
            Assert.Contains("\"sharedVars\":{", json);
            Assert.Contains("\"outside_hours\":\"true\"", json);
            Assert.DoesNotContain("4242424242424242", json);
        }
    }
}
