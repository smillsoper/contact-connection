using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

public class AgentConfiguration : IEntityTypeConfiguration<Agent>
{
    public void Configure(EntityTypeBuilder<Agent> builder)
    {
        builder.ToTable("agents");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id).HasColumnName("id");
        builder.Property(a => a.TenantId).HasColumnName("tenant_id");

        builder.Property(a => a.FirstName)
            .HasColumnName("first_name").IsRequired().HasMaxLength(100);
        builder.Property(a => a.LastName)
            .HasColumnName("last_name").IsRequired().HasMaxLength(100);

        builder.Property(a => a.Email)
            .HasColumnName("email").IsRequired().HasMaxLength(254);
        builder.HasIndex(a => a.Email)
            .IsUnique()
            .HasDatabaseName("idx_agents_email");

        // PasswordHash is never returned via API — kept internal to the entity
        builder.Property(a => a.PasswordHash)
            .HasColumnName("password_hash").IsRequired().HasMaxLength(100);

        builder.Property(a => a.Role)
            .HasColumnName("role").IsRequired().HasMaxLength(20);
        builder.Property(a => a.IsActive).HasColumnName("is_active");
        builder.Property(a => a.CreatedAt).HasColumnName("created_at");
        builder.Property(a => a.LastLoginAt).HasColumnName("last_login_at");
        builder.Property(a => a.MfaSecret).HasColumnName("mfa_secret").HasMaxLength(64);
        builder.Property(a => a.MfaEnabled).HasColumnName("mfa_enabled");
    }
}
