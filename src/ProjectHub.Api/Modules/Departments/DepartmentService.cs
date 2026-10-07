using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Infrastructure.Outcomes;
using ProjectHub.Api.Modules.Audit;
using ProjectHub.Api.Modules.Identity;
using ProjectHub.Api.Modules.Identity.Authorization;
using ProjectHub.Api.Modules.Knowledge;
using ProjectHub.Api.Modules.Projects;
using ProjectHub.Api.Modules.Users;

namespace ProjectHub.Api.Modules.Departments;

/// <summary><c>EntraGroupId</c> is only shown to those who manage the department.</summary>
public sealed record DepartmentSummary(
    Guid Id, string Name, string? Description, int MemberCount, string? MyRole, bool CanManage, string? EntraGroupId, long Version);

public sealed record DepartmentMemberResponse(Guid UserId, string DisplayName, string Email, string Role, string Source);

/// <summary>Members are listed for the department's own people and organization admins only.</summary>
public sealed record DepartmentDetails(
    Guid Id, string Name, string? Description, string? MyRole, bool CanManage, string? EntraGroupId, long Version,
    IReadOnlyList<DepartmentMemberResponse> Members);

public sealed record CreateDepartmentRequest(string? Name, string? Description, string? EntraGroupId = null);

public sealed record AddDepartmentMemberRequest(Guid? UserId, string? Role);

public sealed record ChangeDepartmentMemberRequest(string? Role);

/// <summary>A department the caller belongs to, for the department switcher (ADR 0021).</summary>
public sealed record MyDepartment(Guid Id, string Name, string Role);

