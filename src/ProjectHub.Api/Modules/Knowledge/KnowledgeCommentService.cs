using Microsoft.EntityFrameworkCore;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Infrastructure.Events;
using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Infrastructure.Outcomes;
using ProjectHub.Api.Modules.Identity;
using ProjectHub.Api.Modules.Users;

namespace ProjectHub.Api.Modules.Knowledge;

public sealed record KnowledgeCommentResponse(
    Guid Id, Guid ArticleId, Guid AuthorId, string AuthorName, string Content, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, long Version);

public sealed record CreateKnowledgeCommentRequest(string? Content, IReadOnlyList<Guid>? MentionedUserIds);

public sealed record UpdateKnowledgeCommentRequest(string? Content, long? Version);

/// <summary>
/// Raised after a knowledge comment mentioning people was saved. Only people who can read the
/// article can be mentioned. The notifications module (phase 6) turns it into notifications.
/// </summary>
public sealed record UsersMentionedInKnowledgeComment(
    Guid OrganizationId, Guid ArticleId, Guid CommentId, Guid AuthorId, IReadOnlyList<Guid> MentionedUserIds) : IDomainEvent;

/// <summary>Everyone who can read an article can discuss it.</summary>
public sealed class KnowledgeCommentService(
    ProjectHubDbContext db,
    KnowledgeAccess access,
    IDomainEventPublisher events,
    TimeProvider clock)
{
    public const int MaxContentLength = 10_000;
    public const int MaxMentions = 50;

    public async Task<ServiceResult<IReadOnlyList<KnowledgeCommentResponse>>> ListAsync(UserContext user, Guid articleId, Paging paging, CancellationToken ct)
    {
        if (await access.RequireAsync(user, articleId, KnowledgeRight.View, ct) is { Failure: { } failure })
        {
            return failure;
        }

        return await Project(Active(user).Where(c => c.ArticleId == articleId).OrderBy(c => c.CreatedAt).ThenBy(c => c.Id))
            .Skip(paging.Skip).Take(paging.Take + 1)
            .ToListAsync(ct);
    }

    public async Task<ServiceResult<KnowledgeCommentResponse>> AddAsync(UserContext user, Guid articleId, CreateKnowledgeCommentRequest request, CancellationToken ct)
    {
        var (article, _, _, failure) = await access.RequireAsync(user, articleId, KnowledgeRight.View, ct);
        if (failure is not null)
        {
            return failure;
        }

        var content = request.Content?.Trim() ?? string.Empty;
        if (content.Length is 0 or > MaxContentLength)
        {
            return ServiceFailure.Invalid("content", $"Required, at most {MaxContentLength} characters.");
        }

        var mentioned = (request.MentionedUserIds ?? []).Distinct().Where(id => id != user.UserId).ToList();
        if (mentioned.Count > MaxMentions)
        {
            return ServiceFailure.Invalid("mentionedUserIds", $"At most {MaxMentions} people.");
        }

        foreach (var mentionedId in mentioned)
        {
            var reader = await access.ReaderForAsync(user.OrganizationId, mentionedId, ct);
            if (reader is null || await access.RightAsync(reader, article!, ct) == KnowledgeRight.None)
            {
                return ServiceFailure.Invalid("mentionedUserIds", "Only people who can read the article can be mentioned.");
            }
        }

        var now = clock.GetUtcNow();
        var comment = new KnowledgeComment
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = user.OrganizationId,
            ArticleId = articleId,
            AuthorId = user.UserId,
            Content = content,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
        db.Set<KnowledgeComment>().Add(comment);
        await db.SaveChangesAsync(ct);

        if (mentioned.Count > 0)
        {
            await events.PublishAsync(new UsersMentionedInKnowledgeComment(user.OrganizationId, articleId, comment.Id, user.UserId, mentioned), ct);
        }

        return await Project(Active(user).Where(c => c.Id == comment.Id)).SingleAsync(ct);
    }

    /// <summary>Only the author edits a comment.</summary>
    public async Task<ServiceResult<KnowledgeCommentResponse>> UpdateAsync(UserContext user, Guid commentId, UpdateKnowledgeCommentRequest request, CancellationToken ct)
    {
        var (comment, _, failure) = await FindAsync(user, commentId, ct);
        if (failure is not null)
        {
            return failure;
        }

        if (comment!.AuthorId != user.UserId)
        {
            return ServiceFailure.Forbidden("Only the author can edit a comment.");
        }

        if (request.Version is not { } expectedVersion)
        {
            return ServiceFailure.Invalid("version", "Required: the version the change is based on.");
        }

        var content = request.Content?.Trim() ?? string.Empty;
        if (content.Length is 0 or > MaxContentLength)
        {
            return ServiceFailure.Invalid("content", $"Required, at most {MaxContentLength} characters.");
        }

        if (comment.Version != expectedVersion)
        {
            return ServiceFailure.StaleVersion(comment.Version);
        }

        comment.Content = content;
        db.Touch(comment, expectedVersion, clock.GetUtcNow());
        return await db.SaveVersionedAsync(comment, ct) is { } conflict
            ? conflict
            : await Project(Active(user).Where(c => c.Id == comment.Id)).SingleAsync(ct);
    }

    /// <summary>The author or an article admin deletes a comment.</summary>
    public async Task<ServiceResult<Done>> DeleteAsync(UserContext user, Guid commentId, CancellationToken ct)
    {
        var (comment, right, failure) = await FindAsync(user, commentId, ct);
        if (failure is not null)
        {
            return failure;
        }

        if (comment!.AuthorId != user.UserId && right < KnowledgeRight.Admin)
        {
            return ServiceFailure.Forbidden("Only the author or an article admin can delete a comment.");
        }

        var now = clock.GetUtcNow();
        comment.DeletedAt = now;
        db.Touch(comment, comment.Version, now);
        return await db.SaveVersionedAsync(comment, ct) is { } conflict ? conflict : Done.Value;
    }

    private async Task<(KnowledgeComment? Comment, KnowledgeRight Right, ServiceFailure? Failure)> FindAsync(UserContext user, Guid commentId, CancellationToken ct)
    {
        var comment = await db.Set<KnowledgeComment>().AsTracking()
            .SingleOrDefaultAsync(c => c.Id == commentId && c.OrganizationId == user.OrganizationId && c.DeletedAt == null, ct);
        if (comment is null)
        {
            return (null, KnowledgeRight.None, ServiceFailure.NotFound("Comment"));
        }

        var (_, _, right, failure) = await access.RequireAsync(user, comment.ArticleId, KnowledgeRight.View, ct);
        return failure is null ? (comment, right, null) : (null, KnowledgeRight.None, ServiceFailure.NotFound("Comment"));
    }

    private IQueryable<KnowledgeComment> Active(UserContext user) =>
        db.Set<KnowledgeComment>().AsNoTracking().Where(c => c.OrganizationId == user.OrganizationId && c.DeletedAt == null);

    private IQueryable<KnowledgeCommentResponse> Project(IQueryable<KnowledgeComment> comments) =>
        from comment in comments
        join author in db.Set<AppUser>() on comment.AuthorId equals author.Id
        select new KnowledgeCommentResponse(
            comment.Id, comment.ArticleId, comment.AuthorId, author.DisplayName, comment.Content, comment.CreatedAt, comment.UpdatedAt, comment.Version);
}
