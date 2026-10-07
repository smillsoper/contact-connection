using ContactConnection.Domain.Entities;
using Xunit;

namespace ContactConnection.Domain.Tests.Domain;

/// <summary>S181: a designer sandbox run chooses each integration's environment (e.g. production tax, sandbox payments).</summary>
public class IntegrationEnvironmentTests
{
    private static readonly Guid OrderApi = Guid.NewGuid();

    [Fact]
    public void Sandbox_run_uses_each_integrations_own_choice()
    {
        var r = CallRecord.CreateManual(Guid.NewGuid(), Guid.NewGuid(), CallRunMode.Sandbox, CallCredentialSet.Sandbox,
            new Dictionary<string, string> { ["tax"] = "production", ["payment"] = "sandbox", [IntegrationEnvironment.Api(OrderApi)] = "simulated" });

        Assert.Equal(CallCredentialSet.Production, r.CredentialSetFor(IntegrationEnvironment.Tax));
        Assert.Equal(CallCredentialSet.Sandbox, r.CredentialSetFor(IntegrationEnvironment.Payment));
        Assert.Equal("simulated", r.EnvironmentFor(IntegrationEnvironment.Api(OrderApi)));
        Assert.Equal("sandbox", r.EnvironmentFor(IntegrationEnvironment.Api(Guid.NewGuid())));   // unlisted → the run's set
    }

    [Fact]
    public void Training_ignores_choices_and_never_reaches_production()
    {
        var r = CallRecord.CreateManual(Guid.NewGuid(), Guid.NewGuid(), CallRunMode.Training, CallCredentialSet.Production,
            new Dictionary<string, string> { ["tax"] = "production", ["payment"] = "production" });
        Assert.Empty(r.IntegrationEnvironments);
        Assert.Equal(CallCredentialSet.Sandbox, r.CredentialSetFor(IntegrationEnvironment.Tax));
        Assert.Equal(CallCredentialSet.Sandbox, r.CredentialSetFor(IntegrationEnvironment.Payment));
    }

    [Fact]
    public void Unknown_environments_are_dropped()
    {
        var r = CallRecord.CreateManual(Guid.NewGuid(), Guid.NewGuid(), CallRunMode.Sandbox, CallCredentialSet.Sandbox,
            new Dictionary<string, string> { ["tax"] = "live!", ["payment"] = "production" });
        Assert.False(r.IntegrationEnvironments.ContainsKey("tax"));
        Assert.Equal(CallCredentialSet.Sandbox, r.CredentialSetFor(IntegrationEnvironment.Tax));
        Assert.Equal(CallCredentialSet.Production, r.CredentialSetFor(IntegrationEnvironment.Payment));
    }
}
