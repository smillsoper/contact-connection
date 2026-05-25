using System.Text.Json;
using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id");

        builder.Property(t => t.Name)
            .HasColumnName("name")
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(t => t.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(200);

        builder.Property(t => t.LogoUrl)
            .HasColumnName("logo_url")
            .HasMaxLength(2048);

        builder.Property(t => t.Subdomain)
            .HasColumnName("subdomain")
            .IsRequired()
            .HasMaxLength(63);  // DNS label max length
        builder.HasIndex(t => t.Subdomain).IsUnique();

        builder.Property(t => t.CustomDomain)
            .HasColumnName("custom_domain")
            .HasMaxLength(253); // DNS name max length
        builder.HasIndex(t => t.CustomDomain).IsUnique().HasFilter("custom_domain IS NOT NULL");

        builder.Property(t => t.SchemaName)
            .HasColumnName("schema_name")
            .IsRequired()
            .HasMaxLength(63);
        builder.HasIndex(t => t.SchemaName).IsUnique();

        builder.Property(t => t.PlanTier)
            .HasColumnName("plan_tier")
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(t => t.Timezone)
            .HasColumnName("timezone")
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(t => t.IsActive).HasColumnName("is_active");

        builder.Property(t => t.TrialExpiresAt).HasColumnName("trial_expires_at");

        builder.Property(t => t.BillingContact)
            .HasColumnName("billing_contact")
            .HasMaxLength(200);

        builder.Property(t => t.InviteEmail)
            .HasColumnName("invite_email")
            .HasMaxLength(254);

        builder.Property(t => t.FeatureFlags)
            .HasColumnName("feature_flags")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<TenantFeatureFlags>(v, JsonOptions)
                     ?? TenantFeatureFlags.Default()
            );

        builder.Property(t => t.Settings)
            .HasColumnName("settings")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<TenantSettings>(v, JsonOptions)
                     ?? TenantSettings.Default()
            );

        builder.Property(t => t.OnboardingComplete).HasColumnName("onboarding_complete");

        builder.Property(t => t.CreatedAt).HasColumnName("created_at");
    }
}
