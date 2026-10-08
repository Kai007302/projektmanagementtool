using ProjectHub.Api.Modules.Projects;

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

    /// <summary>
    /// The active projects of the caller's organization the caller may see, as one query (lists, calendar feed).
    /// Same rules as <see cref="HasProjectPermissionAsync"/> with <see cref="ProjectPermission.View"/>.
    /// </summary>
    IQueryable<Project> VisibleProjects(UserContext user);

    /// <summary>
    /// Whether an organization admin sees the project only because of that role: not a member, not in its
    /// department and the project is not shared with them. Such access is audited (ADR 0021).
    /// </summary>
    Task<bool> SeesProjectOnlyAsOrganizationAdminAsync(UserContext user, Guid projectId, CancellationToken ct);

    /// <summary>
    /// Whether a user may be assigned tasks in a project: an active user of the organization
    /// who holds <see cref="ProjectPermission.Contribute"/> on it.
    /// </summary>
    Task<bool> CanBeAssignedAsync(Guid organizationId, Guid projectId, Guid userId, CancellationToken ct);

    /// <summary>The caller's role in a department of their organization, or null.</summary>
    Task<string?> DepartmentRoleAsync(UserContext user, Guid departmentId, CancellationToken ct);

    /// <summary>Organization admins create departments.</summary>
    bool CanCreateDepartment(UserContext user) => user.IsOrganizationAdmin;

    /// <summary>Organization admins and the department's leads. False for departments of other organizations.</summary>
    Task<bool> CanManageDepartmentAsync(UserContext user, Guid departmentId, CancellationToken ct);

    /// <summary>
    /// Who may create projects, knowledge spaces' articles and the like in a department: organization admins, its
    /// leads and members (not guests). False for departments of other organizations.
    /// </summary>
    Task<bool> CanCreateInDepartmentAsync(UserContext user, Guid departmentId, CancellationToken ct);
}
