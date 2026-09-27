using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

public class OrderNumberSequenceConfiguration : IEntityTypeConfiguration<OrderNumberSequence>
{
    public void Configure(EntityTypeBuilder<OrderNumberSequence> builder)
    {
        builder.ToTable("order_number_sequences");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id).HasColumnName("id");
        builder.Property(s => s.TenantId).HasColumnName("tenant_id");
        builder.Property(s => s.ClientId).HasColumnName("client_id");
        builder.Property(s => s.Prefix).HasColumnName("prefix").HasMaxLength(OrderNumberSequence.MaxFormattedLength).IsRequired();
        builder.Property(s => s.Suffix).HasColumnName("suffix").HasMaxLength(OrderNumberSequence.MaxFormattedLength).IsRequired();
        builder.Property(s => s.Width).HasColumnName("width");
        builder.Property(s => s.NextValue).HasColumnName("next_value");
        builder.Property(s => s.CreatedAt).HasColumnName("created_at");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");

        // One sequence per client. Allocation targets this row by client_id with an atomic
        // UPDATE ... RETURNING (see OrderNumberSequenceRepository), so the unique index is also
        // what makes that UPDATE unambiguous.
        builder.HasIndex(s => s.ClientId).IsUnique().HasDatabaseName("ux_order_number_sequences_client_id");
    }
}
