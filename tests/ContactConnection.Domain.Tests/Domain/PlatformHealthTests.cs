using ContactConnection.Domain.Entities;
using Xunit;

namespace ContactConnection.Domain.Tests.Domain;

/// <summary>Platform health (S184): levels, and when an alert email is due.</summary>
public class PlatformHealthTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(100, "ok")]
    [InlineData(250, "warning")]
    [InlineData(999, "warning")]
    [InlineData(1000, "critical")]
    public void Higher_is_worse(double ms, string expected) =>
        Assert.Equal(expected, HealthCatalog.Find("postgres")!.Evaluate(ms, 250, 1000));

    [Theory]
    [InlineData(40, "ok")]
    [InlineData(15, "warning")]
    [InlineData(4, "critical")]
    public void Lower_is_worse_for_free_space(double pct, string expected) =>
        Assert.Equal(expected, HealthCatalog.Find("storage")!.Evaluate(pct, 15, 5));

    [Fact]
    public void Jobs_are_known_checks() => Assert.Equal("Background jobs", HealthCatalog.Find("job:Data exports")!.Area);

    [Fact]
    public void A_problem_alerts_once_then_recovery_alerts_once()
    {
        var c = PlatformHealthCheck.New("redis", T0);
        c.Record(HealthStatus.Ok, 1, null, T0);
        Assert.Null(c.AlertDue(T0));

        c.Record(HealthStatus.Warning, 150, null, T0.AddMinutes(1));
        Assert.Equal("problem", c.AlertDue(T0.AddMinutes(1)));
        c.MarkAlerted(T0.AddMinutes(1));
        Assert.Null(c.AlertDue(T0.AddMinutes(40)));          // warnings don't repeat

        c.Record(HealthStatus.Ok, 1, null, T0.AddMinutes(41));
        Assert.Equal("recovered", c.AlertDue(T0.AddMinutes(41)));
        c.MarkAlerted(T0.AddMinutes(41));
        Assert.Null(c.AlertDue(T0.AddMinutes(42)));
    }

    [Fact]
    public void Worsening_alerts_again_and_critical_repeats_until_acknowledged()
    {
        var c = PlatformHealthCheck.New("redis", T0);
        c.Record(HealthStatus.Warning, 150, null, T0);
        c.MarkAlerted(T0);
        c.Record(HealthStatus.Critical, 900, null, T0.AddMinutes(1));
        Assert.Equal("problem", c.AlertDue(T0.AddMinutes(1)));
        c.MarkAlerted(T0.AddMinutes(1));
        Assert.Null(c.AlertDue(T0.AddMinutes(20)));
        Assert.Equal("problem", c.AlertDue(T0.AddMinutes(31)));   // 30-minute reminder
        c.Acknowledge("Stephen", T0.AddMinutes(32));
        Assert.Null(c.AlertDue(T0.AddMinutes(90)));
    }

    [Fact]
    public void Muted_checks_stay_quiet_and_unknown_never_alerts()
    {
        var c = PlatformHealthCheck.New("trunk", T0);
        c.Mute(T0.AddHours(1));
        c.Record(HealthStatus.Critical, null, "down", T0);
        Assert.Null(c.AlertDue(T0.AddMinutes(5)));
        Assert.Equal("problem", c.AlertDue(T0.AddHours(2)));     // mute over

        var u = PlatformHealthCheck.New("abandon_rate", T0);
        u.Record(HealthStatus.Unknown, null, null, T0);
        Assert.Null(u.AlertDue(T0));
    }

    [Fact]
    public void A_new_worse_problem_clears_the_old_acknowledgement()
    {
        var c = PlatformHealthCheck.New("redis", T0);
        c.Record(HealthStatus.Warning, 150, null, T0);
        c.Acknowledge("Stephen", T0);
        c.Record(HealthStatus.Critical, 900, null, T0.AddMinutes(1));
        Assert.Null(c.AcknowledgedAt);
    }

    [Fact]
    public void Extra_recipients_are_validated()
    {
        var s = HealthSettings.Default();
        s.SetRecipients([" Ops@Example.com ", "ops@example.com", ""]);
        Assert.Equal(["ops@example.com"], s.ExtraRecipients);
        Assert.Throws<ArgumentException>(() => s.SetRecipients(["not-an-email"]));
    }
}
