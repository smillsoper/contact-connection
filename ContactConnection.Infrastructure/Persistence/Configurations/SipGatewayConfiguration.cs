using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Persistence.Configurations;

public class SipGatewayConfiguration : IEntityTypeConfiguration<SipGateway>
{
    public void Configure(EntityTypeBuilder<SipGateway> builder)
    {
        builder.ToTable("sip_gateways", "public");
        builder.HasKey(g => g.Id);

        builder.Property(g => g.Id).HasColumnName("id");
        builder.Property(g => g.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(g => g.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(g => g.Proxy).HasColumnName("proxy").HasMaxLength(255).IsRequired();
        builder.Property(g => g.FromDomain).HasColumnName("from_domain").HasMaxLength(255);
        builder.Property(g => g.Username).HasColumnName("username").HasMaxLength(100).IsRequired();
        builder.Property(g => g.Password).HasColumnName("password").HasMaxLength(255).IsRequired();
        builder.Property(g => g.Register).HasColumnName("register");
        builder.Property(g => g.Transport).HasColumnName("transport").HasMaxLength(10).IsRequired();
        builder.Property(g => g.CodecPrefs).HasColumnName("codec_prefs").HasMaxLength(255);
        builder.Property(g => g.IsActive).HasColumnName("is_active");
        builder.Property(g => g.CreatedAt).HasColumnName("created_at");
        builder.Property(g => g.UpdatedAt).HasColumnName("updated_at");

        // Gateway name must be unique platform-wide — used as the ESL lookup key
        builder.HasIndex(g => g.Name).IsUnique();
        builder.HasIndex(g => g.TenantId);
    }
}
