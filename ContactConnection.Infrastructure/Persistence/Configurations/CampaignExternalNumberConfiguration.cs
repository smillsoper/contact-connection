using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Persistence.Configurations;

public class CampaignExternalNumberConfiguration : IEntityTypeConfiguration<CampaignExternalNumber>
{
    public void Configure(EntityTypeBuilder<CampaignExternalNumber> builder)
    {
        builder.ToTable("campaign_external_numbers");
        builder.HasKey(n => n.Id);

        builder.Property(n => n.Id).HasColumnName("id");
        builder.Property(n => n.CampaignId).HasColumnName("campaign_id").IsRequired();
        builder.Property(n => n.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(n => n.Label).HasColumnName("label").HasMaxLength(100).IsRequired();
        builder.Property(n => n.Number).HasColumnName("number").HasMaxLength(30).IsRequired();
        builder.Property(n => n.DisplayOrder).HasColumnName("display_order");
        builder.Property(n => n.IsActive).HasColumnName("is_active");
        builder.Property(n => n.CreatedAt).HasColumnName("created_at");

        builder.HasOne(n => n.Campaign)
               .WithMany(c => c.ExternalNumbers)
               .HasForeignKey(n => n.CampaignId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(n => n.CampaignId);
        builder.HasIndex(n => n.TenantId);
    }
}
