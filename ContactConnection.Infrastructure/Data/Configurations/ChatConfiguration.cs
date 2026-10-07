using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

// Team chat (S183). Id lists are native uuid[] columns (Npgsql primitive collections) so "mentions me" / "may post"
// translate to = ANY(...) in SQL.

public class ChatChannelConfiguration : IEntityTypeConfiguration<ChatChannel>
{
    public void Configure(EntityTypeBuilder<ChatChannel> b)
    {
        b.ToTable("chat_channels");
        b.HasKey(c => c.Id);
        b.Property(c => c.Id).HasColumnName("id");
        b.Property(c => c.TenantId).HasColumnName("tenant_id");
        b.Property(c => c.Kind).HasColumnName("kind").HasMaxLength(10).IsRequired();
        b.Property(c => c.Name).HasColumnName("name").HasMaxLength(80).IsRequired();
        b.Property(c => c.Description).HasColumnName("description").HasMaxLength(300);
        b.Property(c => c.IsPrivate).HasColumnName("is_private");
        b.Property(c => c.PostingRestricted).HasColumnName("posting_restricted");
        b.Property(c => c.PosterIds).HasColumnName("poster_ids").HasColumnType("uuid[]");
        b.Property(c => c.PosterRoleIds).HasColumnName("poster_role_ids").HasColumnType("uuid[]");
        b.Property(c => c.PinnerIds).HasColumnName("pinner_ids").HasColumnType("uuid[]");
        b.Property(c => c.PinnerRoleIds).HasColumnName("pinner_role_ids").HasColumnType("uuid[]");
        b.Property(c => c.ModeratorIds).HasColumnName("moderator_ids").HasColumnType("uuid[]");
        b.Property(c => c.ModeratorRoleIds).HasColumnName("moderator_role_ids").HasColumnType("uuid[]");
        b.Property(c => c.MembershipLocked).HasColumnName("membership_locked");
        b.Property(c => c.AssignedRoleIds).HasColumnName("assigned_role_ids").HasColumnType("uuid[]");
        b.Property(c => c.RetiredAt).HasColumnName("retired_at");
        b.Property(c => c.DirectKey).HasColumnName("direct_key").HasMaxLength(4000);
        b.Property(c => c.CreatedById).HasColumnName("created_by_id");
        b.Property(c => c.CreatedAt).HasColumnName("created_at");
        b.Property(c => c.UpdatedAt).HasColumnName("updated_at");
        b.Property(c => c.LastMessageAt).HasColumnName("last_message_at");
        b.Ignore(c => c.IsRetired);
        b.Ignore(c => c.IsDirect);
        b.HasIndex(c => c.DirectKey).IsUnique().HasFilter("direct_key IS NOT NULL").HasDatabaseName("ux_chat_channels_direct_key");
    }
}

public class ChatMemberConfiguration : IEntityTypeConfiguration<ChatMember>
{
    public void Configure(EntityTypeBuilder<ChatMember> b)
    {
        b.ToTable("chat_members");
        b.HasKey(m => new { m.ChannelId, m.AgentId });
        b.Property(m => m.ChannelId).HasColumnName("channel_id");
        b.Property(m => m.AgentId).HasColumnName("agent_id");
        b.Property(m => m.IsAssigned).HasColumnName("is_assigned");
        b.Property(m => m.JoinedAt).HasColumnName("joined_at");
        b.Property(m => m.LastReadAt).HasColumnName("last_read_at");
        b.Property(m => m.LeftAt).HasColumnName("left_at");
        b.Ignore(m => m.IsActive);
        b.HasOne<ChatChannel>().WithMany().HasForeignKey(m => m.ChannelId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(m => m.AgentId).HasDatabaseName("ix_chat_members_agent");
    }
}

public class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> b)
    {
        b.ToTable("chat_messages");
        b.HasKey(m => m.Id);
        b.Property(m => m.Id).HasColumnName("id");
        b.Property(m => m.ChannelId).HasColumnName("channel_id");
        b.Property(m => m.AgentId).HasColumnName("agent_id");
        b.Property(m => m.Kind).HasColumnName("kind").HasMaxLength(10).IsRequired();
        b.Property(m => m.ParentId).HasColumnName("parent_id");
        b.Property(m => m.Body).HasColumnName("body").HasMaxLength(ChatMessage.MaxHtmlLength).IsRequired();
        b.Property(m => m.MentionIds).HasColumnName("mention_ids").HasColumnType("uuid[]");
        b.Property(m => m.Format).HasColumnName("format").HasMaxLength(8).HasDefaultValue(ChatMessageFormat.Text).IsRequired();
        b.Property(m => m.BodyText).HasColumnName("body_text").HasMaxLength(ChatMessage.MaxLength + 20).HasDefaultValue("").IsRequired();
        b.Property(m => m.ReplyCount).HasColumnName("reply_count");
        b.Property(m => m.LastReplyAt).HasColumnName("last_reply_at");
        b.Property(m => m.CreatedAt).HasColumnName("created_at");
        b.Property(m => m.EditedAt).HasColumnName("edited_at");
        b.Property(m => m.DeletedAt).HasColumnName("deleted_at");
        b.Property(m => m.PinnedAt).HasColumnName("pinned_at");
        b.Property(m => m.PinnedById).HasColumnName("pinned_by_id");
        b.HasOne<ChatChannel>().WithMany().HasForeignKey(m => m.ChannelId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(m => new { m.ChannelId, m.ParentId, m.CreatedAt }).HasDatabaseName("ix_chat_messages_channel_parent_created");
    }
}

