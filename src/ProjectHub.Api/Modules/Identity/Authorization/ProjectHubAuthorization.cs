using Microsoft.EntityFrameworkCore;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Modules.Departments;
using ProjectHub.Api.Modules.Projects;
using ProjectHub.Api.Modules.Users;

namespace ProjectHub.Api.Modules.Identity.Authorization;

internal sealed class ProjectHubAuthorization(ProjectHubDbContext db) : IProjectHubAuthorization
{
    public bool CanManageOrganization(UserContext user) => user.IsOrganizationAdmin;

    /// <summary>
    /// The highest of: organization admin (everything), lead of the project's department (everything),
    /// the project role, and read access through the project's visibility (docs/PERMISSIONS.md, ADR 0021).
    /// </summary>
    public async Task<bool> HasProjectPermissionAsync(UserContext user, Guid projectId, ProjectPermission permission, CancellationToken ct)
    {
        var access = await (
                from project in db.Set<Project>()
                where project.Id == projectId && project.OrganizationId == user.OrganizationId && project.DeletedAt == null
                select new
                {
                    project.Visibility,
                    ProjectRole = db.Set<ProjectMember>()
                        .Where(m => m.ProjectId == project.Id && m.UserId == user.UserId)
                        .Select(m => m.Role)
                        .FirstOrDefault(),
                    DepartmentRole = db.Set<DepartmentMember>()
                        .Where(m => m.DepartmentId == project.DepartmentId && m.UserId == user.UserId && m.OrganizationId == user.OrganizationId)
                        .Select(m => m.Role)
                        .FirstOrDefault(),
                })
            .SingleOrDefaultAsync(ct);

        if (access is null)
        {
            return false;
        }

        if (user.IsOrganizationAdmin || access.DepartmentRole == DepartmentRole.Lead)
        {
            return true;
        }

        if (access.ProjectRole is not null && ProjectPermissions.Grants(access.ProjectRole, permission))
        {
            return true;
        }

        return permission == ProjectPermission.View
               && (access.Visibility == ProjectVisibility.Organization
                   || (access.Visibility == ProjectVisibility.Department && access.DepartmentRole == DepartmentRole.Member));
    }

    public IQueryable<Project> VisibleProjects(UserContext user)
    {
        var userId = user.UserId;
        var projects = db.Set<Project>().Where(p => p.OrganizationId == user.OrganizationId && p.DeletedAt == null);
        if (user.IsOrganizationAdmin)
        {
            return projects;
        }

        var departments = db.Set<DepartmentMember>().Where(m => m.OrganizationId == user.OrganizationId && m.UserId == userId);
        return projects.Where(p =>
            p.Visibility == ProjectVisibility.Organization
            || db.Set<ProjectMember>().Any(m => m.ProjectId == p.Id && m.UserId == userId)
            || departments.Any(m => m.DepartmentId == p.DepartmentId
                                    && (m.Role == DepartmentRole.Lead
                                        || (m.Role == DepartmentRole.Member && p.Visibility == ProjectVisibility.Department))));
    }

    public async Task<bool> SeesProjectOnlyAsOrganizationAdminAsync(UserContext user, Guid projectId, CancellationToken ct) =>
        user.IsOrganizationAdmin
        && !await HasProjectPermissionAsync(user with { OrganizationRole = OrganizationRole.Member }, projectId, ProjectPermission.View, ct);

    public async Task<bool> CanBeAssignedAsync(Guid organizationId, Guid projectId, Guid userId, CancellationToken ct)
    {
        var user = await db.Set<AppUser>().AsNoTracking()
            .Where(u => u.Id == userId && u.OrganizationId == organizationId && u.Status == UserStatus.Active)
            .Select(u => new UserContext(u.Id, u.OrganizationId, u.OrganizationRole, u.DisplayName, u.Email))
            .SingleOrDefaultAsync(ct);

        return user is not null && await HasProjectPermissionAsync(user, projectId, ProjectPermission.Contribute, ct);
    }

    public Task<string?> DepartmentRoleAsync(UserContext user, Guid departmentId, CancellationToken ct) =>
        db.Set<DepartmentMember>()
            .Where(m => m.OrganizationId == user.OrganizationId && m.DepartmentId == departmentId && m.UserId == user.UserId)
            .Select(m => m.Role)
            .SingleOrDefaultAsync(ct);

    public async Task<bool> CanManageDepartmentAsync(UserContext user, Guid departmentId, CancellationToken ct) =>
        user.IsOrganizationAdmin
            ? await db.Set<Department>().AnyAsync(d => d.Id == departmentId && d.OrganizationId == user.OrganizationId, ct)
            : await DepartmentRoleAsync(user, departmentId, ct) == DepartmentRole.Lead;

    public async Task<bool> CanCreateInDepartmentAsync(UserContext user, Guid departmentId, CancellationToken ct) =>
        user.IsOrganizationAdmin
            ? await db.Set<Department>().AnyAsync(d => d.Id == departmentId && d.OrganizationId == user.OrganizationId, ct)
            : await DepartmentRoleAsync(user, departmentId, ct) is DepartmentRole.Lead or DepartmentRole.Member;
}
