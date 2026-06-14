using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Persistence.Configurations;

public class PhoneNumberConfiguration : IEntityTypeConfiguration<PhoneNumber>
{
    public void Configure(EntityTypeBuilder<PhoneNumber> builder)
    {
        builder.ToTable("phone_numbers");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(p => p.CampaignId).HasColumnName("campaign_id").IsRequired();
        builder.Property(p => p.Number).HasColumnName("number").HasMaxLength(20).IsRequired();
        builder.Property(p => p.Label).HasColumnName("label").HasMaxLength(100);
        builder.Property(p => p.IsActive).HasColumnName("is_active");
        builder.Property(p => p.CreatedAt).HasColumnName("created_at");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");

        builder.HasOne(p => p.Campaign)
               .WithMany(c => c.PhoneNumbers)
               .HasForeignKey(p => p.CampaignId)
               .OnDelete(DeleteBehavior.Restrict);

        // Number unique within tenant schema (multiple tenants can share a DID)
        builder.HasIndex(p => p.Number).IsUnique();
        builder.HasIndex(p => p.CampaignId);
    }
}
