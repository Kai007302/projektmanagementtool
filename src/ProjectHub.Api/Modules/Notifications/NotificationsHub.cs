using Microsoft.AspNetCore.SignalR;
using ProjectHub.Api.Modules.Identity;

namespace ProjectHub.Api.Modules.Notifications;

public interface INotificationsClient
{
    /// <summary>The person's notifications changed; carries only the unread count, the client reloads the list.</summary>
    Task NotificationsChanged(NotificationsChangedMessage message);
}

public sealed record NotificationsChangedMessage(int UnreadCount);

/// <summary>Every connection joins the group of its own person; there is nothing else to join.</summary>
public sealed class NotificationsHub : Hub<INotificationsClient>
{
    public const string Path = IdentityModule.ApiV1Prefix + "/hubs/notifications";

    public static string GroupOf(Guid userId) => $"user:{userId:N}";

    public override async Task OnConnectedAsync()
    {
        var user = UserContextMiddleware.FromHttpContext(Context.GetHttpContext());
        if (user is null)
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupOf(user.UserId), Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }
}
