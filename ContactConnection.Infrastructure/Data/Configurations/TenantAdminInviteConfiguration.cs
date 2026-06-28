using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

public class TenantAdminInviteConfiguration : IEntityTypeConfiguration<TenantAdminInvite>
{
    public void Configure(EntityTypeBuilder<TenantAdminInvite> builder)
    {
        builder.ToTable("tenant_admin_invites");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id");

        builder.Property(i => i.TenantId).HasColumnName("tenant_id").IsRequired();

        builder.Property(i => i.Email)
            .HasColumnName("email")
            .IsRequired()
            .HasMaxLength(254);

        builder.Property(i => i.Role)
            .HasColumnName("role")
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(i => i.RoleId).HasColumnName("role_id");

        builder.Property(i => i.RoleName)
            .HasColumnName("role_name")
            .HasMaxLength(100);

        builder.Property(i => i.Token)
            .HasColumnName("token")
            .IsRequired()
            .HasMaxLength(64);
        builder.HasIndex(i => i.Token).IsUnique();

        builder.Property(i => i.ExpiresAt).HasColumnName("expires_at");
        builder.Property(i => i.AcceptedAt).HasColumnName("accepted_at");

        builder.HasOne(i => i.Tenant)
            .WithMany()
            .HasForeignKey(i => i.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
