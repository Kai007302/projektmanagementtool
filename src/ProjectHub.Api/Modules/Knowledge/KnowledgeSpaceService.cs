using Microsoft.EntityFrameworkCore;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Infrastructure.Outcomes;
using ProjectHub.Api.Modules.Audit;
using ProjectHub.Api.Modules.Identity;

namespace ProjectHub.Api.Modules.Knowledge;

public sealed record SpaceResponse(Guid Id, string Name, string? Description, int ArticleCount, long Version);

public sealed record CreateSpaceRequest(string? Name, string? Description);

/// <summary>
/// Knowledge spaces structure the organization's knowledge. Everyone sees them (with the number of
/// articles visible to them); organization admins create and change them.
/// </summary>
public sealed class KnowledgeSpaceService(ProjectHubDbContext db, KnowledgeAccess access, IAuditLog audit, TimeProvider clock)
{
    public const int MaxNameLength = 200;
    public const int MaxDescriptionLength = 2000;

    public async Task<IReadOnlyList<SpaceResponse>> ListAsync(UserContext user, CancellationToken ct)
    {
        var visible = access.Visible(await access.ReaderAsync(user, ct));
        return await db.Set<KnowledgeSpace>().AsNoTracking()
            .Where(s => s.OrganizationId == user.OrganizationId)
            .OrderBy(s => s.Name)
            .Select(s => new SpaceResponse(s.Id, s.Name, s.Description, visible.Count(a => a.KnowledgeSpaceId == s.Id), s.Version))
            .ToListAsync(ct);
    }

    public async Task<ServiceResult<SpaceResponse>> CreateAsync(UserContext user, CreateSpaceRequest request, CancellationToken ct)
    {
        if (!user.IsOrganizationAdmin)
        {
            return ServiceFailure.Forbidden("Only organization admins create knowledge spaces.");
        }

        var now = clock.GetUtcNow();
        var space = new KnowledgeSpace
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = user.OrganizationId,
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
        return new SpaceResponse(space.Id, space.Name, space.Description, 0, space.Version);
    }

    public async Task<ServiceResult<SpaceResponse>> UpdateAsync(UserContext user, Guid spaceId, PatchDocument patch, CancellationToken ct)
    {
        var space = await db.Set<KnowledgeSpace>().AsTracking().SingleOrDefaultAsync(s => s.Id == spaceId && s.OrganizationId == user.OrganizationId, ct);
        if (space is null)
        {
            return ServiceFailure.NotFound("Space");
        }

        if (!user.IsOrganizationAdmin)
        {
            return ServiceFailure.Forbidden("Only organization admins change knowledge spaces.");
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

        return (await ListAsync(user, ct)).Single(s => s.Id == spaceId);
    }

    private static ServiceFailure? Validate(KnowledgeSpace space) =>
        space.Name.Length is 0 or > MaxNameLength
            ? ServiceFailure.Invalid("name", $"Required, at most {MaxNameLength} characters.")
            : space.Description?.Length > MaxDescriptionLength
                ? ServiceFailure.Invalid("description", $"At most {MaxDescriptionLength} characters.")
                : null;
}
