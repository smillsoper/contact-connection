using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Persistence.Configurations;

public class AgentGroupMemberConfiguration : IEntityTypeConfiguration<AgentGroupMember>
{
    public void Configure(EntityTypeBuilder<AgentGroupMember> builder)
    {
        builder.ToTable("agent_group_members");
        builder.HasKey(m => new { m.GroupId, m.AgentId });

        builder.Property(m => m.GroupId).HasColumnName("group_id");
        builder.Property(m => m.AgentId).HasColumnName("agent_id");
        builder.Property(m => m.JoinedAt).HasColumnName("joined_at");

        builder.HasIndex(m => m.AgentId);
    }
}
