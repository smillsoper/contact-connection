using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

public class ScreenViewSessionConfiguration : IEntityTypeConfiguration<ScreenViewSession>
{
    public void Configure(EntityTypeBuilder<ScreenViewSession> b)
    {
        b.ToTable("screen_view_sessions");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).HasColumnName("id");
        b.Property(s => s.TenantId).HasColumnName("tenant_id");
        b.Property(s => s.AgentId).HasColumnName("agent_id");
        b.Property(s => s.ViewerId).HasColumnName("viewer_id");
        b.Property(s => s.ViewerName).HasColumnName("viewer_name").HasMaxLength(200).IsRequired();
        b.Property(s => s.RequestedAt).HasColumnName("requested_at");
        b.Property(s => s.ConnectedAt).HasColumnName("connected_at");
        b.Property(s => s.EndedAt).HasColumnName("ended_at");
        b.Property(s => s.EndReason).HasColumnName("end_reason").HasMaxLength(120);
        b.Property(s => s.PointCount).HasColumnName("point_count");
        b.Property(s => s.DrawCount).HasColumnName("draw_count");
        b.HasIndex(s => new { s.AgentId, s.RequestedAt }).HasDatabaseName("ix_screen_view_sessions_agent_requested");
    }
}
