using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

public class ZipCodeLocationConfiguration : IEntityTypeConfiguration<ZipCodeLocation>
{
    public void Configure(EntityTypeBuilder<ZipCodeLocation> builder)
    {
        builder.ToTable("zip_codes");
        builder.HasKey(z => z.Zip);
        builder.Property(z => z.Zip).HasColumnName("zip").HasMaxLength(5);
        builder.Property(z => z.City).HasColumnName("city").HasMaxLength(100);
        builder.Property(z => z.State).HasColumnName("state").HasMaxLength(2);
        builder.Property(z => z.County).HasColumnName("county").HasMaxLength(100);
        builder.Property(z => z.Latitude).HasColumnName("latitude");
        builder.Property(z => z.Longitude).HasColumnName("longitude");
        builder.Property(z => z.AreaCodes).HasColumnName("area_codes");
        builder.Property(z => z.ImportedAt).HasColumnName("imported_at");
    }
}

public class AreaCodeLocationConfiguration : IEntityTypeConfiguration<AreaCodeLocation>
{
    public void Configure(EntityTypeBuilder<AreaCodeLocation> builder)
    {
        builder.ToTable("area_codes");
        builder.HasKey(a => a.AreaCode);
        builder.Property(a => a.AreaCode).HasColumnName("area_code").HasMaxLength(3);
        builder.Property(a => a.Latitude).HasColumnName("latitude");
        builder.Property(a => a.Longitude).HasColumnName("longitude");
        builder.Property(a => a.ZipCount).HasColumnName("zip_count");
        builder.Property(a => a.ImportedAt).HasColumnName("imported_at");
    }
}