public class ChatFileConfiguration : IEntityTypeConfiguration<ChatFile>
{
    public void Configure(EntityTypeBuilder<ChatFile> b)
    {
        b.ToTable("chat_files");
        b.HasKey(f => f.Id);
        b.Property(f => f.Id).HasColumnName("id");
        b.Property(f => f.TenantId).HasColumnName("tenant_id");
        b.Property(f => f.AgentId).HasColumnName("agent_id");
        b.Property(f => f.ContentType).HasColumnName("content_type").HasMaxLength(40).IsRequired();
        b.Property(f => f.SizeBytes).HasColumnName("size_bytes");
        b.Property(f => f.StorageKey).HasColumnName("storage_key").HasMaxLength(200).IsRequired();
        b.Property(f => f.CreatedAt).HasColumnName("created_at");
    }
}

public class ChatPersonalPinConfiguration : IEntityTypeConfiguration<ChatPersonalPin>
{
    public void Configure(EntityTypeBuilder<ChatPersonalPin> b)
    {
        b.ToTable("chat_personal_pins");
        b.HasKey(p => new { p.AgentId, p.MessageId });
        b.Property(p => p.AgentId).HasColumnName("agent_id");
        b.Property(p => p.MessageId).HasColumnName("message_id");
        b.Property(p => p.ChannelId).HasColumnName("channel_id");
        b.Property(p => p.CreatedAt).HasColumnName("created_at");
        b.HasOne<ChatMessage>().WithMany().HasForeignKey(p => p.MessageId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(p => new { p.AgentId, p.ChannelId }).HasDatabaseName("ix_chat_personal_pins_agent_channel");
    }
}

public class ChatReactionConfiguration : IEntityTypeConfiguration<ChatReaction>
{
    public void Configure(EntityTypeBuilder<ChatReaction> b)
    {
        b.ToTable("chat_reactions");
        b.HasKey(r => new { r.MessageId, r.AgentId, r.Emoji });
        b.Property(r => r.MessageId).HasColumnName("message_id");
        b.Property(r => r.AgentId).HasColumnName("agent_id");
        b.Property(r => r.Emoji).HasColumnName("emoji").HasMaxLength(16);
        b.Property(r => r.CreatedAt).HasColumnName("created_at");
        b.HasOne<ChatMessage>().WithMany().HasForeignKey(r => r.MessageId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class AgentSupervisorConfiguration : IEntityTypeConfiguration<AgentSupervisor>
{
    public void Configure(EntityTypeBuilder<AgentSupervisor> b)
    {
        b.ToTable("agent_supervisors");
        b.HasKey(s => new { s.AgentId, s.SupervisorId });
        b.Property(s => s.AgentId).HasColumnName("agent_id");
        b.Property(s => s.SupervisorId).HasColumnName("supervisor_id");
        b.Property(s => s.CreatedAt).HasColumnName("created_at");
        b.HasOne<Agent>().WithMany().HasForeignKey(s => s.AgentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Agent>().WithMany().HasForeignKey(s => s.SupervisorId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(s => s.SupervisorId).HasDatabaseName("ix_agent_supervisors_supervisor");
    }
}

public class HelpRequestConfiguration : IEntityTypeConfiguration<HelpRequest>
{
    public void Configure(EntityTypeBuilder<HelpRequest> b)
    {
        b.ToTable("help_requests");
        b.HasKey(h => h.Id);
        b.Property(h => h.Id).HasColumnName("id");
        b.Property(h => h.TenantId).HasColumnName("tenant_id");
        b.Property(h => h.AgentId).HasColumnName("agent_id");
        b.Property(h => h.Status).HasColumnName("status").HasMaxLength(12).IsRequired();
        b.Property(h => h.Note).HasColumnName("note").HasMaxLength(300);
        b.Property(h => h.CallRecordId).HasColumnName("call_record_id");
        b.Property(h => h.NotifiedIds).HasColumnName("notified_ids").HasColumnType("uuid[]");
        b.Property(h => h.WentToAllSupervisors).HasColumnName("went_to_all_supervisors");
        b.Property(h => h.ClaimedById).HasColumnName("claimed_by_id");
        b.Property(h => h.ChannelId).HasColumnName("channel_id");
        b.Property(h => h.CreatedAt).HasColumnName("created_at");
        b.Property(h => h.ClosedAt).HasColumnName("closed_at");
        b.Ignore(h => h.IsOpen);
        b.HasIndex(h => new { h.Status, h.CreatedAt }).HasDatabaseName("ix_help_requests_status_created");
    }
}