/// <summary>
/// Departments and their members (ADR 0021). Everyone sees the names (to share knowledge with a department);
/// organization admins create, rename and delete departments, admins and the department's leads manage members.
/// </summary>
public sealed class DepartmentService(
    ProjectHubDbContext db,
    IProjectHubAuthorization authorization,
    IAuditLog audit,
    TimeProvider clock)
{
    public const int MaxNameLength = 200;
    public const int MaxDescriptionLength = 2000;
    public const int MaxEntraGroupIdLength = 64;

    public async Task<IReadOnlyList<DepartmentSummary>> ListAsync(UserContext user, Paging paging, CancellationToken ct)
    {
        var rows = await db.Set<Department>().AsNoTracking()
            .Where(d => d.OrganizationId == user.OrganizationId)
            .OrderBy(d => d.Name).ThenBy(d => d.Id)
            .Skip(paging.Skip).Take(paging.Take + 1)
            .Select(d => new
            {
                Department = d,
                Members = db.Set<DepartmentMember>().Count(m => m.DepartmentId == d.Id),
                MyRole = db.Set<DepartmentMember>().Where(m => m.DepartmentId == d.Id && m.UserId == user.UserId).Select(m => m.Role).FirstOrDefault(),
            })
            .ToListAsync(ct);

        return rows.Select(r =>
            {
                var canManage = user.IsOrganizationAdmin || r.MyRole == DepartmentRole.Lead;
                return new DepartmentSummary(
                    r.Department.Id, r.Department.Name, r.Department.Description, r.Members, r.MyRole, canManage,
                    canManage ? r.Department.EntraGroupId : null, r.Department.Version);
            })
            .ToList();
    }

    /// <summary>The caller's own departments, the ones they can work in first (lead, member), then guest.</summary>
    public Task<List<MyDepartment>> MineAsync(UserContext user, CancellationToken ct) =>
        (
            from member in db.Set<DepartmentMember>().AsNoTracking()
            join department in db.Set<Department>().AsNoTracking() on member.DepartmentId equals department.Id
            where member.OrganizationId == user.OrganizationId && member.UserId == user.UserId
            orderby member.Role == DepartmentRole.Guest, department.Name
            select new MyDepartment(department.Id, department.Name, member.Role))
        .ToListAsync(ct);

    public async Task<ServiceResult<DepartmentDetails>> GetAsync(UserContext user, Guid departmentId, CancellationToken ct)
    {
        var department = await db.Set<Department>().AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == departmentId && d.OrganizationId == user.OrganizationId, ct);
        if (department is null)
        {
            return ServiceFailure.NotFound("Department");
        }

        var myRole = await authorization.DepartmentRoleAsync(user, departmentId, ct);
        var canManage = user.IsOrganizationAdmin || myRole == DepartmentRole.Lead;
        var members = user.IsOrganizationAdmin || myRole is not null
            ? await (
                    from member in db.Set<DepartmentMember>().AsNoTracking()
                    join appUser in db.Set<AppUser>().AsNoTracking() on member.UserId equals appUser.Id
                    where member.DepartmentId == departmentId && member.OrganizationId == user.OrganizationId
                    orderby appUser.DisplayName
                    select new DepartmentMemberResponse(appUser.Id, appUser.DisplayName, appUser.Email, member.Role, member.Source))
                .ToListAsync(ct)
            : [];

        return new DepartmentDetails(
            department.Id, department.Name, department.Description, myRole, canManage,
            canManage ? department.EntraGroupId : null, department.Version, members);
    }

    public async Task<ServiceResult<DepartmentSummary>> CreateAsync(UserContext user, CreateDepartmentRequest request, CancellationToken ct)
    {
        if (!authorization.CanCreateDepartment(user))
        {
            return ServiceFailure.Forbidden("Only organization admins create departments.");
        }

        var now = clock.GetUtcNow();
        var department = new Department
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = user.OrganizationId,
            Name = request.Name?.Trim() ?? string.Empty,
            Description = Normalize(request.Description),
            EntraGroupId = Normalize(request.EntraGroupId),
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
        if (Validate(department) is { } invalid)
        {
            return invalid;
        }

        db.Set<Department>().Add(department);
        audit.Record(user, AuditActions.DepartmentCreated, "department", department.Id, new { department.Name, department.EntraGroupId });
        if (!await TrySaveAsync(ct))
        {
            return ServiceFailure.Conflict("A department with this name or Entra group already exists.");
        }

        if (department.EntraGroupId is not null)
        {
            await ResyncEntraGroupsAsync(user.OrganizationId, ct);
        }

        return new DepartmentSummary(department.Id, department.Name, department.Description, 0, null, true, department.EntraGroupId, department.Version);
    }

    /// <summary>Name and description: admins and leads. The Entra group: organization admins only.</summary>
    public async Task<ServiceResult<DepartmentSummary>> UpdateAsync(UserContext user, Guid departmentId, PatchDocument patch, CancellationToken ct)
    {
        var department = await db.Set<Department>().AsTracking()
            .SingleOrDefaultAsync(d => d.Id == departmentId && d.OrganizationId == user.OrganizationId, ct);
        if (department is null)
        {
            return ServiceFailure.NotFound("Department");
        }

        if (!await authorization.CanManageDepartmentAsync(user, departmentId, ct))
        {
            return ServiceFailure.Forbidden("Only organization admins and the department's leads change a department.");
        }

        if (!patch.TryGetVersion(out var expectedVersion, out var versionError))
        {
            return versionError!;
        }

        if (department.Version != expectedVersion)
        {
            return ServiceFailure.StaleVersion(department.Version);
        }

        var changed = new List<string>();
        if (!patch.TryApply<string>("name", v => department.Name = v?.Trim() ?? string.Empty, changed, out var error)
            || !patch.TryApply<string>("description", v => department.Description = Normalize(v), changed, out error)
            || !patch.TryApply<string>("entraGroupId", v => department.EntraGroupId = Normalize(v), changed, out error))
        {
            return error!;
        }

        if (changed.Contains("entraGroupId") && !user.IsOrganizationAdmin)
        {
            return ServiceFailure.Forbidden("Only organization admins connect a department to an Entra group.");
        }

        if (Validate(department) is { } invalid)
        {
            return invalid;
        }

        if (changed.Count > 0)
        {
            db.Touch(department, expectedVersion, clock.GetUtcNow());
            audit.Record(user, AuditActions.DepartmentUpdated, "department", department.Id, new { Fields = changed, department.EntraGroupId });
            try
            {
                if (await db.SaveVersionedAsync(department, ct) is { } conflict)
                {
                    return conflict;
                }
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                db.ChangeTracker.Clear();
                return ServiceFailure.Conflict("A department with this name or Entra group already exists.");
            }

            if (changed.Contains("entraGroupId"))
            {
                await ResyncEntraGroupsAsync(user.OrganizationId, ct);
            }
        }

        return (await ListAsync(user, new Paging(0, Paging.MaxPageSize), ct)).FirstOrDefault(d => d.Id == departmentId)
               ?? new DepartmentSummary(department.Id, department.Name, department.Description, 0, null, true, department.EntraGroupId, department.Version);
    }

    /// <summary>Only empty departments can be deleted: projects, spaces and articles never lose their department.</summary>
    public async Task<ServiceResult<Done>> DeleteAsync(UserContext user, Guid departmentId, CancellationToken ct)
    {
        var department = await db.Set<Department>().AsTracking()
            .SingleOrDefaultAsync(d => d.Id == departmentId && d.OrganizationId == user.OrganizationId, ct);
        if (department is null)
        {
            return ServiceFailure.NotFound("Department");
        }

        if (!user.IsOrganizationAdmin)
        {
            return ServiceFailure.Forbidden("Only organization admins delete departments.");
        }

        var inUse = await db.Set<Project>().AnyAsync(p => p.DepartmentId == departmentId, ct)
                    || await db.Set<KnowledgeSpace>().AnyAsync(s => s.DepartmentId == departmentId, ct)
                    || await db.Set<KnowledgeArticle>().AnyAsync(a => a.DepartmentId == departmentId, ct);
        if (inUse)
        {
            return ServiceFailure.Conflict("The department still has projects or knowledge. Move them to another department first.");
        }

        await db.Set<KnowledgePermission>()
            .Where(p => p.OrganizationId == user.OrganizationId && p.PrincipalType == KnowledgePrincipal.Department && p.PrincipalId == departmentId)
            .ExecuteDeleteAsync(ct);
        await db.Set<KnowledgeReference>()
            .Where(r => r.OrganizationId == user.OrganizationId && r.ResourceType == KnowledgeResourceType.Department && r.ResourceId == departmentId)
            .ExecuteDeleteAsync(ct);
        db.Set<Department>().Remove(department);
        audit.Record(user, AuditActions.DepartmentDeleted, "department", department.Id, new { department.Name });
        await db.SaveChangesAsync(ct);
        return Done.Value;
    }

    public async Task<ServiceResult<Done>> AddMemberAsync(UserContext user, Guid departmentId, AddDepartmentMemberRequest request, CancellationToken ct)
    {
        if (await RequireManageAsync(user, departmentId, ct) is { } failure)
        {
            return failure;
        }

        if (request.UserId is not { } memberId)
        {
            return ServiceFailure.Invalid("userId", "Required.");
        }

        var role = request.Role ?? DepartmentRole.Member;
        if (!DepartmentRole.All.Contains(role))
        {
            return ServiceFailure.Invalid("role", $"Must be one of: {string.Join(", ", DepartmentRole.All)}.");
        }

        var userExists = await db.Set<AppUser>().AnyAsync(
            u => u.Id == memberId && u.OrganizationId == user.OrganizationId && u.Status == UserStatus.Active && u.AnonymizedAt == null, ct);
        if (!userExists)
        {
            return ServiceFailure.Invalid("userId", "User not found.");
        }

        db.Set<DepartmentMember>().Add(new DepartmentMember
        {
            OrganizationId = user.OrganizationId,
            DepartmentId = departmentId,
            UserId = memberId,
            Role = role,
            Source = DepartmentMemberSource.Manual,
            CreatedAt = clock.GetUtcNow(),
        });
        audit.Record(user, AuditActions.DepartmentMemberAdded, "department", departmentId, new { UserId = memberId, Role = role });

        return await TrySaveAsync(ct) ? Done.Value : ServiceFailure.Conflict("The user is already a member of this department.");
    }

    public async Task<ServiceResult<Done>> ChangeMemberRoleAsync(UserContext user, Guid departmentId, Guid memberId, string? role, CancellationToken ct)
    {
        if (await RequireManageAsync(user, departmentId, ct) is { } failure)
        {
            return failure;
        }

        if (role is null || !DepartmentRole.All.Contains(role))
        {
            return ServiceFailure.Invalid("role", $"Must be one of: {string.Join(", ", DepartmentRole.All)}.");
        }

        var member = await FindMemberAsync(user, departmentId, memberId, ct);
        if (member is null)
        {
            return ServiceFailure.NotFound("Department member");
        }

        var previous = member.Role;
        member.Role = role;
        audit.Record(user, AuditActions.DepartmentMemberRoleChanged, "department", departmentId, new { UserId = memberId, From = previous, To = role });
        await db.SaveChangesAsync(ct);
        return Done.Value;
    }

    /// <summary>
    /// Removing someone who joined through an Entra group only lasts until their next sign-in while they are still
    /// in the group; the department's Entra group decides.
    /// </summary>
    public async Task<ServiceResult<Done>> RemoveMemberAsync(UserContext user, Guid departmentId, Guid memberId, CancellationToken ct)
    {
        if (await RequireManageAsync(user, departmentId, ct) is { } failure)
        {
            return failure;
        }

        var member = await FindMemberAsync(user, departmentId, memberId, ct);
        if (member is null)
        {
            return ServiceFailure.NotFound("Department member");
        }

        db.Set<DepartmentMember>().Remove(member);
        audit.Record(user, AuditActions.DepartmentMemberRemoved, "department", departmentId, new { UserId = memberId });
        await db.SaveChangesAsync(ct);
        return Done.Value;
    }

    /// <summary>Active people without any department, waiting to be taken in. Organization admins and leads.</summary>
    public async Task<ServiceResult<List<UserResponse>>> UnassignedAsync(UserContext user, CancellationToken ct)
    {
        var isLead = await db.Set<DepartmentMember>()
            .AnyAsync(m => m.OrganizationId == user.OrganizationId && m.UserId == user.UserId && m.Role == DepartmentRole.Lead, ct);
        if (!user.IsOrganizationAdmin && !isLead)
        {
            return ServiceFailure.Forbidden("Only organization admins and department leads see people without a department.");
        }

        return await db.Set<AppUser>().AsNoTracking()
            .Where(u => u.OrganizationId == user.OrganizationId && u.Status == UserStatus.Active && u.AnonymizedAt == null
                        && !db.Set<DepartmentMember>().Any(m => m.UserId == u.Id))
            .OrderBy(u => u.DisplayName).ThenBy(u => u.Id)
            .Take(Paging.MaxPageSize)
            .Select(u => new UserResponse(u.Id, u.DisplayName, u.Email, u.Department, u.Status))
            .ToListAsync(ct);
    }

    /// <summary>Departments of other organizations are "not found", never "forbidden".</summary>
    private async Task<ServiceFailure?> RequireManageAsync(UserContext user, Guid departmentId, CancellationToken ct)
    {
        if (!await db.Set<Department>().AnyAsync(d => d.Id == departmentId && d.OrganizationId == user.OrganizationId, ct))
        {
            return ServiceFailure.NotFound("Department");
        }

        return await authorization.CanManageDepartmentAsync(user, departmentId, ct)
            ? null
            : ServiceFailure.Forbidden("Only organization admins and the department's leads manage members.");
    }

    private Task<DepartmentMember?> FindMemberAsync(UserContext user, Guid departmentId, Guid memberId, CancellationToken ct) =>
        db.Set<DepartmentMember>().AsTracking().SingleOrDefaultAsync(
            m => m.DepartmentId == departmentId && m.UserId == memberId && m.OrganizationId == user.OrganizationId, ct);

    /// <summary>A department's Entra group changed: everyone syncs again at their next request.</summary>
    private Task ResyncEntraGroupsAsync(Guid organizationId, CancellationToken ct) =>
        db.Set<AppUser>().Where(u => u.OrganizationId == organizationId && u.EntraGroupsHash != null)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.EntraGroupsHash, (string?)null), ct);

    private async Task<bool> TrySaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }

    private static ServiceFailure? Validate(Department department) =>
        department.Name.Length is 0 or > MaxNameLength
            ? ServiceFailure.Invalid("name", $"Required, at most {MaxNameLength} characters.")
            : department.Description?.Length > MaxDescriptionLength
                ? ServiceFailure.Invalid("description", $"At most {MaxDescriptionLength} characters.")
                : department.EntraGroupId?.Length > MaxEntraGroupIdLength
                    ? ServiceFailure.Invalid("entraGroupId", $"At most {MaxEntraGroupIdLength} characters.")
                    : null;

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
