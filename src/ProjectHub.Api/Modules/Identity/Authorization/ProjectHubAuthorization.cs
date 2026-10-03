using Microsoft.EntityFrameworkCore;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Modules.Projects;
using ProjectHub.Api.Modules.Teams;

namespace ProjectHub.Api.Modules.Identity.Authorization;

internal sealed class ProjectHubAuthorization(ProjectHubDbContext db) : IProjectHubAuthorization
{
    public bool CanManageOrganization(UserContext user) => user.IsOrganizationAdmin;

    public bool CanCreateTeam(UserContext user) => user.IsOrganizationAdmin;

    public async Task<bool> HasProjectPermissionAsync(UserContext user, Guid projectId, ProjectPermission permission, CancellationToken ct)
    {
        if (user.IsOrganizationAdmin)
        {
            return await db.Set<Project>().AnyAsync(p => p.Id == projectId && p.OrganizationId == user.OrganizationId, ct);
        }

        var role = await db.Set<ProjectMember>()
            .Where(m => m.OrganizationId == user.OrganizationId && m.ProjectId == projectId && m.UserId == user.UserId)
            .Select(m => m.Role)
            .SingleOrDefaultAsync(ct);

        return role is not null && ProjectPermissions.Grants(role, permission);
    }

    public async Task<bool> CanManageTeamAsync(UserContext user, Guid teamId, CancellationToken ct)
    {
        if (user.IsOrganizationAdmin)
        {
            return await db.Set<Team>().AnyAsync(t => t.Id == teamId && t.OrganizationId == user.OrganizationId, ct);
        }

        return await db.Set<TeamMember>().AnyAsync(
            m => m.OrganizationId == user.OrganizationId && m.TeamId == teamId && m.UserId == user.UserId && m.Role == TeamRole.Owner,
            ct);
    }
}
