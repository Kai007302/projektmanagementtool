using Microsoft.AspNetCore.SignalR;
using ProjectHub.Api.Modules.Identity;
using ProjectHub.Api.Modules.Identity.Authorization;
using ProjectHub.Api.Modules.Projects;

namespace ProjectHub.Api.Modules.Realtime;

/// <summary>Messages the server pushes to browsers.</summary>
public interface IProjectEventsClient
{
    /// <summary>Something in the project changed; the client reloads what it shows through the API.</summary>
    Task ProjectChanged(ProjectChangedMessage message);
}

public sealed record ProjectChangedMessage(Guid ProjectId, string Area);

/// <summary>
/// Realtime notifications per project (ADR 0003). Clients join a project's group only with View
/// permission. Messages carry ids, never content, so a member removed later learns nothing new.
/// </summary>
public sealed class ProjectEventsHub(ProjectAccess access) : Hub<IProjectEventsClient>
{
    public const string Path = IdentityModule.ApiV1Prefix + "/hubs/projects";

    public static string GroupOf(Guid projectId) => $"project:{projectId:N}";

    public async Task JoinProject(Guid projectId)
    {
        var user = UserContextMiddleware.FromHttpContext(Context.GetHttpContext())
                   ?? throw new HubException("Not signed in.");
        if (await access.RequireAsync(user, projectId, ProjectPermission.View, Context.ConnectionAborted) is not null)
        {
            throw new HubException("Project not found.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupOf(projectId), Context.ConnectionAborted);
    }

    public Task LeaveProject(Guid projectId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupOf(projectId), Context.ConnectionAborted);
}
