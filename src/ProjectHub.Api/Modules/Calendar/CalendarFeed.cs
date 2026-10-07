using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Infrastructure.Outcomes;
using ProjectHub.Api.Modules.Audit;
using ProjectHub.Api.Modules.Gantt;
using ProjectHub.Api.Modules.Identity;
using ProjectHub.Api.Modules.Identity.Authorization;
using ProjectHub.Api.Modules.Notifications;
using ProjectHub.Api.Modules.Projects;
using ProjectHub.Api.Modules.Tasks;
using ProjectHub.Api.Modules.Users;

namespace ProjectHub.Api.Modules.Calendar;

/// <summary>
/// The secret address of a person's subscribed calendar; only the hash of its token is kept. Without a project it holds
/// the person's own dates, with one the dates of that project (ADR 0020).
/// </summary>
public sealed class CalendarFeed
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public Guid UserId { get; init; }
    public Guid? ProjectId { get; init; }
    public required byte[] TokenHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
}

internal sealed class CalendarFeedConfiguration : IEntityTypeConfiguration<CalendarFeed>
{
    public void Configure(EntityTypeBuilder<CalendarFeed> builder) => builder.ToTable("calendar_feed");
}

public sealed record CalendarFeedStatus(bool Active, DateTimeOffset? CreatedAt, DateTimeOffset? LastUsedAt);

/// <summary>The feed address; returned only when it is created, because it cannot be read back later.</summary>
public sealed record CalendarFeedCreated(string Url, DateTimeOffset CreatedAt);

