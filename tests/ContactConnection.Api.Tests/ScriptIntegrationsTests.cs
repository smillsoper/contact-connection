using ContactConnection.Api.Endpoints;
using Xunit;

namespace ContactConnection.Api.Tests;

public class ScriptIntegrationsTests
{
    [Fact]
    public void Walk_follows_sub_flows_and_skips_platform_lookups()
    {
        Guid main = Guid.NewGuid(), sub = Guid.NewGuid(), orderEp = Guid.NewGuid(), zipEp = Guid.NewGuid(), subEp = Guid.NewGuid();
        var defs = new Dictionary<Guid, string>
        {
            [main] = "{\"nodes\":{\"a\":{\"type\":\"api_call\",\"apiEndpointId\":\"" + orderEp + "\"},"
                     + "\"z\":{\"type\":\"api_call\",\"apiDefinitionScope\":\"portal\",\"apiEndpointId\":\"" + zipEp + "\"},"
                     + "\"p\":{\"type\":\"authorize_payment\"},\"x\":{\"type\":\"execute_flow\",\"targetFlowId\":\"" + sub + "\"}}}",
            [sub] = "{\"nodes\":{\"b\":{\"type\":\"api_call\",\"apiDefinitionScope\":\"tenant\",\"apiEndpointId\":\"" + subEp + "\"},"
                    + "\"back\":{\"type\":\"transition_to_flow\",\"targetFlowId\":\"" + main + "\"}}}",
        };
        var (types, endpoints) = ScriptIntegrationsEndpoints.Walk(defs, main);
        Assert.Contains("authorize_payment", types);
        Assert.Equal(new HashSet<Guid> { orderEp, subEp }, endpoints);   // ZIP lookup (portal) excluded; cycle handled
    }
}
