using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

public class TenantInviteConfiguration : IEntityTypeConfiguration<TenantInvite>
{
    public void Configure(EntityTypeBuilder<TenantInvite> builder)
    {
        builder.ToTable("tenant_invites");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id");

        builder.Property(i => i.TenantId).HasColumnName("tenant_id").IsRequired();

        builder.Property(i => i.Token)
            .HasColumnName("token")
            .IsRequired()
            .HasMaxLength(64);
        builder.HasIndex(i => i.Token).IsUnique();

        builder.Property(i => i.Email)
            .HasColumnName("email")
            .IsRequired()
            .HasMaxLength(254);

        builder.Property(i => i.ExpiresAt).HasColumnName("expires_at");
        builder.Property(i => i.AcceptedAt).HasColumnName("accepted_at");

        builder.HasOne(i => i.Tenant)
            .WithMany()
            .HasForeignKey(i => i.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
