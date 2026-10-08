using Microsoft.EntityFrameworkCore;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Modules.Identity;

namespace ProjectHub.Api.Modules.Knowledge;

public sealed record GraphNode(Guid Id, string Title, string ArticleType, string Status, Guid? SpaceId, string? Summary, int Degree);

public sealed record GraphEdge(Guid Id, Guid Source, Guid Target, string RelationType);

/// <param name="Truncated">More articles are visible than <see cref="KnowledgeGraphService.MaxNodes"/>; the newest are shown.</param>
public sealed record KnowledgeGraph(IReadOnlyList<GraphNode> Nodes, IReadOnlyList<GraphEdge> Edges, bool Truncated);

/// <summary>
/// The Knowledge Galaxy is a projection of articles and their relations (ai/prompts/03-knowledge-hub.md):
/// it has no data of its own. Only articles the reader may see become nodes, and only relations
/// between two such articles become edges.
/// </summary>
public sealed class KnowledgeGraphService(ProjectHubDbContext db, KnowledgeAccess access)
{
    public const int MaxNodes = 2000;

    /// <summary><paramref name="departmentId"/>: only the articles of one department, its own galaxy (ADR 0021).</summary>
    public async Task<KnowledgeGraph> GetAsync(UserContext user, Guid? spaceId, string? articleType, CancellationToken ct, Guid? departmentId = null)
    {
        var articles = access.Visible(await access.ReaderAsync(user, ct));
        if (departmentId is { } department)
        {
            articles = articles.Where(a => a.DepartmentId == department);
        }

        if (spaceId is { } space)
        {
            articles = articles.Where(a => a.KnowledgeSpaceId == space);
        }

        if (articleType is { Length: > 0 } type)
        {
            articles = articles.Where(a => a.ArticleType == type);
        }

        var nodes = await articles
            .OrderByDescending(a => a.UpdatedAt)
            .ThenBy(a => a.Id)
            .Take(MaxNodes + 1)
            .Select(a => new { a.Id, a.Title, a.ArticleType, a.Status, a.KnowledgeSpaceId, a.Summary })
            .ToListAsync(ct);
        var truncated = nodes.Count > MaxNodes;
        if (truncated)
        {
            nodes.RemoveAt(nodes.Count - 1);
        }

        var ids = nodes.Select(n => n.Id).ToList();
        var edges = await db.Set<KnowledgeRelation>().AsNoTracking()
            .Where(r => r.OrganizationId == user.OrganizationId && ids.Contains(r.SourceArticleId) && ids.Contains(r.TargetArticleId))
            .OrderBy(r => r.Id)
            .Select(r => new GraphEdge(r.Id, r.SourceArticleId, r.TargetArticleId, r.RelationType))
            .ToListAsync(ct);

        var degree = edges.SelectMany(e => new[] { e.Source, e.Target }).GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());
        return new KnowledgeGraph(
            nodes.Select(n => new GraphNode(n.Id, n.Title, n.ArticleType, n.Status, n.KnowledgeSpaceId, n.Summary, degree.GetValueOrDefault(n.Id))).ToList(),
            edges,
            truncated);
    }
}
