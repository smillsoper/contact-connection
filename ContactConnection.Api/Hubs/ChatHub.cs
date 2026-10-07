using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ContactConnection.Api.Hubs;

/// <summary>
/// Team chat push channel (S183), at /hubs/chat. Delivery only — every chat action is an HTTP call (tenant-resolved,
/// permission-checked), which then pushes <see cref="IChatHubClient.ReceiveChatEvent"/> to the people it concerns.
/// Each connection joins its user's group and its tenant's group (live agent status for everyone).
/// </summary>
[Authorize]
public class ChatHub : Hub<IChatHubClient>
{
    public static string UserGroup(Guid agentId) => $"chat-user:{agentId}";
    public static string TenantGroup(Guid tenantId) => $"chat-tenant:{tenantId}";

    public override async Task OnConnectedAsync()
    {
        if (Guid.TryParse(Context.User?.FindFirst("sub")?.Value, out var agentId))
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(agentId));
        if (Guid.TryParse(Context.User?.FindFirst("tenant_id")?.Value, out var tenantId))
            await Groups.AddToGroupAsync(Context.ConnectionId, TenantGroup(tenantId));
        await base.OnConnectedAsync();
    }
}

public interface IChatHubClient
{
    /// <summary>type: message · message-updated · reactions · channel · channel-removed · read · typing · presence · help</summary>
    Task ReceiveChatEvent(string type, string payloadJson);
}
