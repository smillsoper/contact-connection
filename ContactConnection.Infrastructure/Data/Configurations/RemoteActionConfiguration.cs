using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

public class RemoteActionConfiguration : IEntityTypeConfiguration<RemoteAction>
{
    public void Configure(EntityTypeBuilder<RemoteAction> b)
    {
        b.ToTable("remote_actions");
        b.HasKey(a => a.Id);
        b.Property(a => a.Id).HasColumnName("id");
        b.Property(a => a.TenantId).HasColumnName("tenant_id");
        b.Property(a => a.AgentId).HasColumnName("agent_id");
        b.Property(a => a.RequestedById).HasColumnName("requested_by_id");
        b.Property(a => a.RequestedByName).HasColumnName("requested_by_name").HasMaxLength(200).IsRequired();
        b.Property(a => a.Action).HasColumnName("action").HasMaxLength(20).IsRequired();
        b.Property(a => a.RequestedAt).HasColumnName("requested_at");
        b.Property(a => a.CompletedAt).HasColumnName("completed_at");
        b.Property(a => a.Ok).HasColumnName("ok");
        b.Property(a => a.Detail).HasColumnName("detail").HasMaxLength(8000);
        b.HasIndex(a => new { a.AgentId, a.RequestedAt }).HasDatabaseName("ix_remote_actions_agent_requested");
    }
}
