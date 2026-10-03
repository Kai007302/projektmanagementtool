using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Modules.Users;

namespace ProjectHub.Api.Modules.Notifications;

/// <summary>A notification to deliver, before preferences are applied.</summary>
public sealed record NotificationDraft(Guid RecipientId, string Type, string Title, string? Body, string ResourceType, Guid ResourceId);

public sealed record NotificationOptions(string AppUrl)
{
    public const string AppUrlKey = "PROJECTHUB_APP_URL";
}

/// <summary>
/// Delivers notifications on the channels each recipient wants. Runs after the change that caused them
/// was committed, in its own unit of work: a failure here is logged and never undoes or fails that change.
/// Mails go to the outbox in the same transaction as the in-app notifications (ADR 0010).
/// </summary>
public sealed class NotificationDispatcher(
    IServiceScopeFactory scopes,
    MailOutboxSignal outbox,
    NotificationChannelOptions channels,
    IHubContext<NotificationsHub, INotificationsClient> hub,
    NotificationOptions options,
    TimeProvider clock,
    ILogger<NotificationDispatcher> logger)
{
    public const int MaxBodyLength = 200;

    /// <summary>
    /// Builds the drafts with a fresh database context and delivers them. Recipients outside the organization,
    /// inactive people and the actor themself are skipped.
    /// </summary>
    public async Task DispatchAsync(
        Guid organizationId, Guid actorId, Func<ProjectHubDbContext, Task<IReadOnlyList<NotificationDraft>>> build, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ProjectHubDbContext>();
            var drafts = (await build(db)).Where(d => d.RecipientId != actorId).DistinctBy(d => (d.RecipientId, d.Type, d.ResourceId)).ToList();
            if (drafts.Count == 0)
            {
                return;
            }

            var recipientIds = drafts.Select(d => d.RecipientId).Distinct().ToList();
            var recipients = await db.Set<AppUser>().AsNoTracking()
                .Where(u => u.OrganizationId == organizationId && u.Status == UserStatus.Active && recipientIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, ct);
            var preferences = await db.Set<NotificationPreference>().AsNoTracking()
                .Where(p => p.OrganizationId == organizationId && recipientIds.Contains(p.UserId))
                .ToDictionaryAsync(p => p.UserId, ct);

            var now = clock.GetUtcNow();
            var mails = 0;
            var inApp = new HashSet<Guid>();
            foreach (var draft in drafts.Where(d => recipients.ContainsKey(d.RecipientId)))
            {
                var preference = preferences.GetValueOrDefault(draft.RecipientId) ?? new NotificationPreference();
                if (preference.InAppEnabled)
                {
                    db.Set<Notification>().Add(new Notification
                    {
                        Id = Guid.CreateVersion7(),
                        OrganizationId = organizationId,
                        UserId = draft.RecipientId,
                        Type = draft.Type,
                        Title = draft.Title,
                        Body = Excerpt(draft.Body),
                        ResourceType = draft.ResourceType,
                        ResourceId = draft.ResourceId,
                        CreatedAt = now,
                    });
                    inApp.Add(draft.RecipientId);
                }

                if (preference.EmailEnabled)
                {
                    // Mails carry no content beyond the title, so mailboxes never keep what access rules may later hide.
                    db.Set<MailOutboxEntry>().Add(new MailOutboxEntry
                    {
                        Id = Guid.CreateVersion7(),
                        OrganizationId = organizationId,
                        RecipientId = draft.RecipientId,
                        ToAddress = recipients[draft.RecipientId].Email,
                        Subject = draft.Title,
                        Body = $"{draft.Title}\n\nIn ProjectHub öffnen: {options.AppUrl}",
                        NextAttemptAt = now,
                        CreatedAt = now,
                    });
                    mails++;
                }

                if (preference.WebexEnabled && channels.WebexAvailable)
                {
                    // Same rule as mails: title and link only (ADR 0011).
                    db.Set<MailOutboxEntry>().Add(new MailOutboxEntry
                    {
                        Id = Guid.CreateVersion7(),
                        OrganizationId = organizationId,
                        RecipientId = draft.RecipientId,
                        Channel = MailOutboxChannel.Webex,
                        ToAddress = recipients[draft.RecipientId].Email,
                        Subject = draft.Title,
                        Body = $"**{WebexMarkdown(draft.Title)}**\n\n[In ProjectHub öffnen]({options.AppUrl})",
                        NextAttemptAt = now,
                        CreatedAt = now,
                    });
                    mails++;
                }
            }

            await db.SaveChangesAsync(ct);
            if (mails > 0)
            {
                outbox.Notify();
            }

            foreach (var userId in inApp)
            {
                var unread = await db.Set<Notification>().CountAsync(n => n.UserId == userId && n.ReadAt == null, ct);
                await PushAsync(userId, unread);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Delivering notifications in organization {OrganizationId} failed", organizationId);
        }
    }

    /// <summary>Tells the person's open clients the new unread count. Best effort, like all realtime messages.</summary>
    public async Task PushAsync(Guid userId, int unreadCount)
    {
        try
        {
            await hub.Clients.Group(NotificationsHub.GroupOf(userId)).NotificationsChanged(new NotificationsChangedMessage(unreadCount));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Realtime notification for user {UserId} failed", userId);
        }
    }

    /// <summary>Titles contain user text; Markdown characters are escaped so they cannot add links or formatting.</summary>
    public static string WebexMarkdown(string text)
    {
        var builder = new System.Text.StringBuilder(text.Length);
        foreach (var c in text)
        {
            if ("\\`*_{}[]()<>#+-.!|~".Contains(c, StringComparison.Ordinal))
            {
                builder.Append('\\');
            }

            builder.Append(char.IsControl(c) ? ' ' : c);
        }

        return builder.ToString();
    }

    public static string? Excerpt(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var collapsed = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length <= MaxBodyLength ? collapsed : collapsed[..(MaxBodyLength - 1)].TrimEnd() + "…";
    }
}
