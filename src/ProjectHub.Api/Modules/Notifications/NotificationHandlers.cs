using Microsoft.EntityFrameworkCore;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Infrastructure.Events;
using ProjectHub.Api.Modules.Comments;
using ProjectHub.Api.Modules.Knowledge;
using ProjectHub.Api.Modules.Projects;
using ProjectHub.Api.Modules.Tasks;
using ProjectHub.Api.Modules.Users;

namespace ProjectHub.Api.Modules.Notifications;

/// <summary>Turns domain events of other modules into notifications. The modules do not know about this one.</summary>
internal sealed class NotificationHandlers(NotificationDispatcher dispatcher)
    : IDomainEventHandler<TaskAssigned>,
      IDomainEventHandler<UsersMentionedInComment>,
      IDomainEventHandler<UsersMentionedInKnowledgeComment>,
      IDomainEventHandler<ProjectMemberAdded>
{
    public Task HandleAsync(TaskAssigned e, CancellationToken ct) =>
        dispatcher.DispatchAsync(e.OrganizationId, e.ActorId, async db =>
        {
            var actor = await NameAsync(db, e.ActorId, ct);
            var title = await db.Set<ProjectTask>().Where(t => t.Id == e.TaskId).Select(t => t.Title).SingleAsync(ct);
            return [new NotificationDraft(e.AssigneeId, NotificationTypes.TaskAssigned, $"{actor} hat dir „{title}“ zugewiesen", null, NotificationResources.Task, e.TaskId)];
        }, ct);

    public Task HandleAsync(UsersMentionedInComment e, CancellationToken ct) =>
        dispatcher.DispatchAsync(e.OrganizationId, e.AuthorId, async db =>
        {
            var actor = await NameAsync(db, e.AuthorId, ct);
            var title = await db.Set<ProjectTask>().Where(t => t.Id == e.TaskId).Select(t => t.Title).SingleAsync(ct);
            var content = await db.Set<TaskComment>().Where(c => c.Id == e.CommentId).Select(c => c.Content).SingleAsync(ct);
            return e.MentionedUserIds
                .Select(id => new NotificationDraft(id, NotificationTypes.TaskCommentMention,
                    $"{actor} hat dich bei „{title}“ erwähnt", content, NotificationResources.Task, e.TaskId))
                .ToList();
        }, ct);

    public Task HandleAsync(UsersMentionedInKnowledgeComment e, CancellationToken ct) =>
        dispatcher.DispatchAsync(e.OrganizationId, e.AuthorId, async db =>
        {
            var actor = await NameAsync(db, e.AuthorId, ct);
            var title = await db.Set<KnowledgeArticle>().Where(a => a.Id == e.ArticleId).Select(a => a.Title).SingleAsync(ct);
            var content = await db.Set<KnowledgeComment>().Where(c => c.Id == e.CommentId).Select(c => c.Content).SingleAsync(ct);
            return e.MentionedUserIds
                .Select(id => new NotificationDraft(id, NotificationTypes.KnowledgeCommentMention,
                    $"{actor} hat dich im Artikel „{title}“ erwähnt", content, NotificationResources.KnowledgeArticle, e.ArticleId))
                .ToList();
        }, ct);

    public Task HandleAsync(ProjectMemberAdded e, CancellationToken ct) =>
        dispatcher.DispatchAsync(e.OrganizationId, e.ActorId, async db =>
        {
            var actor = await NameAsync(db, e.ActorId, ct);
            var name = await db.Set<Project>().Where(p => p.Id == e.ProjectId).Select(p => p.Name).SingleAsync(ct);
            return [new NotificationDraft(e.UserId, NotificationTypes.ProjectMemberAdded, $"{actor} hat dich zum Projekt „{name}“ hinzugefügt", null, NotificationResources.Project, e.ProjectId)];
        }, ct);

    private static Task<string> NameAsync(ProjectHubDbContext db, Guid userId, CancellationToken ct) =>
        db.Set<AppUser>().Where(u => u.Id == userId).Select(u => u.DisplayName).SingleAsync(ct);
}
