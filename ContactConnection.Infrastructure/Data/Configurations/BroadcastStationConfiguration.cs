using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

public class BroadcastStationConfiguration : IEntityTypeConfiguration<BroadcastStation>
{
    public void Configure(EntityTypeBuilder<BroadcastStation> builder)
    {
        builder.ToTable("broadcast_stations");
        builder.HasKey(s => s.FacilityId);
        builder.Property(s => s.FacilityId).HasColumnName("facility_id").ValueGeneratedNever();
        builder.Property(s => s.CallSign).HasColumnName("call_sign").HasMaxLength(20).IsRequired();
        builder.Property(s => s.ServiceCode).HasColumnName("service_code").HasMaxLength(10).IsRequired();
        builder.Property(s => s.CommunityCity).HasColumnName("community_city").HasMaxLength(100);
        builder.Property(s => s.CommunityState).HasColumnName("community_state").HasMaxLength(10);
        builder.Property(s => s.Latitude).HasColumnName("latitude");
        builder.Property(s => s.Longitude).HasColumnName("longitude");
        builder.Property(s => s.NetworkAffiliation).HasColumnName("network_affiliation").HasMaxLength(100);
        builder.Property(s => s.ImportedAt).HasColumnName("imported_at");
        builder.HasIndex(s => s.CallSign).HasDatabaseName("ix_broadcast_stations_call_sign");
    }
}
