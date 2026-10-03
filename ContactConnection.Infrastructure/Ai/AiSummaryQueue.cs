using System.Threading.Channels;

namespace ContactConnection.Infrastructure.Ai;

/// <summary>
/// Calls waiting for an automatic AI summary (AI step c, S171). The flow engine adds a call when its script
/// finishes — the moment the context is complete, disposition included — and returns immediately; a
/// background processor (API) does the slow model call. Never put a slow external call in the agent's
/// request path. In-memory: a restart drops pending items, and the agent can still press Generate.
/// </summary>
public sealed class AiSummaryQueue
{
    public record Item(Guid TenantId, Guid CallRecordId, Guid? AgentId);

    private readonly Channel<Item> _channel = Channel.CreateBounded<Item>(
        new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest });

    public void Enqueue(Item item) => _channel.Writer.TryWrite(item);

    public IAsyncEnumerable<Item> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}
