using System.Text.RegularExpressions;
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

/// <summary>A passage of an authorized article, the unit the assistant answers from (ADR 0015).</summary>
public sealed record KnowledgePassage(Guid ArticleId, string Title, string? SpaceName, string Text, double Score);

/// <summary>
/// Finds the passages of authorized knowledge that fit a question in natural language (retrieval for the AI
/// assistant, ADR 0015). The reader is part of the contract, as for <see cref="IKnowledgeSearch"/>: an
/// implementation never returns passages of articles the reader may not see. Embeddings can replace or
/// complement the full-text implementation behind this interface later.
/// </summary>
public interface IKnowledgeRetrieval
{
    Task<IReadOnlyList<KnowledgePassage>> RetrieveAsync(KnowledgeReader reader, string question, int limit, CancellationToken ct);
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
            // The stored vector (migration 011) is indexed; neither the match nor the rank recomputes it.
            ordered = articles
                .Where(a => a.SearchVector.Matches(EF.Functions.WebSearchToTsQuery(TextSearchConfiguration, text)))
                .OrderByDescending(a => a.SearchVector.Rank(EF.Functions.WebSearchToTsQuery(TextSearchConfiguration, text)))
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

/// <summary>
/// Retrieval over the full-text index: a question matches articles that contain any of its words (OR instead of the
/// AND of the search box, questions are long), ranked by cover density; the passages are PostgreSQL headlines around
/// the matches. Only articles from <see cref="KnowledgeAccess.Visible"/> are considered.
/// </summary>
public sealed partial class PostgresKnowledgeRetrieval(ProjectHubDbContext db, KnowledgeAccess access) : IKnowledgeRetrieval
{
    public const int MaxLimit = 20;
    private const int MaxTerms = 24;
    private const string HeadlineOptions = "MaxFragments=3, MaxWords=45, MinWords=15, FragmentDelimiter=\" … \", StartSel=\"\", StopSel=\"\"";

    public async Task<IReadOnlyList<KnowledgePassage>> RetrieveAsync(KnowledgeReader reader, string question, int limit, CancellationToken ct)
    {
        if (TermQuery(question) is not { } terms)
        {
            return [];
        }

        // The query is built inline: EF translates EF.Functions only inside the expression tree.
        var rows = await (
                from article in access.Visible(reader)
                let query = EF.Functions.ToTsQuery(PostgresKnowledgeSearch.TextSearchConfiguration, terms)
                where article.SearchVector.Matches(query)
                join space in db.Set<KnowledgeSpace>() on article.KnowledgeSpaceId equals space.Id into spaces
                from space in spaces.DefaultIfEmpty()
                let score = article.SearchVector.RankCoverDensity(query)
                orderby score descending, article.UpdatedAt descending
                select new
                {
                    article.Id,
                    article.Title,
                    SpaceName = space == null ? null : space.Name,
                    Text = query.GetResultHeadline((article.Summary ?? string.Empty) + "\n" + article.SearchText, HeadlineOptions),
                    Score = score,
                })
            .Take(Math.Clamp(limit, 1, MaxLimit))
            .ToListAsync(ct);

        return rows.Select(r => new KnowledgePassage(r.Id, r.Title, r.SpaceName, r.Text.Trim(), r.Score)).ToList();
    }

    /// <summary>
    /// "'wort1' | 'wort2' | …" from the letters and digits of the question; quoting keeps every word a plain lexeme,
    /// so no input can form tsquery syntax. Stop words are dropped by the text search configuration.
    /// </summary>
    public static string? TermQuery(string question)
    {
        var words = Word().Matches(question)
            .Select(m => m.Value.ToLowerInvariant())
            .Where(w => w.Length >= 2)
            .Distinct()
            .Take(MaxTerms)
            .Select(w => $"'{w}'")
            .ToList();
        return words.Count == 0 ? null : string.Join(" | ", words);
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Word();
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
