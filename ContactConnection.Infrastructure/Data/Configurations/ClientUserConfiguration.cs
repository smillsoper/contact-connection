using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

public class ClientUserConfiguration : IEntityTypeConfiguration<ClientUser>
{
    public void Configure(EntityTypeBuilder<ClientUser> b)
    {
        b.ToTable("client_users");
        b.HasKey(u => u.Id);
        b.Property(u => u.Id).HasColumnName("id");
        b.Property(u => u.Email).HasColumnName("email").HasMaxLength(320).IsRequired();
        b.Property(u => u.FirstName).HasColumnName("first_name").HasMaxLength(100).IsRequired();
        b.Property(u => u.LastName).HasColumnName("last_name").HasMaxLength(100).IsRequired();
        b.Property(u => u.PasswordHash).HasColumnName("password_hash").HasMaxLength(200);
        b.Property(u => u.MfaEnabled).HasColumnName("mfa_enabled");
        b.Property(u => u.MfaSecret).HasColumnName("mfa_secret").HasMaxLength(200);
        b.Property(u => u.IsActive).HasColumnName("is_active");
        b.Property(u => u.CanPlayRecordings).HasColumnName("can_play_recordings");
        b.Property(u => u.InviteTokenHash).HasColumnName("invite_token_hash").HasMaxLength(64);
        b.Property(u => u.InviteExpiresAt).HasColumnName("invite_expires_at");
        b.Property(u => u.TimeZone).HasColumnName("time_zone").HasMaxLength(100);
        b.Property(u => u.DefaultDashboardId).HasColumnName("default_dashboard_id");
        b.Property(u => u.LastLoginAt).HasColumnName("last_login_at");
        b.Property(u => u.CreatedAt).HasColumnName("created_at");
        b.Property(u => u.CreatedByAgentId).HasColumnName("created_by_agent_id");
        b.HasIndex(u => u.Email).IsUnique();
        b.HasIndex(u => u.InviteTokenHash);
        b.HasMany(u => u.Dashboards).WithOne().HasForeignKey(d => d.ClientUserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ClientUserDashboardConfiguration : IEntityTypeConfiguration<ClientUserDashboard>
{
    public void Configure(EntityTypeBuilder<ClientUserDashboard> b)
    {
        b.ToTable("client_user_dashboards");
        b.HasKey(d => new { d.ClientUserId, d.DashboardId });
        b.Property(d => d.ClientUserId).HasColumnName("client_user_id");
        b.Property(d => d.DashboardId).HasColumnName("dashboard_id");
        b.HasOne<Dashboard>().WithMany().HasForeignKey(d => d.DashboardId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(d => d.DashboardId);
    }
}

public class ClientUserAuditEntryConfiguration : IEntityTypeConfiguration<ClientUserAuditEntry>
{
    public void Configure(EntityTypeBuilder<ClientUserAuditEntry> b)
    {
        b.ToTable("client_user_audit");
        b.HasKey(a => a.Id);
        b.Property(a => a.Id).HasColumnName("id");
        b.Property(a => a.ClientUserId).HasColumnName("client_user_id");
        b.Property(a => a.Action).HasColumnName("action").HasMaxLength(40).IsRequired();
        b.Property(a => a.Detail).HasColumnName("detail").HasMaxLength(1000);
        b.Property(a => a.IpAddress).HasColumnName("ip_address").HasMaxLength(64);
        b.Property(a => a.ByAgentId).HasColumnName("by_agent_id");
        b.Property(a => a.At).HasColumnName("at");
        b.HasIndex(a => new { a.ClientUserId, a.At });
        b.HasIndex(a => a.At);
    }
}
