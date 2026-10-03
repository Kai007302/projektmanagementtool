using Microsoft.AspNetCore.SignalR;
using ProjectHub.Api.Modules.Identity;
using ProjectHub.Api.Modules.Identity.Authorization;

namespace ProjectHub.Api.Modules.Whiteboard;

/// <summary>Messages the server pushes to whiteboard clients.</summary>
public interface IWhiteboardsClient
{
    /// <summary>A Yjs update someone else made; the client applies it to its document.</summary>
    Task Update(WhiteboardUpdateMessage message);

    /// <summary>Where someone's pointer is. Ephemeral, never stored.</summary>
    Task Presence(PresenceMessage message);

    /// <summary>A connection left the whiteboard; its pointer disappears.</summary>
    Task PresenceLeft(PresenceLeftMessage message);
}

public sealed record WhiteboardUpdateMessage(Guid WhiteboardId, byte[] Update);

public sealed record PresenceMessage(Guid WhiteboardId, string ConnectionId, Guid UserId, string Name, double? X, double? Y, string? SelectedObjectId);

public sealed record PresenceLeftMessage(Guid WhiteboardId, string ConnectionId);

public sealed record JoinResponse(byte[] State, bool CanEdit);

public sealed record PresenceRequest(double? X, double? Y, string? SelectedObjectId);

/// <summary>
/// Yjs sync for whiteboards (ADR 0009). Updates are checked and stored before they are fanned out; presence
/// only travels between connections. Messages and errors never reveal whether a whiteboard the caller
/// cannot see exists.
/// </summary>
public sealed class WhiteboardsHub(
    WhiteboardService whiteboards,
    WhiteboardDocumentStore documents,
    WhiteboardEngine engine,
    WhiteboardOptions options,
    ILogger<WhiteboardsHub> logger) : Hub<IWhiteboardsClient>
{
    public const string Path = IdentityModule.ApiV1Prefix + "/hubs/whiteboards";

    /// <summary>Base64 adds a third to the update size; leave room for the JSON envelope.</summary>
    public static long MaximumReceiveMessageSize(WhiteboardOptions options) => options.MaxUpdateBytes * 4L / 3 + 4096;

    public static string GroupOf(Guid whiteboardId) => $"whiteboard:{whiteboardId:N}";

    public async Task<JoinResponse> Join(Guid whiteboardId)
    {
        var user = CurrentUser();
        var (board, failure) = await whiteboards.RequireAsync(user, whiteboardId, ProjectPermission.View, Context.ConnectionAborted);
        if (failure is not null)
        {
            throw new HubException("Whiteboard not found.");
        }

        // Join first, then load: an update stored in between arrives twice, which Yjs ignores, instead of never.
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupOf(board!.Id), Context.ConnectionAborted);
        Joined().Add(board.Id);
        var state = await documents.LoadAsync(board.Id, Context.ConnectionAborted);
        var canEdit = (await whiteboards.RequireAsync(user, board.Id, ProjectPermission.Contribute, Context.ConnectionAborted)).Failure is null;
        return new JoinResponse(state, canEdit);
    }

    public async Task Leave(Guid whiteboardId)
    {
        if (Joined().Remove(whiteboardId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupOf(whiteboardId), Context.ConnectionAborted);
            await Clients.OthersInGroup(GroupOf(whiteboardId)).PresenceLeft(new PresenceLeftMessage(whiteboardId, Context.ConnectionId));
        }
    }

    /// <summary>Validates, stores and forwards one Yjs update (v1 encoding).</summary>
    public async Task PushUpdate(Guid whiteboardId, byte[] update)
    {
        var user = CurrentUser();
        var (board, failure) = await whiteboards.RequireAsync(user, whiteboardId, ProjectPermission.Contribute, Context.ConnectionAborted);
        if (failure is not null)
        {
            throw new HubException(board is null ? "Whiteboard not found." : "You may not change this whiteboard.");
        }

        if (update.Length > options.MaxUpdateBytes)
        {
            throw new HubException($"Update too large (at most {options.MaxUpdateBytes} bytes).");
        }

        if (!await IsValidAsync(update))
        {
            logger.LogInformation("Rejected an invalid update for whiteboard {WhiteboardId} from {UserId}", whiteboardId, user.UserId);
            throw new HubException("Invalid update.");
        }

        await documents.AppendAsync(board!, user.UserId, update, Context.ConnectionAborted);
        try
        {
            await Clients.OthersInGroup(GroupOf(board!.Id)).Update(new WhiteboardUpdateMessage(board.Id, update));
        }
        catch (Exception ex)
        {
            // Stored already; the others get it when they next load.
            logger.LogWarning(ex, "Fan-out of a whiteboard update for {WhiteboardId} failed", whiteboardId);
        }
    }

    /// <summary>Forwards the caller's pointer to the others on the whiteboard. Requires a prior <see cref="Join"/>.</summary>
    public Task UpdatePresence(Guid whiteboardId, PresenceRequest presence)
    {
        if (!Joined().Contains(whiteboardId))
        {
            throw new HubException("Join the whiteboard first.");
        }

        if (presence.X is { } x && !double.IsFinite(x) || presence.Y is { } y && !double.IsFinite(y) || presence.SelectedObjectId is { Length: > 100 })
        {
            throw new HubException("Invalid presence.");
        }

        var user = CurrentUser();
        return Clients.OthersInGroup(GroupOf(whiteboardId)).Presence(new PresenceMessage(
            whiteboardId, Context.ConnectionId, user.UserId, user.DisplayName, presence.X, presence.Y, presence.SelectedObjectId));
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        foreach (var whiteboardId in Joined())
        {
            await Clients.OthersInGroup(GroupOf(whiteboardId)).PresenceLeft(new PresenceLeftMessage(whiteboardId, Context.ConnectionId));
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>Format check in managed code first, then a real decode in the isolated engine.</summary>
    private async Task<bool> IsValidAsync(byte[] update)
    {
        if (!YjsUpdateValidator.IsWellFormed(update))
        {
            return false;
        }

        try
        {
            return await engine.IsValidUpdateAsync(update, Context.ConnectionAborted);
        }
        catch (WhiteboardEngineException ex)
        {
            logger.LogWarning(ex, "The whiteboard engine failed on an update; it is rejected");
            return false;
        }
    }

    private UserContext CurrentUser() =>
        UserContextMiddleware.FromHttpContext(Context.GetHttpContext()) ?? throw new HubException("Not signed in.");

    /// <summary>Whiteboards this connection joined. Lives with the connection only (for presence), never shared.</summary>
    private HashSet<Guid> Joined()
    {
        if (Context.Items.TryGetValue(nameof(Joined), out var value) && value is HashSet<Guid> joined)
        {
            return joined;
        }

        joined = [];
        Context.Items[nameof(Joined)] = joined;
        return joined;
    }
}
