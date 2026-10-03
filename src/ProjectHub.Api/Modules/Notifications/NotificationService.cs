using Microsoft.EntityFrameworkCore;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Infrastructure.Outcomes;
using ProjectHub.Api.Modules.Identity;
using ProjectHub.Api.Modules.Tasks;

namespace ProjectHub.Api.Modules.Notifications;

/// <summary><paramref name="ProjectId"/> lets the client open a task inside its project without another request.</summary>
public sealed record NotificationResponse(
    Guid Id,
    string Type,
    string Title,
    string? Body,
    string? ResourceType,
    Guid? ResourceId,
    Guid? ProjectId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt);

public sealed record UnreadCountResponse(int Count);

/// <summary>Channels of a person. <c>WebexAvailable</c> says whether Webex is set up at all (ADR 0011).</summary>
public sealed record NotificationPreferencesResponse(bool InAppEnabled, bool EmailEnabled, bool WebexEnabled, bool WebexAvailable);

/// <summary>Without <c>WebexEnabled</c> the Webex setting stays as it is.</summary>
public sealed record UpdatePreferencesRequest(bool? InAppEnabled, bool? EmailEnabled, bool? WebexEnabled = null);

/// <summary>A person's own notifications and settings. Nobody else's are ever reachable.</summary>
public sealed class NotificationService(ProjectHubDbContext db, NotificationDispatcher dispatcher, NotificationChannelOptions channels, TimeProvider clock)
{
    public async Task<List<NotificationResponse>> ListAsync(UserContext user, bool unreadOnly, Paging paging, CancellationToken ct)
    {
        var query = Own(user);
        if (unreadOnly)
        {
            query = query.Where(n => n.ReadAt == null);
        }

        var rows = await query.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Skip(paging.Skip).Take(paging.Take + 1)
            .ToListAsync(ct);

        var taskIds = rows.Where(n => n.ResourceType == NotificationResources.Task && n.ResourceId != null).Select(n => n.ResourceId!.Value).ToList();
        var projects = await db.Set<ProjectTask>().AsNoTracking()
            .Where(t => t.OrganizationId == user.OrganizationId && taskIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.ProjectId, ct);

        return rows.Select(n => new NotificationResponse(
                n.Id, n.Type, n.Title, n.Body, n.ResourceType, n.ResourceId, ProjectOf(n, projects), n.CreatedAt, n.ReadAt))
            .ToList();
    }

    private static Guid? ProjectOf(Notification notification, Dictionary<Guid, Guid> taskProjects) =>
        notification switch
        {
            { ResourceType: NotificationResources.Project } => notification.ResourceId,
            { ResourceType: NotificationResources.Task, ResourceId: { } taskId } when taskProjects.TryGetValue(taskId, out var projectId) => projectId,
            _ => null,
        };

    public async Task<UnreadCountResponse> UnreadCountAsync(UserContext user, CancellationToken ct) =>
        new(await Own(user).CountAsync(n => n.ReadAt == null, ct));

    public async Task<ServiceResult<Done>> MarkReadAsync(UserContext user, Guid notificationId, CancellationToken ct)
    {
        if (!await Own(user).AnyAsync(n => n.Id == notificationId, ct))
        {
            return ServiceFailure.NotFound("Notification");
        }

        await Own(user).Where(n => n.Id == notificationId && n.ReadAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(n => n.ReadAt, clock.GetUtcNow()), ct);
        await PushAsync(user, ct);
        return Done.Value;
    }

    public async Task<Done> MarkAllReadAsync(UserContext user, CancellationToken ct)
    {
        await Own(user).Where(n => n.ReadAt == null).ExecuteUpdateAsync(set => set.SetProperty(n => n.ReadAt, clock.GetUtcNow()), ct);
        await PushAsync(user, ct);
        return Done.Value;
    }

    public async Task<NotificationPreferencesResponse> PreferencesAsync(UserContext user, CancellationToken ct)
    {
        var preference = await db.Set<NotificationPreference>().AsNoTracking()
            .SingleOrDefaultAsync(p => p.OrganizationId == user.OrganizationId && p.UserId == user.UserId, ct) ?? new NotificationPreference();
        return new NotificationPreferencesResponse(preference.InAppEnabled, preference.EmailEnabled, preference.WebexEnabled, channels.WebexAvailable);
    }

    public async Task<ServiceResult<NotificationPreferencesResponse>> UpdatePreferencesAsync(UserContext user, UpdatePreferencesRequest request, CancellationToken ct)
    {
        if (request.InAppEnabled is not { } inApp)
        {
            return ServiceFailure.Invalid("inAppEnabled", "Required.");
        }

        if (request.EmailEnabled is not { } email)
        {
            return ServiceFailure.Invalid("emailEnabled", "Required.");
        }

        // Upsert: two tabs saving at once must not fail on the primary key.
        var now = clock.GetUtcNow();
        var webex = request.WebexEnabled;
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             insert into notification_preference (organization_id, user_id, in_app_enabled, email_enabled, webex_enabled, updated_at)
             values ({user.OrganizationId}, {user.UserId}, {inApp}, {email}, coalesce({webex}, false), {now})
             on conflict (organization_id, user_id)
             do update set in_app_enabled = excluded.in_app_enabled, email_enabled = excluded.email_enabled,
                 webex_enabled = coalesce({webex}, notification_preference.webex_enabled), updated_at = excluded.updated_at
             """,
            ct);
        return await PreferencesAsync(user, ct);
    }

    private IQueryable<Notification> Own(UserContext user) =>
        db.Set<Notification>().AsNoTracking().Where(n => n.OrganizationId == user.OrganizationId && n.UserId == user.UserId);

    /// <summary>Other open tabs of the same person update their counter too.</summary>
    private async Task PushAsync(UserContext user, CancellationToken ct) =>
        await dispatcher.PushAsync(user.UserId, (await UnreadCountAsync(user, ct)).Count);
}
