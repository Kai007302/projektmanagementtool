using ProjectHub.Api.Infrastructure.Outcomes;
using ProjectHub.Api.Modules.Identity;
using ProjectHub.Api.Modules.Identity.Authorization;

namespace ProjectHub.Api.Modules.Projects;

/// <summary>
/// Turns project permissions into failures: projects the caller cannot see are "not found"
/// (their existence is not revealed), visible ones without the needed permission are "forbidden".
/// </summary>
public sealed class ProjectAccess(IProjectHubAuthorization authorization)
{
    public async Task<ServiceFailure?> RequireAsync(UserContext user, Guid projectId, ProjectPermission permission, CancellationToken ct)
    {
        if (!await authorization.CanViewProjectAsync(user, projectId, ct))
        {
            return ServiceFailure.NotFound("Project");
        }

        if (permission != ProjectPermission.View && !await authorization.HasProjectPermissionAsync(user, projectId, permission, ct))
        {
            return ServiceFailure.Forbidden($"Missing project permission '{permission}'.");
        }

        return null;
    }

    public async Task<ProjectCapabilities> CapabilitiesAsync(UserContext user, Guid projectId, CancellationToken ct) =>
        new(
            await authorization.HasProjectPermissionAsync(user, projectId, ProjectPermission.Contribute, ct),
            await authorization.HasProjectPermissionAsync(user, projectId, ProjectPermission.Edit, ct),
            await authorization.HasProjectPermissionAsync(user, projectId, ProjectPermission.Manage, ct));
}

/// <summary>
/// What the caller may do in a project, so the UI only offers allowed actions. <c>CanShareWithOrganization</c>: may make
/// the project visible to the whole organization (organization admins and leads of its department, ADR 0021).
/// </summary>
public sealed record ProjectCapabilities(bool CanContribute, bool CanEdit, bool CanManage, bool CanShareWithOrganization = false);
