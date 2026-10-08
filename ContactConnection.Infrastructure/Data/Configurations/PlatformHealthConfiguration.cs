using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

// Platform health (S184) — public schema.

public class PlatformHealthCheckConfiguration : IEntityTypeConfiguration<PlatformHealthCheck>
{
    public void Configure(EntityTypeBuilder<PlatformHealthCheck> b)
    {
        b.ToTable("health_checks");
        b.HasKey(c => c.Key);
        b.Property(c => c.Key).HasColumnName("key").HasMaxLength(100);
        b.Property(c => c.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(c => c.Value).HasColumnName("value");
        b.Property(c => c.Detail).HasColumnName("detail").HasMaxLength(500);
        b.Property(c => c.CheckedAt).HasColumnName("checked_at");
        b.Property(c => c.StatusSince).HasColumnName("status_since");
        b.Property(c => c.WarnOverride).HasColumnName("warn_override");
        b.Property(c => c.CritOverride).HasColumnName("crit_override");
        b.Property(c => c.MutedUntil).HasColumnName("muted_until");
        b.Property(c => c.AcknowledgedAt).HasColumnName("acknowledged_at");
        b.Property(c => c.AcknowledgedBy).HasColumnName("acknowledged_by").HasMaxLength(200);
        b.Property(c => c.LastAlertedAt).HasColumnName("last_alerted_at");
        b.Property(c => c.LastAlertedStatus).HasColumnName("last_alerted_status").HasMaxLength(20);
    }
}

public class HealthSampleConfiguration : IEntityTypeConfiguration<HealthSample>
{
    public void Configure(EntityTypeBuilder<HealthSample> b)
    {
        b.ToTable("health_samples");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        b.Property(s => s.Key).HasColumnName("key").HasMaxLength(100).IsRequired();
        b.Property(s => s.At).HasColumnName("at");
        b.Property(s => s.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(s => s.Value).HasColumnName("value");
        b.HasIndex(s => new { s.Key, s.At }).HasDatabaseName("ix_health_samples_key_at");
        b.HasIndex(s => s.At).HasDatabaseName("ix_health_samples_at");
    }
}

public class HealthIncidentConfiguration : IEntityTypeConfiguration<HealthIncident>
{
    public void Configure(EntityTypeBuilder<HealthIncident> b)
    {
        b.ToTable("health_incidents");
        b.HasKey(i => i.Id);
        b.Property(i => i.Id).HasColumnName("id");
        b.Property(i => i.Key).HasColumnName("key").HasMaxLength(100).IsRequired();
        b.Property(i => i.Severity).HasColumnName("severity").HasMaxLength(20).IsRequired();
        b.Property(i => i.Detail).HasColumnName("detail").HasMaxLength(500);
        b.Property(i => i.StartedAt).HasColumnName("started_at");
        b.Property(i => i.ResolvedAt).HasColumnName("resolved_at");
        b.HasIndex(i => i.StartedAt).HasDatabaseName("ix_health_incidents_started");
    }
}

public class HealthSettingsConfiguration : IEntityTypeConfiguration<HealthSettings>
{
    public void Configure(EntityTypeBuilder<HealthSettings> b)
    {
        b.ToTable("health_settings");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(s => s.ExtraRecipients).HasColumnName("extra_recipients").HasColumnType("text[]");
    }
}

public class PlatformUserConfiguration : IEntityTypeConfiguration<PlatformUser>
{
    public void Configure(EntityTypeBuilder<PlatformUser> b)
    {
        b.ToTable("platform_users");
        b.HasKey(u => u.EntraOid);
        b.Property(u => u.EntraOid).HasColumnName("entra_oid").HasMaxLength(64);
        b.Property(u => u.Email).HasColumnName("email").HasMaxLength(320).IsRequired();
        b.Property(u => u.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        b.Property(u => u.Role).HasColumnName("role").HasMaxLength(20).IsRequired();
        b.Property(u => u.LastSignInAt).HasColumnName("last_sign_in_at");
    }
}
