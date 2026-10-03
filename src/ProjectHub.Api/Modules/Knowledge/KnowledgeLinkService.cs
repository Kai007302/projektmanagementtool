using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Infrastructure.Outcomes;
using ProjectHub.Api.Modules.Identity;
using ProjectHub.Api.Modules.Identity.Authorization;
using ProjectHub.Api.Modules.Projects;
using ProjectHub.Api.Modules.Tasks;
using ProjectHub.Api.Modules.Teams;
using ProjectHub.Api.Modules.Whiteboard;

namespace ProjectHub.Api.Modules.Knowledge;

public sealed record CreateRelationRequest(Guid? TargetArticleId, string? RelationType);

public sealed record CreateReferenceRequest(string? ResourceType, Guid? ResourceId);

/// <summary>
/// Links between articles (relations) and from articles to projects, tasks and teams (references).
/// Both directions are only ever shown for things the reader may see.
/// </summary>
public sealed class KnowledgeLinkService(
    ProjectHubDbContext db,
    KnowledgeAccess access,
    IProjectHubAuthorization authorization,
    TimeProvider clock)
{
    /// <summary>Guards against corrupt data when walking PART_OF chains.</summary>
    private const int MaxHierarchyDepth = 100;

    public async Task<ServiceResult<RelationResponse>> AddRelationAsync(UserContext user, Guid articleId, CreateRelationRequest request, CancellationToken ct)
    {
        var (_, reader, _, failure) = await access.RequireAsync(user, articleId, KnowledgeRight.Edit, ct);
        if (failure is not null)
        {
            return failure;
        }

        if (request.RelationType is not { } type || !KnowledgeRelationType.All.Contains(type))
        {
            return ServiceFailure.Invalid("relationType", $"Must be one of: {string.Join(", ", KnowledgeRelationType.All)}.");
        }

        if (request.TargetArticleId is not { } targetId || targetId == articleId)
        {
            return ServiceFailure.Invalid("targetArticleId", "Must be another article.");
        }

        var target = await access.Visible(reader).Where(a => a.Id == targetId).Select(a => new { a.Title, a.ArticleType }).SingleOrDefaultAsync(ct);
        if (target is null)
        {
            return ServiceFailure.Invalid("targetArticleId", "Must be an article you can see.");
        }

        if (type == KnowledgeRelationType.PartOf && await IsPartOfAsync(targetId, articleId, ct))
        {
            return ServiceFailure.Invalid("targetArticleId", "Would make the article part of itself.");
        }

        var relation = new KnowledgeRelation
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = user.OrganizationId,
            SourceArticleId = articleId,
            TargetArticleId = targetId,
            RelationType = type,
            CreatedBy = user.UserId,
            CreatedAt = clock.GetUtcNow(),
        };
        db.Set<KnowledgeRelation>().Add(relation);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return ServiceFailure.Conflict("The relation already exists.");
        }

        return new RelationResponse(relation.Id, type, "outgoing", targetId, target.Title, target.ArticleType);
    }

    /// <summary>Removing needs Edit on the source article; relations are owned by their source.</summary>
    public async Task<ServiceResult<Done>> RemoveRelationAsync(UserContext user, Guid relationId, CancellationToken ct)
    {
        var relation = await db.Set<KnowledgeRelation>().SingleOrDefaultAsync(r => r.Id == relationId && r.OrganizationId == user.OrganizationId, ct);
        if (relation is null)
        {
            return ServiceFailure.NotFound("Relation");
        }

        var (_, _, _, failure) = await access.RequireAsync(user, relation.SourceArticleId, KnowledgeRight.Edit, ct);
        if (failure is not null)
        {
            return failure.Error == ServiceError.NotFound ? ServiceFailure.NotFound("Relation") : failure;
        }

        db.Set<KnowledgeRelation>().Remove(relation);
        await db.SaveChangesAsync(ct);
        return Done.Value;
    }

    public async Task<ServiceResult<ReferenceResponse>> AddReferenceAsync(UserContext user, Guid articleId, CreateReferenceRequest request, CancellationToken ct)
    {
        var (_, _, _, failure) = await access.RequireAsync(user, articleId, KnowledgeRight.Edit, ct);
        if (failure is not null)
        {
            return failure;
        }

        if (request.ResourceType is not { } type || !KnowledgeResourceType.Supported.Contains(type))
        {
            return ServiceFailure.Invalid("resourceType", $"Must be one of: {string.Join(", ", KnowledgeResourceType.Supported)}.");
        }

        if (request.ResourceId is not { } resourceId)
        {
            return ServiceFailure.Invalid("resourceId", "Required.");
        }

        var resolved = (await ResolveAsync(user, [(type, resourceId)], ct)).GetValueOrDefault((type, resourceId));
        if (resolved is null)
        {
            return ServiceFailure.Invalid("resourceId", $"Must be a {type} you can see.");
        }

        var reference = new KnowledgeReference
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = user.OrganizationId,
            ArticleId = articleId,
            ResourceType = type,
            ResourceId = resourceId,
            CreatedBy = user.UserId,
            CreatedAt = clock.GetUtcNow(),
        };
        db.Set<KnowledgeReference>().Add(reference);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return ServiceFailure.Conflict("The reference already exists.");
        }

        return new ReferenceResponse(reference.Id, type, resourceId, resolved.Title, resolved.ProjectId);
    }

    public async Task<ServiceResult<Done>> RemoveReferenceAsync(UserContext user, Guid referenceId, CancellationToken ct)
    {
        var reference = await db.Set<KnowledgeReference>().SingleOrDefaultAsync(r => r.Id == referenceId && r.OrganizationId == user.OrganizationId, ct);
        if (reference is null)
        {
            return ServiceFailure.NotFound("Reference");
        }

        var (_, _, _, failure) = await access.RequireAsync(user, reference.ArticleId, KnowledgeRight.Edit, ct);
        if (failure is not null)
        {
            return failure.Error == ServiceError.NotFound ? ServiceFailure.NotFound("Reference") : failure;
        }

        db.Set<KnowledgeReference>().Remove(reference);
        await db.SaveChangesAsync(ct);
        return Done.Value;
    }

    /// <summary>
    /// The other direction: visible articles that reference a project, task, team or whiteboard.
    /// The resource itself must be visible to the caller, otherwise it does not exist for them.
    /// </summary>
    public async Task<ServiceResult<IReadOnlyList<ArticleSummary>>> ArticlesReferencingAsync(
        UserContext user, string resourceType, Guid resourceId, CancellationToken ct)
    {
        if ((await ResolveAsync(user, [(resourceType, resourceId)], ct)).GetValueOrDefault((resourceType, resourceId)) is null)
        {
            return ServiceFailure.NotFound(resourceType switch
            {
                KnowledgeResourceType.Project => "Project",
                KnowledgeResourceType.Task => "Task",
                KnowledgeResourceType.Whiteboard => "Whiteboard",
                _ => "Team",
            });
        }

        var reader = await access.ReaderAsync(user, ct);
        var ids = await access.Visible(reader)
            .Where(a => db.Set<KnowledgeReference>().Any(r => r.ArticleId == a.Id && r.ResourceType == resourceType && r.ResourceId == resourceId))
            .OrderBy(a => a.Title)
            .Select(a => a.Id)
            .Take(100)
            .ToListAsync(ct);
        return (ServiceResult<IReadOnlyList<ArticleSummary>>)(await KnowledgeSummaries.LoadAsync(db, ids, ct)).ToList();
    }

    /// <summary>Outgoing and incoming relations whose other article the reader may see.</summary>
    internal async Task<IReadOnlyList<RelationResponse>> RelationsOfAsync(KnowledgeReader reader, Guid articleId, CancellationToken ct)
    {
        var visible = access.Visible(reader);
        var outgoing = await (
                from relation in db.Set<KnowledgeRelation>().AsNoTracking()
                where relation.SourceArticleId == articleId
                join other in visible on relation.TargetArticleId equals other.Id
                select new RelationResponse(relation.Id, relation.RelationType, "outgoing", other.Id, other.Title, other.ArticleType))
            .ToListAsync(ct);
        var incoming = await (
                from relation in db.Set<KnowledgeRelation>().AsNoTracking()
                where relation.TargetArticleId == articleId
                join other in visible on relation.SourceArticleId equals other.Id
                select new RelationResponse(relation.Id, relation.RelationType, "incoming", other.Id, other.Title, other.ArticleType))
            .ToListAsync(ct);
        return outgoing.Concat(incoming).OrderBy(r => r.Title).ToList();
    }

    /// <summary>References whose resource the caller may see; the others stay hidden.</summary>
    internal async Task<IReadOnlyList<ReferenceResponse>> ReferencesOfAsync(UserContext user, Guid articleId, CancellationToken ct)
    {
        var references = await db.Set<KnowledgeReference>().AsNoTracking().Where(r => r.ArticleId == articleId).ToListAsync(ct);
        var resolved = await ResolveAsync(user, references.Select(r => (r.ResourceType, r.ResourceId)).ToList(), ct);
        return references
            .Where(r => resolved.GetValueOrDefault((r.ResourceType, r.ResourceId)) is not null)
            .Select(r =>
            {
                var target = resolved[(r.ResourceType, r.ResourceId)]!;
                return new ReferenceResponse(r.Id, r.ResourceType, r.ResourceId, target.Title, target.ProjectId);
            })
            .OrderBy(r => r.ResourceType).ThenBy(r => r.Title)
            .ToList();
    }

    /// <summary>Reference blocks in content may only point to things the author can see.</summary>
    internal async Task<ServiceFailure?> ValidateBlockReferencesAsync(
        UserContext user, KnowledgeReader reader, BlockContent.BlockReferences references, CancellationToken ct)
    {
        var resources = references.Tasks.Select(id => (KnowledgeResourceType.Task, id))
            .Concat(references.Projects.Select(id => (KnowledgeResourceType.Project, id)))
            .ToList();
        var resolved = await ResolveAsync(user, resources, ct);
        if (resources.Any(r => resolved.GetValueOrDefault(r) is null))
        {
            return ServiceFailure.Invalid("content", "Task and project references must point to items you can see.");
        }

        var articleIds = references.Articles.ToList();
        var visible = await access.Visible(reader).CountAsync(a => articleIds.Contains(a.Id), ct);
        return visible == articleIds.Count
            ? null
            : ServiceFailure.Invalid("content", "Knowledge references must point to articles you can see.");
    }

    private sealed record Resolved(string Title, Guid? ProjectId);

    /// <summary>Titles of the resources the caller may see; invisible or unknown ones map to null.</summary>
    private async Task<Dictionary<(string Type, Guid Id), Resolved?>> ResolveAsync(
        UserContext user, IReadOnlyList<(string Type, Guid Id)> resources, CancellationToken ct)
    {
        var result = resources.Distinct().ToDictionary(r => r, _ => (Resolved?)null);

        var projectIds = resources.Where(r => r.Type == KnowledgeResourceType.Project).Select(r => r.Id).ToList();
        if (projectIds.Count > 0)
        {
            var projects = await db.Set<Project>()
                .Where(p => projectIds.Contains(p.Id) && p.OrganizationId == user.OrganizationId && p.DeletedAt == null)
                .Select(p => new { p.Id, p.Name })
                .ToListAsync(ct);
            foreach (var project in projects)
            {
                if (await authorization.CanViewProjectAsync(user, project.Id, ct))
                {
                    result[(KnowledgeResourceType.Project, project.Id)] = new Resolved(project.Name, project.Id);
                }
            }
        }

        var taskIds = resources.Where(r => r.Type == KnowledgeResourceType.Task).Select(r => r.Id).ToList();
        if (taskIds.Count > 0)
        {
            var tasks = await db.Set<ProjectTask>()
                .Where(t => taskIds.Contains(t.Id) && t.OrganizationId == user.OrganizationId && t.DeletedAt == null)
                .Select(t => new { t.Id, t.Title, t.ProjectId })
                .ToListAsync(ct);
            foreach (var task in tasks)
            {
                if (await authorization.CanViewProjectAsync(user, task.ProjectId, ct))
                {
                    result[(KnowledgeResourceType.Task, task.Id)] = new Resolved(task.Title, task.ProjectId);
                }
            }
        }

        var whiteboardIds = resources.Where(r => r.Type == KnowledgeResourceType.Whiteboard).Select(r => r.Id).ToList();
        if (whiteboardIds.Count > 0)
        {
            var boards = await db.Set<ProjectWhiteboard>()
                .Where(w => whiteboardIds.Contains(w.Id) && w.OrganizationId == user.OrganizationId)
                .Select(w => new { w.Id, w.Name, w.ProjectId })
                .ToListAsync(ct);
            foreach (var board in boards)
            {
                if (await authorization.CanViewProjectAsync(user, board.ProjectId, ct))
                {
                    result[(KnowledgeResourceType.Whiteboard, board.Id)] = new Resolved(board.Name, board.ProjectId);
                }
            }
        }

        // Teams are visible to everyone in the organization (docs/PERMISSIONS.md).
        var teamIds = resources.Where(r => r.Type == KnowledgeResourceType.Team).Select(r => r.Id).ToList();
        if (teamIds.Count > 0)
        {
            var teams = await db.Set<Team>()
                .Where(t => teamIds.Contains(t.Id) && t.OrganizationId == user.OrganizationId)
                .Select(t => new { t.Id, t.Name })
                .ToListAsync(ct);
            foreach (var team in teams)
            {
                result[(KnowledgeResourceType.Team, team.Id)] = new Resolved(team.Name, null);
            }
        }

        return result;
    }

    /// <summary>True when <paramref name="articleId"/> is (transitively) part of <paramref name="ancestorId"/>.</summary>
    private async Task<bool> IsPartOfAsync(Guid articleId, Guid ancestorId, CancellationToken ct)
    {
        var frontier = new List<Guid> { articleId };
        var seen = new HashSet<Guid>();
        for (var depth = 0; frontier.Count > 0 && depth < MaxHierarchyDepth; depth++)
        {
            if (frontier.Contains(ancestorId))
            {
                return true;
            }

            seen.UnionWith(frontier);
            frontier = await db.Set<KnowledgeRelation>()
                .Where(r => r.RelationType == KnowledgeRelationType.PartOf && frontier.Contains(r.SourceArticleId))
                .Select(r => r.TargetArticleId)
                .ToListAsync(ct);
            frontier = frontier.Where(id => !seen.Contains(id)).ToList();
        }

        return false;
    }
}
