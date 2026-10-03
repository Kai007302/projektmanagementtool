using Microsoft.EntityFrameworkCore;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Infrastructure.Events;
using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Infrastructure.Outcomes;
using ProjectHub.Api.Modules.Audit;
using ProjectHub.Api.Modules.Identity;
using ProjectHub.Api.Modules.Identity.Authorization;
using ProjectHub.Api.Modules.Projects;
using ProjectHub.Api.Modules.Tasks;
using ProjectHub.Api.Modules.Users;

namespace ProjectHub.Api.Modules.Comments;

public sealed record CommentResponse(
    Guid Id, Guid TaskId, Guid AuthorId, string AuthorName, string Content, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, long Version);

public sealed record CreateCommentRequest(string? Content, IReadOnlyList<Guid>? MentionedUserIds);

public sealed record UpdateCommentRequest(string? Content, long? Version);

public sealed class CommentService(
    ProjectHubDbContext db,
    TaskAccess tasks,
    IProjectHubAuthorization authorization,
    IActivityLog activity,
    IDomainEventPublisher events,
    TimeProvider clock)
{
    public const int MaxContentLength = 10_000;
    public const int MaxMentions = 50;

    public async Task<ServiceResult<IReadOnlyList<CommentResponse>>> ListAsync(UserContext user, Guid taskId, Paging paging, CancellationToken ct)
    {
        if (await tasks.RequireAsync(user, taskId, ProjectPermission.View, ct) is { Failure: { } failure })
        {
            return failure;
        }

        return await Project(ActiveComments(user).Where(c => c.TaskId == taskId).OrderBy(c => c.CreatedAt).ThenBy(c => c.Id))
            .Skip(paging.Skip).Take(paging.Take + 1)
            .ToListAsync(ct);
    }

    public async Task<ServiceResult<CommentResponse>> AddAsync(UserContext user, Guid taskId, CreateCommentRequest request, CancellationToken ct)
    {
        var (projectId, failure) = await tasks.RequireAsync(user, taskId, ProjectPermission.Contribute, ct);
        if (failure is not null)
        {
            return failure;
        }

        var content = request.Content?.Trim() ?? string.Empty;
        if (ValidateContent(content) is { } invalid)
        {
            return invalid;
        }

        var mentioned = (request.MentionedUserIds ?? []).Distinct().Where(id => id != user.UserId).ToList();
        if (mentioned.Count > MaxMentions)
        {
            return ServiceFailure.Invalid("mentionedUserIds", $"At most {MaxMentions} people.");
        }

        foreach (var mentionedId in mentioned)
        {
            if (!await CanViewProjectAsUserAsync(user.OrganizationId, projectId, mentionedId, ct))
            {
                return ServiceFailure.Invalid("mentionedUserIds", "Only members of the project can be mentioned.");
            }
        }

        var now = clock.GetUtcNow();
        var comment = new TaskComment
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = user.OrganizationId,
            TaskId = taskId,
            AuthorId = user.UserId,
            Content = content,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };

        db.Set<TaskComment>().Add(comment);
        activity.Record(user, projectId, ActivityActions.CommentAdded, "task_comment", comment.Id, new { TaskId = taskId, Mentions = mentioned.Count });
        await db.SaveChangesAsync(ct);

        if (mentioned.Count > 0)
        {
            await events.PublishAsync(new UsersMentionedInComment(user.OrganizationId, projectId, taskId, comment.Id, user.UserId, mentioned), ct);
        }

        return await Project(ActiveComments(user).Where(c => c.Id == comment.Id)).SingleAsync(ct);
    }

    /// <summary>Only the author edits a comment.</summary>
    public async Task<ServiceResult<CommentResponse>> UpdateAsync(UserContext user, Guid commentId, UpdateCommentRequest request, CancellationToken ct)
    {
        var comment = await FindVisibleAsync(user, commentId, ct);
        if (comment is null)
        {
            return ServiceFailure.NotFound("Comment");
        }

        if (comment.AuthorId != user.UserId)
        {
            return ServiceFailure.Forbidden("Only the author can edit a comment.");
        }

        if (request.Version is not { } expectedVersion)
        {
            return ServiceFailure.Invalid("version", "Required: the version the change is based on.");
        }

        if (comment.Version != expectedVersion)
        {
            return ServiceFailure.StaleVersion(comment.Version);
        }

        var content = request.Content?.Trim() ?? string.Empty;
        if (ValidateContent(content) is { } invalid)
        {
            return invalid;
        }

        comment.Content = content;
        db.Touch(comment, expectedVersion, clock.GetUtcNow());
        if (await db.SaveVersionedAsync(comment, ct) is { } conflict)
        {
            return conflict;
        }

        return await Project(ActiveComments(user).Where(c => c.Id == commentId)).SingleAsync(ct);
    }

    /// <summary>The author or a project manager deletes a comment (soft delete).</summary>
    public async Task<ServiceResult<Done>> DeleteAsync(UserContext user, Guid commentId, CancellationToken ct)
    {
        var comment = await FindVisibleAsync(user, commentId, ct);
        if (comment is null)
        {
            return ServiceFailure.NotFound("Comment");
        }

        var projectId = await db.Set<ProjectTask>().Where(t => t.Id == comment.TaskId).Select(t => t.ProjectId).SingleAsync(ct);
        if (comment.AuthorId != user.UserId && !await authorization.CanManageProjectAsync(user, projectId, ct))
        {
            return ServiceFailure.Forbidden("Only the author or a project admin can delete a comment.");
        }

        var now = clock.GetUtcNow();
        comment.DeletedAt = now;
        db.Touch(comment, comment.Version, now);
        return await db.SaveVersionedAsync(comment, ct) is { } conflict ? conflict : Done.Value;
    }

    private async Task<TaskComment?> FindVisibleAsync(UserContext user, Guid commentId, CancellationToken ct)
    {
        var comment = await ActiveComments(user).AsTracking().SingleOrDefaultAsync(c => c.Id == commentId, ct);
        if (comment is null)
        {
            return null;
        }

        return await tasks.RequireAsync(user, comment.TaskId, ProjectPermission.View, ct) is { Failure: null } ? comment : null;
    }

    private async Task<bool> CanViewProjectAsUserAsync(Guid organizationId, Guid projectId, Guid userId, CancellationToken ct)
    {
        var mentioned = await db.Set<AppUser>().AsNoTracking()
            .Where(u => u.Id == userId && u.OrganizationId == organizationId && u.Status == UserStatus.Active)
            .Select(u => new UserContext(u.Id, u.OrganizationId, u.OrganizationRole, u.DisplayName, u.Email))
            .SingleOrDefaultAsync(ct);
        return mentioned is not null && await authorization.CanViewProjectAsync(mentioned, projectId, ct);
    }

    private IQueryable<TaskComment> ActiveComments(UserContext user) =>
        db.Set<TaskComment>().AsNoTracking().Where(c => c.OrganizationId == user.OrganizationId && c.DeletedAt == null);

    private IQueryable<CommentResponse> Project(IQueryable<TaskComment> comments) =>
        from comment in comments
        join author in db.Set<AppUser>() on comment.AuthorId equals author.Id
        select new CommentResponse(
            comment.Id, comment.TaskId, comment.AuthorId, author.DisplayName, comment.Content, comment.CreatedAt, comment.UpdatedAt, comment.Version);

    private static ServiceFailure? ValidateContent(string content) =>
        content.Length is 0 or > MaxContentLength
            ? ServiceFailure.Invalid("content", $"Required, at most {MaxContentLength} characters.")
            : null;
}