/// <summary>
/// A subscribable calendar of the person's own dates (ADR 0018): tasks assigned to them with a start or due date, and
/// milestones of the projects they are a member of. Calendar programs fetch it without signing in, so the address
/// carries a random token; access to every entry is checked again on each fetch, as for the person themselves.
/// </summary>
public sealed class CalendarFeedService(
    ProjectHubDbContext db,
    ProjectAccess projectAccess,
    IProjectHubAuthorization authorization,
    IAuditLog audit,
    NotificationOptions app,
    TimeProvider clock)
{
    public const string FeedPath = "/api/v1/calendar-feed.ics";
    public const int MaxEntries = 1_000;

    /// <summary>Entries from this many days ago on; older ones are of no use in a calendar that refreshes.</summary>
    public const int PastDays = 90;

    private const int TokenBytes = 32;

    /// <summary>The last-used time is written at most this often, not on every refresh.</summary>
    private static readonly TimeSpan UsageResolution = TimeSpan.FromMinutes(15);

    public Task<CalendarFeedStatus> StatusAsync(UserContext user, CancellationToken ct) => StatusOfAsync(user, null, ct);

    public async Task<ServiceResult<CalendarFeedStatus>> ProjectStatusAsync(UserContext user, Guid projectId, CancellationToken ct)
    {
        if (await projectAccess.RequireAsync(user, projectId, ProjectPermission.View, ct) is { } failure)
        {
            return failure;
        }

        return await StatusOfAsync(user, projectId, ct);
    }

    private async Task<CalendarFeedStatus> StatusOfAsync(UserContext user, Guid? projectId, CancellationToken ct)
    {
        var feed = await Own(user, projectId).AsNoTracking().SingleOrDefaultAsync(ct);
        return new CalendarFeedStatus(feed is not null, feed?.CreatedAt, feed?.LastUsedAt);
    }

    /// <summary>Creates the address, or replaces it: the previous address stops working at once.</summary>
    public Task<CalendarFeedCreated> CreateAsync(UserContext user, CancellationToken ct) => CreateForAsync(user, null, ct);

    /// <summary>The address of one project's calendar; replacing it leaves the person's other addresses working.</summary>
    public async Task<ServiceResult<CalendarFeedCreated>> CreateForProjectAsync(UserContext user, Guid projectId, CancellationToken ct)
    {
        if (await projectAccess.RequireAsync(user, projectId, ProjectPermission.View, ct) is { } failure)
        {
            return failure;
        }

        return await CreateForAsync(user, projectId, ct);
    }

    private async Task<CalendarFeedCreated> CreateForAsync(UserContext user, Guid? projectId, CancellationToken ct)
    {
        var token = Base64Url(RandomNumberGenerator.GetBytes(TokenBytes));
        var now = clock.GetUtcNow();
        var feed = await Own(user, projectId).SingleOrDefaultAsync(ct);
        var replaced = feed is not null;
        if (feed is null)
        {
            feed = new CalendarFeed
            {
                Id = Guid.CreateVersion7(), OrganizationId = user.OrganizationId, UserId = user.UserId, ProjectId = projectId, TokenHash = Hash(token),
            };
            db.Set<CalendarFeed>().Add(feed);
        }

        feed.TokenHash = Hash(token);
        feed.CreatedAt = now;
        feed.LastUsedAt = null;
        audit.Record(user, AuditActions.CalendarFeedCreated, "calendar_feed", feed.Id, new { Replaced = replaced, ProjectId = projectId });
        await db.SaveChangesAsync(ct);
        return new CalendarFeedCreated($"{app.AppUrl.TrimEnd('/')}{FeedPath}?token={token}", now);
    }

    public Task<ServiceResult<Done>> DeleteAsync(UserContext user, CancellationToken ct) => DeleteForAsync(user, null, ct);

    public async Task<ServiceResult<Done>> DeleteForProjectAsync(UserContext user, Guid projectId, CancellationToken ct)
    {
        if (await projectAccess.RequireAsync(user, projectId, ProjectPermission.View, ct) is { } failure)
        {
            return failure;
        }

        return await DeleteForAsync(user, projectId, ct);
    }

    private async Task<ServiceResult<Done>> DeleteForAsync(UserContext user, Guid? projectId, CancellationToken ct)
    {
        var feed = await Own(user, projectId).SingleOrDefaultAsync(ct);
        if (feed is null)
        {
            return ServiceFailure.NotFound("Calendar feed");
        }

        db.Set<CalendarFeed>().Remove(feed);
        audit.Record(user, AuditActions.CalendarFeedRevoked, "calendar_feed", feed.Id);
        await db.SaveChangesAsync(ct);
        return Done.Value;
    }

    /// <summary>The calendar behind a token; "not found" for unknown tokens and for people who are no longer active.</summary>
    public async Task<ServiceResult<byte[]>> FeedAsync(string? token, CancellationToken ct)
    {
        if (token is null || token.Length != 43 || !token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            return ServiceFailure.NotFound("Calendar feed");
        }

        var hash = Hash(token);
        var found = await (
                from feed in db.Set<CalendarFeed>()
                join u in db.Set<AppUser>() on new { feed.OrganizationId, feed.UserId } equals new { u.OrganizationId, UserId = u.Id }
                where feed.TokenHash == hash && u.Status == UserStatus.Active
                select new { Feed = feed, User = new UserContext(u.Id, u.OrganizationId, u.OrganizationRole, u.DisplayName, u.Email) })
            .SingleOrDefaultAsync(ct);
        if (found is null)
        {
            return ServiceFailure.NotFound("Calendar feed");
        }

        var now = clock.GetUtcNow();
        if (found.Feed.LastUsedAt is not { } lastUsed || now - lastUsed > UsageResolution)
        {
            found.Feed.LastUsedAt = now;
            await db.SaveChangesAsync(ct);
        }

        var since = DateOnly.FromDateTime(now.UtcDateTime).AddDays(-PastDays);
        if (found.Feed.ProjectId is not { } projectId)
        {
            return CalendarFile.Write(await EntriesAsync(found.User, since, ct), now, "ProjectHub – Meine Termine");
        }

        // A project calendar holds what the person could see in the project now; leaving the project ends it.
        if (await projectAccess.RequireAsync(found.User, projectId, ProjectPermission.View, ct) is not null)
        {
            return ServiceFailure.NotFound("Calendar feed");
        }

        var name = await db.Set<Project>().Where(p => p.Id == projectId).Select(p => p.Name).SingleAsync(ct);
        return CalendarFile.Write(await ProjectEntriesAsync(found.User, projectId, since, ct), now, $"ProjectHub – {name}");
    }

    /// <summary>All dated tasks and the milestones of one project; the caller has checked that the person may see it.</summary>
    private async Task<List<CalendarEntry>> ProjectEntriesAsync(UserContext user, Guid projectId, DateOnly since, CancellationToken ct)
    {
        var tasks = await db.Set<ProjectTask>().AsNoTracking()
            .Where(t => t.OrganizationId == user.OrganizationId && t.ProjectId == projectId && t.DeletedAt == null
                        && (t.StartDate != null || t.DueDate != null)
                        && (t.DueDate ?? t.StartDate) >= since)
            .OrderBy(t => t.StartDate ?? t.DueDate).ThenBy(t => t.Id)
            .Select(t => new { t.Id, t.Title, t.StartDate, t.DueDate, t.Version })
            .Take(MaxEntries)
            .ToListAsync(ct);
        var milestones = await db.Set<GanttMilestone>().AsNoTracking()
            .Where(m => m.OrganizationId == user.OrganizationId && m.ProjectId == projectId && m.MilestoneDate >= since)
            .OrderBy(m => m.MilestoneDate).ThenBy(m => m.Id)
            .Select(m => new { m.Id, m.Name, m.MilestoneDate, m.Version })
            .Take(MaxEntries)
            .ToListAsync(ct);

        return tasks
            .Select(t => new CalendarEntry(
                $"task-{t.Id}@projecthub", t.Version, t.Title, (t.StartDate ?? t.DueDate)!.Value, (t.DueDate ?? t.StartDate)!.Value, app.AppUrl))
            .Concat(milestones.Select(m => new CalendarEntry(
                $"milestone-{m.Id}@projecthub", m.Version, $"◆ {m.Name}", m.MilestoneDate, m.MilestoneDate, app.AppUrl)))
            .ToList();
    }

    private async Task<List<CalendarEntry>> EntriesAsync(UserContext user, DateOnly since, CancellationToken ct)
    {
        var activeProjects = db.Set<Project>().Where(p => p.OrganizationId == user.OrganizationId && p.DeletedAt == null);
        var memberships = db.Set<ProjectMember>().Where(m => m.OrganizationId == user.OrganizationId && m.UserId == user.UserId);

        // Tasks: assigned to the person, in projects they can still see (organization admins see all of them).
        var visibleProjects = authorization.VisibleProjects(user);
        var tasks = await (
                from task in db.Set<ProjectTask>().AsNoTracking()
                join project in visibleProjects on task.ProjectId equals project.Id
                where task.OrganizationId == user.OrganizationId && task.AssigneeId == user.UserId && task.DeletedAt == null
                      && (task.StartDate != null || task.DueDate != null)
                      && (task.DueDate ?? task.StartDate) >= since
                orderby task.StartDate ?? task.DueDate, task.Id
                select new { task.Id, task.Title, task.StartDate, task.DueDate, task.Version, Project = project.Name })
            .Take(MaxEntries)
            .ToListAsync(ct);

        // Milestones: of the projects the person is a member of (not every project an admin could open).
        var milestones = await (
                from milestone in db.Set<GanttMilestone>().AsNoTracking()
                join project in activeProjects.Where(p => memberships.Any(m => m.ProjectId == p.Id)) on milestone.ProjectId equals project.Id
                where milestone.OrganizationId == user.OrganizationId && milestone.MilestoneDate >= since
                orderby milestone.MilestoneDate, milestone.Id
                select new { milestone.Id, milestone.Name, milestone.MilestoneDate, milestone.Version, Project = project.Name })
            .Take(MaxEntries)
            .ToListAsync(ct);

        return tasks
            .Select(t => new CalendarEntry(
                $"task-{t.Id}@projecthub", t.Version, $"{t.Title} · {t.Project}", (t.StartDate ?? t.DueDate)!.Value, (t.DueDate ?? t.StartDate)!.Value, app.AppUrl))
            .Concat(milestones.Select(m => new CalendarEntry(
                $"milestone-{m.Id}@projecthub", m.Version, $"◆ {m.Name} · {m.Project}", m.MilestoneDate, m.MilestoneDate, app.AppUrl)))
            .ToList();
    }

    private IQueryable<CalendarFeed> Own(UserContext user, Guid? projectId) =>
        db.Set<CalendarFeed>().Where(f => f.OrganizationId == user.OrganizationId && f.UserId == user.UserId && f.ProjectId == projectId);

    private static byte[] Hash(string token) => SHA256.HashData(Encoding.ASCII.GetBytes(token));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
