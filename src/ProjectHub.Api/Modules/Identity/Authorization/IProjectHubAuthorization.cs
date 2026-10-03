namespace ProjectHub.Api.Modules.Identity.Authorization;

/// <summary>
/// Central server-side authorization. Every check is scoped to the caller's organization first,
/// so resources of another organization are never accessible.
/// </summary>
public interface IProjectHubAuthorization
{
    bool CanManageOrganization(UserContext user);

    Task<bool> HasProjectPermissionAsync(UserContext user, Guid projectId, ProjectPermission permission, CancellationToken ct);

    Task<bool> CanViewProjectAsync(UserContext user, Guid projectId, CancellationToken ct) =>
        HasProjectPermissionAsync(user, projectId, ProjectPermission.View, ct);

    Task<bool> CanEditProjectAsync(UserContext user, Guid projectId, CancellationToken ct) =>
        HasProjectPermissionAsync(user, projectId, ProjectPermission.Edit, ct);

    Task<bool> CanManageProjectAsync(UserContext user, Guid projectId, CancellationToken ct) =>
        HasProjectPermissionAsync(user, projectId, ProjectPermission.Manage, ct);

    bool CanCreateTeam(UserContext user);

    /// <summary>Organization admins and owners of the team. False for teams of other organizations.</summary>
    Task<bool> CanManageTeamAsync(UserContext user, Guid teamId, CancellationToken ct);
}
