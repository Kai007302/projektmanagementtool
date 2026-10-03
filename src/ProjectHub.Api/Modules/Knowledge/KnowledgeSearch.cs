using Microsoft.EntityFrameworkCore;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Modules.Users;

namespace ProjectHub.Api.Modules.Knowledge;

public sealed record ArticleSummary(
    Guid Id,
    string Title,
    string Slug,
    string ArticleType,
    string? Summary,
    string Status,
    string Visibility,
    Guid? SpaceId,
    string? SpaceName,
    Guid? OwnerId,
    string? OwnerName,
    IReadOnlyList<string> Tags,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? PublishedAt,
    long Version);

public sealed record KnowledgeQuery(string? Text, string? ArticleType, string? Status, Guid? SpaceId, string? Tag);

/// <summary>
/// Finds knowledge the reader may see. Implementations must apply <see cref="KnowledgeReader"/>
/// inside the query; they never return content the reader is not allowed to read.
/// </summary>
public interface IKnowledgeSearch
{
    Task<IReadOnlyList<ArticleSummary>> SearchAsync(KnowledgeReader reader, KnowledgeQuery query, Paging paging, CancellationToken ct);
}

/// <summary>A passage of an authorized article, the unit a semantic retrieval or an AI answer works with.</summary>
public sealed record KnowledgePassage(Guid ArticleId, string Title, string Text, double Score);

/// <summary>
/// Semantic retrieval (embeddings). Not implemented yet: it needs the AI provider decision
/// (docs/OPEN_DECISIONS.md). The reader is part of the contract, as for <see cref="IKnowledgeSearch"/>.
/// </summary>
public interface IKnowledgeSemanticSearch
{
    Task<IReadOnlyList<KnowledgePassage>> RetrieveAsync(KnowledgeReader reader, string question, int limit, CancellationToken ct);
}

public sealed record KnowledgeAnswer(string Text, IReadOnlyList<KnowledgePassage> Sources);

/// <summary>Answers questions from authorized knowledge and always names its sources. Not implemented yet.</summary>
public interface IKnowledgeAnswerService
{
    Task<KnowledgeAnswer> AnswerAsync(KnowledgeReader reader, string question, CancellationToken ct);
}

/// <summary>PostgreSQL full-text search (German stemming) over title, summary and the current content.</summary>
internal sealed class PostgresKnowledgeSearch(ProjectHubDbContext db, KnowledgeAccess access) : IKnowledgeSearch
{
    public const string TextSearchConfiguration = "german";

    public async Task<IReadOnlyList<ArticleSummary>> SearchAsync(KnowledgeReader reader, KnowledgeQuery query, Paging paging, CancellationToken ct)
    {
        var articles = access.Visible(reader);
        if (query.ArticleType is { } type)
        {
            articles = articles.Where(a => a.ArticleType == type);
        }

        if (query.Status is { } status)
        {
            articles = articles.Where(a => a.Status == status);
        }

        if (query.SpaceId is { } spaceId)
        {
            articles = articles.Where(a => a.KnowledgeSpaceId == spaceId);
        }

        if (query.Tag is { Length: > 0 } tag)
        {
            var name = tag.Trim().ToLower();
            articles = articles.Where(a => db.Set<KnowledgeArticleTag>().Any(at => at.ArticleId == a.Id
                && db.Set<KnowledgeTag>().Any(t => t.Id == at.TagId && t.Name.ToLower() == name)));
        }

        IQueryable<KnowledgeArticle> ordered;
        if (query.Text is { Length: > 0 } text)
        {
            // Same expression as the index ix_knowledge_article_search (migration 006).
            ordered = articles
                .Where(a => EF.Functions.ToTsVector(TextSearchConfiguration, a.Title + " " + (a.Summary ?? "") + " " + a.SearchText)
                    .Matches(EF.Functions.WebSearchToTsQuery(TextSearchConfiguration, text)))
                .OrderByDescending(a => EF.Functions.ToTsVector(TextSearchConfiguration, a.Title + " " + (a.Summary ?? "") + " " + a.SearchText)
                    .Rank(EF.Functions.WebSearchToTsQuery(TextSearchConfiguration, text)))
                .ThenByDescending(a => a.UpdatedAt);
        }
        else
        {
            ordered = articles.OrderByDescending(a => a.UpdatedAt).ThenBy(a => a.Id);
        }

        var ids = await ordered.Skip(paging.Skip).Take(paging.Take + 1).Select(a => a.Id).ToListAsync(ct);
        return await KnowledgeSummaries.LoadAsync(db, ids, ct);
    }
}

internal static class KnowledgeSummaries
{
    /// <summary>Summaries for already authorized ids, in the order given.</summary>
    public static async Task<IReadOnlyList<ArticleSummary>> LoadAsync(ProjectHubDbContext db, IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var rows = await (
                from article in db.Set<KnowledgeArticle>().AsNoTracking()
                where ids.Contains(article.Id)
                join owner in db.Set<AppUser>() on article.OwnerId equals owner.Id into owners
                from owner in owners.DefaultIfEmpty()
                join space in db.Set<KnowledgeSpace>() on article.KnowledgeSpaceId equals space.Id into spaces
                from space in spaces.DefaultIfEmpty()
                select new { article, OwnerName = owner == null ? null : owner.DisplayName, SpaceName = space == null ? null : space.Name })
            .ToListAsync(ct);

        var tags = await (
                from link in db.Set<KnowledgeArticleTag>()
                where ids.Contains(link.ArticleId)
                join tag in db.Set<KnowledgeTag>() on link.TagId equals tag.Id
                select new { link.ArticleId, tag.Name })
            .ToListAsync(ct);
        var tagsByArticle = tags.GroupBy(t => t.ArticleId).ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(t => t.Name).Order().ToList());

        var byId = rows.ToDictionary(r => r.article.Id);
        return ids.Where(byId.ContainsKey).Select(id =>
        {
            var row = byId[id];
            var a = row.article;
            return new ArticleSummary(
                a.Id, a.Title, a.Slug, a.ArticleType, a.Summary, a.Status, a.Visibility, a.KnowledgeSpaceId, row.SpaceName,
                a.OwnerId, row.OwnerName, tagsByArticle.GetValueOrDefault(a.Id, []), a.UpdatedAt, a.PublishedAt, a.Version);
        }).ToList();
    }
}
