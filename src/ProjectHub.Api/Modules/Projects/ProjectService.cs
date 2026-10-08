using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Infrastructure.Events;
using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Infrastructure.Outcomes;
using ProjectHub.Api.Modules.Audit;
using ProjectHub.Api.Modules.Departments;
using ProjectHub.Api.Modules.Identity;
using ProjectHub.Api.Modules.Identity.Authorization;
using ProjectHub.Api.Modules.Users;

namespace ProjectHub.Api.Modules.Projects;

public sealed record ProjectSummary(
    Guid Id,
    string Name,
    string? Description,
    string Status,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? MyRole,
    long Version,
    string? Icon = null,
    long? LogoVersion = null,
    Guid? DepartmentId = null,
    string? Visibility = null);

public sealed record ProjectMemberResponse(Guid UserId, string DisplayName, string Email, string Role);

public sealed record ProjectDetails(
    Guid Id,
    string Name,
    string? Description,
    string Status,
    DateOnly? StartDate,
    DateOnly? EndDate,
    Guid OwnerId,
    long Version,
    ProjectCapabilities Capabilities,
    IReadOnlyList<ProjectMemberResponse> Members,
    string? Icon = null,
    long? LogoVersion = null,
    Guid? DepartmentId = null,
    string? DepartmentName = null,
    string? Visibility = null);

/// <summary>
/// Without <c>DepartmentId</c> the project goes to the caller's first department they can work in (ADR 0021);
/// without <c>Visibility</c> it is visible to its department.
/// </summary>
public sealed record CreateProjectRequest(
    string? Name, string? Description, string? Status, DateOnly? StartDate, DateOnly? EndDate,
    Guid? DepartmentId = null, string? Visibility = null);

