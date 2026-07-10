using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Persistence.Configurations;

public class CustomUnavailableCodeConfiguration : IEntityTypeConfiguration<CustomUnavailableCode>
{
    public void Configure(EntityTypeBuilder<CustomUnavailableCode> b)
    {
        b.ToTable("agent_unavailable_codes");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        b.Property(x => x.Roles).HasColumnName("roles").HasColumnType("text[]").IsRequired();
        b.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        b.Property(x => x.CreatedAt).HasColumnName("created_at");

        b.HasIndex(x => x.TenantId);
        b.HasIndex(x => new { x.TenantId, x.IsActive });
    }
}
