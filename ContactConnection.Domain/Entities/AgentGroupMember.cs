namespace ContactConnection.Domain.Entities;

public class AgentGroupMember
{
    public Guid GroupId { get; private set; }
    public Guid AgentId { get; private set; }
    public DateTimeOffset JoinedAt { get; private set; }

    private AgentGroupMember() { }

    public static AgentGroupMember Create(Guid groupId, Guid agentId)
        => new() { GroupId = groupId, AgentId = agentId, JoinedAt = DateTimeOffset.UtcNow };
}
