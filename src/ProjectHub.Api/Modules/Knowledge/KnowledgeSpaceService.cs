using Microsoft.EntityFrameworkCore;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Infrastructure.Outcomes;
using ProjectHub.Api.Modules.Audit;
using ProjectHub.Api.Modules.Identity;
using ProjectHub.Api.Modules.Identity.Authorization;

namespace ProjectHub.Api.Modules.Knowledge;

public sealed record SpaceResponse(Guid Id, string Name, string? Description, int ArticleCount, long Version, Guid? DepartmentId = null);

/// <summary>Without <c>DepartmentId</c> the space goes to the first department the caller leads (ADR 0021).</summary>
public sealed record CreateSpaceRequest(string? Name, string? Description, Guid? DepartmentId = null);

/// <summary>
/// Knowledge spaces structure a department's knowledge (ADR 0021). The department's leads and members see them (with
/// the number of articles visible to them), organization admins see all; admins and the department's leads create
/// and change them.
/// </summary>
public sealed class KnowledgeSpaceService(
    ProjectHubDbContext db, KnowledgeAccess access, IProjectHubAuthorization authorization, IAuditLog audit, TimeProvider clock)
{
    public const int MaxNameLength = 200;
    public const int MaxDescriptionLength = 2000;

    public async Task<IReadOnlyList<SpaceResponse>> ListAsync(UserContext user, CancellationToken ct, Guid? departmentId = null)
    {
        var reader = await access.ReaderAsync(user, ct);
        var visible = access.Visible(reader);
        var spaces = access.Spaces(reader);
        if (departmentId is { } department)
        {
            spaces = spaces.Where(s => s.DepartmentId == department);
        }

        return await spaces
            .OrderBy(s => s.Name)
            .Select(s => new SpaceResponse(s.Id, s.Name, s.Description, visible.Count(a => a.KnowledgeSpaceId == s.Id), s.Version, s.DepartmentId))
            .ToListAsync(ct);
    }

    public async Task<ServiceResult<SpaceResponse>> CreateAsync(UserContext user, CreateSpaceRequest request, CancellationToken ct)
    {
        var departmentId = request.DepartmentId
                           ?? await db.Set<Departments.DepartmentMember>()
                               .Where(m => m.OrganizationId == user.OrganizationId && m.UserId == user.UserId && m.Role == DepartmentRole.Lead)
                               .OrderBy(m => m.CreatedAt).ThenBy(m => m.DepartmentId)
                               .Select(m => (Guid?)m.DepartmentId)
                               .FirstOrDefaultAsync(ct);
        if (departmentId is null)
        {
            return user.IsOrganizationAdmin
                ? ServiceFailure.Invalid("departmentId", "Required.")
                : ServiceFailure.Forbidden("Only organization admins and department leads create knowledge spaces.");
        }

        if (!await authorization.CanManageDepartmentAsync(user, departmentId.Value, ct))
        {
            return await db.Set<Departments.Department>().AnyAsync(d => d.Id == departmentId && d.OrganizationId == user.OrganizationId, ct)
                ? ServiceFailure.Forbidden("Only organization admins and the department's leads create knowledge spaces in it.")
                : ServiceFailure.Invalid("departmentId", "Must be a department of the organization.");
        }

        var now = clock.GetUtcNow();
        var space = new KnowledgeSpace
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = user.OrganizationId,
            DepartmentId = departmentId.Value,
            Name = request.Name?.Trim() ?? string.Empty,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            OwnerId = user.UserId,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
        if (Validate(space) is { } invalid)
        {
            return invalid;
        }

        db.Set<KnowledgeSpace>().Add(space);
        audit.Record(user, AuditActions.KnowledgeSpaceCreated, "knowledge_space", space.Id, new { space.Name });
        await db.SaveChangesAsync(ct);
        return new SpaceResponse(space.Id, space.Name, space.Description, 0, space.Version, space.DepartmentId);
    }

    public async Task<ServiceResult<SpaceResponse>> UpdateAsync(UserContext user, Guid spaceId, PatchDocument patch, CancellationToken ct)
    {
        var space = await db.Set<KnowledgeSpace>().AsTracking().SingleOrDefaultAsync(s => s.Id == spaceId && s.OrganizationId == user.OrganizationId, ct);
        if (space is null)
        {
            return ServiceFailure.NotFound("Space");
        }

        if (!await authorization.CanManageDepartmentAsync(user, space.DepartmentId, ct))
        {
            return ServiceFailure.Forbidden("Only organization admins and the department's leads change knowledge spaces.");
        }

        if (!patch.TryGetVersion(out var expectedVersion, out var versionError))
        {
            return versionError!;
        }

        if (space.Version != expectedVersion)
        {
            return ServiceFailure.StaleVersion(space.Version);
        }

        var changed = new List<string>();
        if (!patch.TryApply<string>("name", v => space.Name = v?.Trim() ?? string.Empty, changed, out var error)
            || !patch.TryApply<string>("description", v => space.Description = string.IsNullOrWhiteSpace(v) ? null : v.Trim(), changed, out error))
        {
            return error!;
        }

        if (Validate(space) is { } invalid)
        {
            return invalid;
        }

        if (changed.Count > 0)
        {
            db.Touch(space, expectedVersion, clock.GetUtcNow());
            if (await db.SaveVersionedAsync(space, ct) is { } conflict)
            {
                return conflict;
            }
        }

        return (await ListAsync(user, ct)).SingleOrDefault(s => s.Id == spaceId)
               ?? new SpaceResponse(space.Id, space.Name, space.Description, 0, space.Version, space.DepartmentId);
    }

    private static ServiceFailure? Validate(KnowledgeSpace space) =>
        space.Name.Length is 0 or > MaxNameLength
            ? ServiceFailure.Invalid("name", $"Required, at most {MaxNameLength} characters.")
            : space.Description?.Length > MaxDescriptionLength
                ? ServiceFailure.Invalid("description", $"At most {MaxDescriptionLength} characters.")
                : null;
}
