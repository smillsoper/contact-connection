using System.Text.Json;
using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

public class MediaAssignmentConfiguration : IEntityTypeConfiguration<MediaAssignment>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<MediaAssignment> builder)
    {
        builder.ToTable("media_assignments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");
        builder.Property(a => a.TenantId).HasColumnName("tenant_id");
        builder.Property(a => a.PhoneNumberId).HasColumnName("phone_number_id");
        builder.Property(a => a.MarketType).HasColumnName("market_type").HasMaxLength(20).IsRequired();
        builder.Property(a => a.MediaAgencyId).HasColumnName("media_agency_id");
        builder.Property(a => a.Station).HasColumnName("station").HasMaxLength(200).IsRequired();
        builder.Property(a => a.MediaType).HasColumnName("media_type").HasMaxLength(50);
        builder.Property(a => a.AdType).HasColumnName("ad_type").HasMaxLength(50);
        builder.Property(a => a.StartDate).HasColumnName("start_date");
        builder.Property(a => a.EndDate).HasColumnName("end_date");
        builder.Property(a => a.IsDefaultLocal).HasColumnName("is_default_local");
        builder.Property(a => a.FieldValues)
            .HasColumnName("field_values")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, JsonOptions) ?? new())
            .HasDefaultValueSql("'{}'::jsonb")
            .Metadata.SetValueComparer(new ValueComparer<Dictionary<string, string>>(
                (x, y) => (x ?? new()).Count == (y ?? new()).Count && !(x ?? new()).Except(y ?? new()).Any(),
                v => v == null ? 0 : v.Aggregate(0, (h, kv) => HashCode.Combine(h, kv.Key, kv.Value)),
                v => v == null ? new() : new Dictionary<string, string>(v)));
        builder.Property(a => a.CreatedByName).HasColumnName("created_by_name").HasMaxLength(200);
        builder.Property(a => a.CreatedAt).HasColumnName("created_at");
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at");

        builder.HasOne<PhoneNumber>().WithMany().HasForeignKey(a => a.PhoneNumberId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<MediaAgency>().WithMany().HasForeignKey(a => a.MediaAgencyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => new { a.PhoneNumberId, a.StartDate }).HasDatabaseName("ix_media_assignments_number_start");
    }
}