public sealed class ProjectService(
    ProjectHubDbContext db,
    IProjectHubAuthorization authorization,
    ProjectAccess access,
    IAuditLog audit,
    IActivityLog activity,
    IDomainEventPublisher events,
    TimeProvider clock)
{
    public const int MaxNameLength = 200;
    public const int MaxDescriptionLength = 10_000;
    public const int MaxIconLength = 16;

    /// <summary>The projects the caller may see, optionally only those of one department.</summary>
    public async Task<IReadOnlyList<ProjectSummary>> ListAsync(UserContext user, Paging paging, CancellationToken ct, Guid? departmentId = null)
    {
        var projects = authorization.VisibleProjects(user).AsNoTracking();
        if (departmentId is { } department)
        {
            projects = projects.Where(p => p.DepartmentId == department);
        }

        var rows =
            from project in projects
            join member in db.Set<ProjectMember>().Where(m => m.UserId == user.UserId)
                on project.Id equals member.ProjectId into memberships
            from membership in memberships.DefaultIfEmpty()
            orderby project.Name, project.Id
            select new ProjectSummary(
                project.Id, project.Name, project.Description, project.Status, project.StartDate, project.EndDate,
                membership == null ? null : membership.Role, project.Version, project.Icon, project.LogoVersion,
                project.DepartmentId, project.Visibility);

        return await rows.Skip(paging.Skip).Take(paging.Take + 1).ToListAsync(ct);
    }

    public async Task<ServiceResult<ProjectDetails>> GetAsync(UserContext user, Guid projectId, CancellationToken ct)
    {
        if (await access.RequireAsync(user, projectId, ProjectPermission.View, ct) is { } failure)
        {
            return failure;
        }

        var project = await db.Set<Project>().AsNoTracking().SingleAsync(p => p.Id == projectId, ct);
        if (await authorization.SeesProjectOnlyAsOrganizationAdminAsync(user, projectId, ct))
        {
            audit.Record(user, AuditActions.OrganizationAdminAccess, "project", projectId);
            await db.SaveChangesAsync(ct);
        }

        var departmentName = await db.Set<Department>().Where(d => d.Id == project.DepartmentId).Select(d => d.Name).SingleOrDefaultAsync(ct);
        var members = await (
                from member in db.Set<ProjectMember>().AsNoTracking()
                join appUser in db.Set<AppUser>().AsNoTracking() on member.UserId equals appUser.Id
                where member.ProjectId == projectId && member.OrganizationId == user.OrganizationId
                orderby appUser.DisplayName
                select new ProjectMemberResponse(appUser.Id, appUser.DisplayName, appUser.Email, member.Role))
            .ToListAsync(ct);

        var capabilities = await access.CapabilitiesAsync(user, projectId, ct);
        capabilities = capabilities with
        {
            CanShareWithOrganization = capabilities.CanManage && await CanShareWithOrganizationAsync(user, project.DepartmentId, ct),
        };
        return new ProjectDetails(
            project.Id, project.Name, project.Description, project.Status, project.StartDate, project.EndDate, project.OwnerId,
            project.Version, capabilities, members, project.Icon, project.LogoVersion,
            project.DepartmentId, departmentName, project.Visibility);
    }

    public async Task<ServiceResult<Project>> CreateAsync(UserContext user, CreateProjectRequest request, CancellationToken ct)
    {
        var departmentId = request.DepartmentId ?? await DefaultDepartmentAsync(user, ct);
        if (departmentId is null)
        {
            return ServiceFailure.Invalid("departmentId", "Required: you belong to no department you can create projects in.");
        }

        if (!await authorization.CanCreateInDepartmentAsync(user, departmentId.Value, ct))
        {
            return request.DepartmentId is not null && !await db.Set<Department>().AnyAsync(d => d.Id == departmentId && d.OrganizationId == user.OrganizationId, ct)
                ? ServiceFailure.Invalid("departmentId", "Must be a department of the organization.")
                : ServiceFailure.Forbidden("Only leads and members of the department create projects in it.");
        }

        if (request.Visibility == ProjectVisibility.Organization && !await CanShareWithOrganizationAsync(user, departmentId.Value, ct))
        {
            return ServiceFailure.Forbidden("Only organization admins and the department's leads share projects with the whole organization.");
        }

        var now = clock.GetUtcNow();
        var project = new Project
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = user.OrganizationId,
            Name = request.Name?.Trim() ?? string.Empty,
            Description = Normalize(request.Description),
            Status = request.Status ?? ProjectStatus.Active,
            OwnerId = user.UserId,
            DepartmentId = departmentId.Value,
            Visibility = request.Visibility ?? ProjectVisibility.Department,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };

        if (Validate(project) is { } invalid)
        {
            return invalid;
        }

        db.Set<Project>().Add(project);
        db.Set<ProjectMember>().Add(new ProjectMember
        {
            OrganizationId = user.OrganizationId,
            ProjectId = project.Id,
            UserId = user.UserId,
            Role = ProjectRole.Admin,
            CreatedAt = now,
        });
        activity.Record(user, project.Id, ActivityActions.ProjectCreated, "project", project.Id, new { project.Name });
        audit.Record(user, AuditActions.ProjectCreated, "project", project.Id, new { project.Name });
        await db.SaveChangesAsync(ct);
        return project;
    }

    public async Task<ServiceResult<ProjectSummary>> UpdateAsync(UserContext user, Guid projectId, PatchDocument patch, CancellationToken ct)
    {
        if (await access.RequireAsync(user, projectId, ProjectPermission.Edit, ct) is { } failure)
        {
            return failure;
        }

        if (!patch.TryGetVersion(out var expectedVersion, out var versionError))
        {
            return versionError!;
        }

        var project = await db.Set<Project>().SingleAsync(p => p.Id == projectId, ct);
        if (project.Version != expectedVersion)
        {
            return ServiceFailure.StaleVersion(project.Version);
        }

        var changed = new List<string>();
        if (!patch.TryApply<string>("name", value => project.Name = value?.Trim() ?? string.Empty, changed, out var error)
            || !patch.TryApply<string>("description", value => project.Description = Normalize(value), changed, out error)
            || !patch.TryApply<string>("status", value => project.Status = value ?? string.Empty, changed, out error)
            || !patch.TryApply<string>("icon", value => project.Icon = Normalize(value), changed, out error)
            || !patch.TryApply<DateOnly?>("startDate", value => project.StartDate = value, changed, out error)
            || !patch.TryApply<DateOnly?>("endDate", value => project.EndDate = value, changed, out error)
            || !patch.TryApply<string>("visibility", value => project.Visibility = value ?? string.Empty, changed, out error)
            || !patch.TryApply<Guid?>("departmentId", value => project.DepartmentId = value ?? Guid.Empty, changed, out error))
        {
            return error!;
        }

        if ((changed.Contains("visibility") || changed.Contains("departmentId"))
            && !await authorization.HasProjectPermissionAsync(user, projectId, ProjectPermission.Manage, ct))
        {
            return ServiceFailure.Forbidden("Only those who manage the project change its visibility or department.");
        }

        if (changed.Contains("visibility") && project.Visibility == ProjectVisibility.Organization
            && !await CanShareWithOrganizationAsync(user, project.DepartmentId, ct))
        {
            return ServiceFailure.Forbidden("Only organization admins and the department's leads share projects with the whole organization.");
        }

        if (changed.Contains("departmentId") && !await authorization.CanCreateInDepartmentAsync(user, project.DepartmentId, ct))
        {
            return await db.Set<Department>().AnyAsync(d => d.Id == project.DepartmentId && d.OrganizationId == user.OrganizationId, ct)
                ? ServiceFailure.Forbidden("Projects can only be moved to departments you work in.")
                : ServiceFailure.Invalid("departmentId", "Must be a department of the organization.");
        }

        if (Validate(project) is { } invalid)
        {
            return invalid;
        }

        if (changed.Count > 0)
        {
            db.Touch(project, expectedVersion, clock.GetUtcNow());
            activity.Record(user, project.Id, ActivityActions.ProjectUpdated, "project", project.Id, new { Fields = changed });
            if (changed.Contains("visibility"))
            {
                audit.Record(user, AuditActions.ProjectVisibilityChanged, "project", project.Id, new { project.Visibility });
            }

            if (changed.Contains("departmentId"))
            {
                audit.Record(user, AuditActions.ProjectMoved, "project", project.Id, new { project.DepartmentId });
            }

            if (await db.SaveVersionedAsync(project, ct) is { } conflict)
            {
                return conflict;
            }
        }

        return new ProjectSummary(
            project.Id, project.Name, project.Description, project.Status, project.StartDate, project.EndDate, null, project.Version, project.Icon, project.LogoVersion,
            project.DepartmentId, project.Visibility);
    }

    public async Task<ServiceResult<Done>> DeleteAsync(UserContext user, Guid projectId, CancellationToken ct)
    {
        if (await access.RequireAsync(user, projectId, ProjectPermission.Manage, ct) is { } failure)
        {
            return failure;
        }

        var project = await db.Set<Project>().SingleAsync(p => p.Id == projectId, ct);
        project.DeletedAt = clock.GetUtcNow();
        db.Touch(project, project.Version, clock.GetUtcNow());
        activity.Record(user, project.Id, ActivityActions.ProjectDeleted, "project", project.Id);
        audit.Record(user, AuditActions.ProjectDeleted, "project", project.Id, new { project.Name });
        return await db.SaveVersionedAsync(project, ct) is { } conflict ? conflict : Done.Value;
    }

    public async Task<ServiceResult<Done>> AddMemberAsync(UserContext user, Guid projectId, Guid? memberId, string? role, CancellationToken ct)
    {
        if (await access.RequireAsync(user, projectId, ProjectPermission.Manage, ct) is { } failure)
        {
            return failure;
        }

        if (memberId is null)
        {
            return ServiceFailure.Invalid("userId", "Required.");
        }

        if (role is null || !ProjectRole.All.Contains(role))
        {
            return ServiceFailure.Invalid("role", $"Must be one of: {string.Join(", ", ProjectRole.All)}.");
        }

        var userExists = await db.Set<AppUser>()
            .AnyAsync(u => u.Id == memberId && u.OrganizationId == user.OrganizationId && u.Status == UserStatus.Active, ct);
        if (!userExists)
        {
            return ServiceFailure.Invalid("userId", "User not found.");
        }

        db.Set<ProjectMember>().Add(new ProjectMember
        {
            OrganizationId = user.OrganizationId,
            ProjectId = projectId,
            UserId = memberId.Value,
            Role = role,
            CreatedAt = clock.GetUtcNow(),
        });
        activity.Record(user, projectId, ActivityActions.MemberAdded, "project_member", memberId.Value, new { Role = role });
        audit.Record(user, AuditActions.ProjectMemberAdded, "project", projectId, new { UserId = memberId, Role = role });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return ServiceFailure.Conflict("The user is already a member of this project.");
        }

        await events.PublishAsync(new ProjectMemberAdded(user.OrganizationId, projectId, memberId.Value, user.UserId, role), ct);
        return Done.Value;
    }

    public async Task<ServiceResult<Done>> ChangeMemberRoleAsync(UserContext user, Guid projectId, Guid memberId, string? role, CancellationToken ct)
    {
        if (await access.RequireAsync(user, projectId, ProjectPermission.Manage, ct) is { } failure)
        {
            return failure;
        }

        if (role is null || !ProjectRole.All.Contains(role))
        {
            return ServiceFailure.Invalid("role", $"Must be one of: {string.Join(", ", ProjectRole.All)}.");
        }

        var member = await FindMemberAsync(user, projectId, memberId, ct);
        if (member is null)
        {
            return ServiceFailure.NotFound("Project member");
        }

        if (member.Role == ProjectRole.Admin && role != ProjectRole.Admin && await IsLastAdminAsync(projectId, ct))
        {
            return ServiceFailure.Conflict("A project needs at least one admin.");
        }

        var previous = member.Role;
        member.Role = role;
        activity.Record(user, projectId, ActivityActions.MemberRoleChanged, "project_member", memberId, new { From = previous, To = role });
        audit.Record(user, AuditActions.ProjectMemberRoleChanged, "project", projectId, new { UserId = memberId, From = previous, To = role });
        await db.SaveChangesAsync(ct);
        return Done.Value;
    }

    public async Task<ServiceResult<Done>> RemoveMemberAsync(UserContext user, Guid projectId, Guid memberId, CancellationToken ct)
    {
        if (await access.RequireAsync(user, projectId, ProjectPermission.Manage, ct) is { } failure)
        {
            return failure;
        }

        var member = await FindMemberAsync(user, projectId, memberId, ct);
        if (member is null)
        {
            return ServiceFailure.NotFound("Project member");
        }

        if (member.Role == ProjectRole.Admin && await IsLastAdminAsync(projectId, ct))
        {
            return ServiceFailure.Conflict("A project needs at least one admin.");
        }

        db.Set<ProjectMember>().Remove(member);
        activity.Record(user, projectId, ActivityActions.MemberRemoved, "project_member", memberId);
        audit.Record(user, AuditActions.ProjectMemberRemoved, "project", projectId, new { UserId = memberId });
        await db.SaveChangesAsync(ct);
        return Done.Value;
    }

    private Task<ProjectMember?> FindMemberAsync(UserContext user, Guid projectId, Guid memberId, CancellationToken ct) =>
        db.Set<ProjectMember>().SingleOrDefaultAsync(
            m => m.ProjectId == projectId && m.UserId == memberId && m.OrganizationId == user.OrganizationId, ct);

    private async Task<bool> IsLastAdminAsync(Guid projectId, CancellationToken ct) =>
        await db.Set<ProjectMember>().CountAsync(m => m.ProjectId == projectId && m.Role == ProjectRole.Admin, ct) <= 1;

    /// <summary>The caller's first department they can create in: the one they joined first.</summary>
    private async Task<bool> CanShareWithOrganizationAsync(UserContext user, Guid departmentId, CancellationToken ct) =>
        user.IsOrganizationAdmin || await authorization.DepartmentRoleAsync(user, departmentId, ct) == DepartmentRole.Lead;

    private Task<Guid?> DefaultDepartmentAsync(UserContext user, CancellationToken ct) =>
        db.Set<DepartmentMember>()
            .Where(m => m.OrganizationId == user.OrganizationId && m.UserId == user.UserId && DepartmentRole.Working.Contains(m.Role))
            .OrderBy(m => m.CreatedAt).ThenBy(m => m.DepartmentId)
            .Select(m => (Guid?)m.DepartmentId)
            .FirstOrDefaultAsync(ct);

    private static ServiceFailure? Validate(Project project)
    {
        if (project.Name.Length is 0 or > MaxNameLength)
        {
            return ServiceFailure.Invalid("name", $"Required, at most {MaxNameLength} characters.");
        }

        if (project.Description?.Length > MaxDescriptionLength)
        {
            return ServiceFailure.Invalid("description", $"At most {MaxDescriptionLength} characters.");
        }

        // An emoji, perhaps with a skin tone or joined from several (👩‍💻): short, and without letters or digits.
        if (project.Icon is { } icon && (icon.Length > MaxIconLength || icon.Any(char.IsLetterOrDigit) || icon.Any(char.IsWhiteSpace)))
        {
            return ServiceFailure.Invalid("icon", $"One emoji, at most {MaxIconLength} characters.");
        }

        if (!ProjectVisibility.All.Contains(project.Visibility))
        {
            return ServiceFailure.Invalid("visibility", $"Must be one of: {string.Join(", ", ProjectVisibility.All)}.");
        }

        if (!ProjectStatus.All.Contains(project.Status))
        {
            return ServiceFailure.Invalid("status", $"Must be one of: {string.Join(", ", ProjectStatus.All)}.");
        }

        if (project.StartDate is { } start && project.EndDate is { } end && end < start)
        {
            return ServiceFailure.Invalid("endDate", "Must not be before the start date.");
        }

        return null;
    }

    private static string? Normalize(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
