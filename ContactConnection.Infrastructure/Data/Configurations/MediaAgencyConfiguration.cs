using System.Text.Json;
using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

public class MediaAgencyConfiguration : IEntityTypeConfiguration<MediaAgency>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<MediaAgency> builder)
    {
        builder.ToTable("media_agencies");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");
        builder.Property(a => a.TenantId).HasColumnName("tenant_id");
        builder.Property(a => a.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(a => a.IsActive).HasColumnName("is_active");
        builder.Property(a => a.Fields)
            .HasColumnName("fields")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<List<MediaAgencyField>>(v, JsonOptions) ?? new())
            .HasDefaultValueSql("'[]'::jsonb")
            .Metadata.SetValueComparer(new ValueComparer<List<MediaAgencyField>>(
                (x, y) => (x ?? new()).SequenceEqual(y ?? new()),
                v => v == null ? 0 : v.Aggregate(0, (h, f) => HashCode.Combine(h, f.GetHashCode())),
                v => v == null ? new() : v.ToList()));
        builder.Property(a => a.CreatedAt).HasColumnName("created_at");
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(a => new { a.TenantId, a.Name }).IsUnique().HasDatabaseName("ix_media_agencies_name");
    }
}
