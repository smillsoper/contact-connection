using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

/// <summary>ContactConnection support sessions inside tenant portals (S184) — platform schema.</summary>
public class SupportSessionConfiguration : IEntityTypeConfiguration<SupportSession>
{
    public void Configure(EntityTypeBuilder<SupportSession> b)
    {
        b.ToTable("support_sessions");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).HasColumnName("id");
        b.Property(s => s.TenantId).HasColumnName("tenant_id");
        b.Property(s => s.EntraOid).HasColumnName("entra_oid").HasMaxLength(64).IsRequired();
        b.Property(s => s.Email).HasColumnName("email").HasMaxLength(320).IsRequired();
        b.Property(s => s.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        b.Property(s => s.PlatformRole).HasColumnName("platform_role").HasMaxLength(20).IsRequired();
        b.Property(s => s.Reason).HasColumnName("reason").HasMaxLength(300).IsRequired();
        b.Property(s => s.AgentId).HasColumnName("agent_id");
        b.Property(s => s.StartedAt).HasColumnName("started_at");
        b.Property(s => s.ExpiresAt).HasColumnName("expires_at");
        b.Property(s => s.EndedAt).HasColumnName("ended_at");
        b.HasIndex(s => new { s.TenantId, s.StartedAt }).HasDatabaseName("ix_support_sessions_tenant_started");
    }
}
