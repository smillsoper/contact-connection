using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ContactConnection.Api.Hubs;

/// <summary>
/// Live refresh for the client portal (S181). Client users only ever receive a data-free "refresh" nudge — the browser then
/// refetches through the scoped client-portal endpoints — so nothing from the supervisor feeds (agent names, caller
/// numbers…) can reach them. Each connection joins its own tenant's group on connect; there is nothing to call.
/// </summary>
[Authorize(Policy = "ClientUser")]
public class ClientDashboardHub : Hub<IClientDashboardHubClient>
{
    public static string Group(Guid tenantId) => $"client-dash:{tenantId}";

    public override async Task OnConnectedAsync()
    {
        if (Guid.TryParse(Context.User?.FindFirst("tenant_id")?.Value, out var tenantId))
            await Groups.AddToGroupAsync(Context.ConnectionId, Group(tenantId));
        await base.OnConnectedAsync();
    }
}

public interface IClientDashboardHubClient
{
    Task ReceiveRefresh();
}
