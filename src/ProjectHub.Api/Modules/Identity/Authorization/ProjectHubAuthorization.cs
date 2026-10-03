using Microsoft.EntityFrameworkCore;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Modules.Projects;
using ProjectHub.Api.Modules.Teams;
using ProjectHub.Api.Modules.Users;

namespace ProjectHub.Api.Modules.Identity.Authorization;

internal sealed class ProjectHubAuthorization(ProjectHubDbContext db) : IProjectHubAuthorization
{
    public bool CanManageOrganization(UserContext user) => user.IsOrganizationAdmin;

    public bool CanCreateTeam(UserContext user) => user.IsOrganizationAdmin;

    public async Task<bool> HasProjectPermissionAsync(UserContext user, Guid projectId, ProjectPermission permission, CancellationToken ct)
    {
        var activeProject = db.Set<Project>()
            .Where(p => p.Id == projectId && p.OrganizationId == user.OrganizationId && p.DeletedAt == null);

        if (user.IsOrganizationAdmin)
        {
            return await activeProject.AnyAsync(ct);
        }

        var role = await (
                from project in activeProject
                join member in db.Set<ProjectMember>() on project.Id equals member.ProjectId
                where member.OrganizationId == user.OrganizationId && member.UserId == user.UserId
                select member.Role)
            .SingleOrDefaultAsync(ct);

        return role is not null && ProjectPermissions.Grants(role, permission);
    }

    public async Task<bool> CanBeAssignedAsync(Guid organizationId, Guid projectId, Guid userId, CancellationToken ct)
    {
        var user = await db.Set<AppUser>().AsNoTracking()
            .Where(u => u.Id == userId && u.OrganizationId == organizationId && u.Status == UserStatus.Active)
            .Select(u => new UserContext(u.Id, u.OrganizationId, u.OrganizationRole, u.DisplayName, u.Email))
            .SingleOrDefaultAsync(ct);

        return user is not null && await HasProjectPermissionAsync(user, projectId, ProjectPermission.Contribute, ct);
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
