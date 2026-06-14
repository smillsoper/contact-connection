using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Persistence.Configurations;

public class PhoneNumberRoutingConfiguration : IEntityTypeConfiguration<PhoneNumberRouting>
{
    public void Configure(EntityTypeBuilder<PhoneNumberRouting> builder)
    {
        builder.ToTable("phone_number_routing", "public");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Number).IsRequired().HasMaxLength(20);
        builder.Property(p => p.TenantId).IsRequired();
        builder.Property(p => p.CampaignId).IsRequired();
        builder.Property(p => p.IsActive).IsRequired();
        builder.Property(p => p.CreatedAt).IsRequired();
        builder.Property(p => p.UpdatedAt).IsRequired();

        // Number is globally unique — one DID, one tenant
        builder.HasIndex(p => p.Number).IsUnique();

        // ESL service queries active routes by tenant frequently
        builder.HasIndex(p => new { p.TenantId, p.IsActive });
    }
}
