using Microsoft.EntityFrameworkCore;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Modules.Attachments;
using ProjectHub.Api.Modules.Audit;
using ProjectHub.Api.Modules.Calendar;
using ProjectHub.Api.Modules.Comments;
using ProjectHub.Api.Modules.Identity;
using ProjectHub.Api.Modules.Knowledge;
using ProjectHub.Api.Modules.Notifications;
using ProjectHub.Api.Modules.Organizations;
using ProjectHub.Api.Modules.Projects;
using ProjectHub.Api.Modules.Tasks;
using ProjectHub.Api.Modules.Departments;
using ProjectHub.Api.Modules.Users;
using ProjectHub.Api.Modules.Whiteboard;

namespace ProjectHub.Api.Modules.Privacy;

/// <summary>
/// Everything ProjectHub stores about the signed-in person (Art. 15 and 20 GDPR, ADR 0017), as one machine-readable
/// document. Content of other people (task descriptions, their comments) is left out; tasks and articles appear with
/// their title, because the person is assignee, creator or owner.
/// </summary>
public sealed record PersonalDataExport(
    DateTimeOffset ExportedAt,
    string Notice,
    ExportProfile Profile,
    IReadOnlyList<ExportMembership> ProjectMemberships,
    IReadOnlyList<ExportMembership> DepartmentMemberships,
    ExportNotificationPreferences NotificationPreferences,
    IReadOnlyList<ExportNotification> Notifications,
    IReadOnlyList<ExportMail> QueuedMessages,
    IReadOnlyList<ExportTask> TasksAssigned,
    IReadOnlyList<ExportTask> TasksCreated,
    IReadOnlyList<ExportComment> TaskComments,
    IReadOnlyList<ExportComment> KnowledgeComments,
    IReadOnlyList<ExportArticle> KnowledgeArticlesOwned,
    IReadOnlyList<ExportArticleVersion> KnowledgeVersionsWritten,
    IReadOnlyList<ExportAttachment> AttachmentsUploaded,
    IReadOnlyList<ExportWhiteboardEdits> WhiteboardEdits,
    IReadOnlyList<ExportLogEntry> ActivityEntries,
    IReadOnlyList<ExportLogEntry> AuditEntries,
    ExportCalendarFeed? CalendarFeed,
    IReadOnlyList<ExportProjectCalendarFeed> ProjectCalendarFeeds);

/// <summary>Whether the person has a calendar address; the address itself is not stored and cannot be exported.</summary>
public sealed record ExportCalendarFeed(DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt);

/// <summary>A calendar address for one project (ADR 0020); like <see cref="ExportCalendarFeed"/> without the address.</summary>
public sealed record ExportProjectCalendarFeed(Guid ProjectId, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt);

public sealed record ExportProfile(
    Guid Id, string DisplayName, string Email, string? Department, string Status, string OrganizationRole, string Organization,
    DateTimeOffset CreatedAt, DateTimeOffset? LastLoginAt);

public sealed record ExportMembership(Guid Id, string Name, string Role, DateTimeOffset Since);

public sealed record ExportNotificationPreferences(bool InApp, bool Email, bool Webex);

public sealed record ExportNotification(string Type, string Title, string? Body, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

public sealed record ExportMail(string Channel, string Subject, string Status, DateTimeOffset CreatedAt, DateTimeOffset? SentAt);

public sealed record ExportTask(
    Guid Id, Guid ProjectId, string Title, string Status, string Priority, DateOnly? StartDate, DateOnly? DueDate,
    DateTimeOffset CreatedAt, DateTimeOffset? DeletedAt);

public sealed record ExportComment(Guid Id, Guid ParentId, string Content, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? DeletedAt);

public sealed record ExportArticle(Guid Id, string Title, string Status, DateTimeOffset CreatedAt, DateTimeOffset? DeletedAt);

public sealed record ExportArticleVersion(Guid ArticleId, int VersionNumber, string? ChangeNote, DateTimeOffset CreatedAt);

public sealed record ExportAttachment(Guid Id, Guid TaskId, string FileName, long SizeBytes, DateTimeOffset CreatedAt);

public sealed record ExportWhiteboardEdits(Guid WhiteboardId, int Changes, DateTimeOffset LastChangeAt);

public sealed record ExportLogEntry(string Action, string? ResourceType, Guid? ResourceId, Guid? ProjectId, DateTimeOffset CreatedAt);

public sealed class PersonalDataExportService(ProjectHubDbContext db, IAuditLog audit, TimeProvider clock)
{
    public const string Notice =
        "Diese Datei enthält die personenbezogenen Daten, die ProjectHub zu Ihrer Person speichert (Art. 15 und 20 DSGVO). " +
        "Daten in Microsoft Entra ID, Microsoft 365 und Webex sind nicht enthalten; dafür ist der Betreiber der jeweiligen Dienste zuständig.";

    public async Task<PersonalDataExport> ExportAsync(UserContext user, CancellationToken ct)
    {
        var (org, me) = (user.OrganizationId, user.UserId);

        var profile = await (
                from u in db.Set<AppUser>().AsNoTracking()
                join o in db.Set<Organization>().AsNoTracking() on u.OrganizationId equals o.Id
                where u.Id == me && u.OrganizationId == org
                select new ExportProfile(u.Id, u.DisplayName, u.Email, u.Department, u.Status, u.OrganizationRole, o.Name, u.CreatedAt, u.LastLoginAt))
            .SingleAsync(ct);

        var projects = await (
                from m in db.Set<ProjectMember>().AsNoTracking()
                join p in db.Set<Project>().AsNoTracking() on m.ProjectId equals p.Id
                where m.OrganizationId == org && m.UserId == me
                orderby m.CreatedAt
                select new ExportMembership(p.Id, p.Name, m.Role, m.CreatedAt))
            .ToListAsync(ct);

        var departments = await (
                from m in db.Set<DepartmentMember>().AsNoTracking()
                join d in db.Set<Department>().AsNoTracking() on m.DepartmentId equals d.Id
                where m.OrganizationId == org && m.UserId == me
                orderby m.CreatedAt
                select new ExportMembership(d.Id, d.Name, m.Role, m.CreatedAt))
            .ToListAsync(ct);

        var preferences = await db.Set<NotificationPreference>().AsNoTracking()
            .Where(p => p.OrganizationId == org && p.UserId == me)
            .Select(p => new ExportNotificationPreferences(p.InAppEnabled, p.EmailEnabled, p.WebexEnabled))
            .SingleOrDefaultAsync(ct) ?? new ExportNotificationPreferences(true, true, false);

        var notifications = await db.Set<Notification>().AsNoTracking()
            .Where(n => n.OrganizationId == org && n.UserId == me)
            .OrderBy(n => n.CreatedAt)
            .Select(n => new ExportNotification(n.Type, n.Title, n.Body, n.CreatedAt, n.ReadAt))
            .ToListAsync(ct);

        var mails = await db.Set<MailOutboxEntry>().AsNoTracking()
            .Where(m => m.OrganizationId == org && m.RecipientId == me)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new ExportMail(m.Channel, m.Subject, m.Status, m.CreatedAt, m.SentAt))
            .ToListAsync(ct);

        var tasks = db.Set<ProjectTask>().AsNoTracking().Where(t => t.OrganizationId == org);
        var assigned = await ToExport(tasks.Where(t => t.AssigneeId == me)).ToListAsync(ct);
        var created = await ToExport(tasks.Where(t => t.CreatorId == me)).ToListAsync(ct);

        var taskComments = await db.Set<TaskComment>().AsNoTracking()
            .Where(c => c.OrganizationId == org && c.AuthorId == me)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new ExportComment(c.Id, c.TaskId, c.Content, c.CreatedAt, c.UpdatedAt, c.DeletedAt))
            .ToListAsync(ct);

        var knowledgeComments = await db.Set<KnowledgeComment>().AsNoTracking()
            .Where(c => c.OrganizationId == org && c.AuthorId == me)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new ExportComment(c.Id, c.ArticleId, c.Content, c.CreatedAt, c.UpdatedAt, c.DeletedAt))
            .ToListAsync(ct);

        var articles = await db.Set<KnowledgeArticle>().AsNoTracking()
            .Where(a => a.OrganizationId == org && a.OwnerId == me)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new ExportArticle(a.Id, a.Title, a.Status, a.CreatedAt, a.DeletedAt))
            .ToListAsync(ct);

        var versions = await db.Set<KnowledgeVersion>().AsNoTracking()
            .Where(v => v.OrganizationId == org && v.CreatedBy == me)
            .OrderBy(v => v.CreatedAt)
            .Select(v => new ExportArticleVersion(v.ArticleId, v.VersionNumber, v.ChangeNote, v.CreatedAt))
            .ToListAsync(ct);

        var attachments = await db.Set<TaskAttachment>().AsNoTracking()
            .Where(a => a.OrganizationId == org && a.UploadedBy == me)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new ExportAttachment(a.Id, a.TaskId, a.FileName, a.SizeBytes, a.CreatedAt))
            .ToListAsync(ct);

        var whiteboardEdits = await db.Set<WhiteboardUpdate>().AsNoTracking()
            .Where(u => u.OrganizationId == org && u.CreatedBy == me)
            .GroupBy(u => u.WhiteboardId)
            .Select(g => new ExportWhiteboardEdits(g.Key, g.Count(), g.Max(u => u.CreatedAt)))
            .ToListAsync(ct);

        var activity = await db.Set<ActivityLogEntry>().AsNoTracking()
            .Where(a => a.OrganizationId == org && a.ActorId == me)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new ExportLogEntry(a.Action, a.ResourceType, a.ResourceId, a.ProjectId, a.CreatedAt))
            .ToListAsync(ct);

        var auditEntries = await db.Set<AuditLogEntry>().AsNoTracking()
            .Where(a => a.OrganizationId == org && a.ActorId == me)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new ExportLogEntry(a.Action, a.ResourceType, a.ResourceId, null, a.CreatedAt))
            .ToListAsync(ct);

        var calendarFeed = await db.Set<CalendarFeed>().AsNoTracking()
            .Where(f => f.OrganizationId == org && f.UserId == me && f.ProjectId == null)
            .Select(f => new ExportCalendarFeed(f.CreatedAt, f.LastUsedAt))
            .SingleOrDefaultAsync(ct);

        var projectCalendarFeeds = await db.Set<CalendarFeed>().AsNoTracking()
            .Where(f => f.OrganizationId == org && f.UserId == me && f.ProjectId != null)
            .OrderBy(f => f.CreatedAt)
            .Select(f => new ExportProjectCalendarFeed(f.ProjectId!.Value, f.CreatedAt, f.LastUsedAt))
            .ToListAsync(ct);

        // Accountability (Art. 5 (2) GDPR): who exported when, without content.
        audit.Record(user, AuditActions.PersonalDataExported, "app_user", me);
        await db.SaveChangesAsync(ct);

        return new PersonalDataExport(
            clock.GetUtcNow(), Notice, profile, projects, departments, preferences, notifications, mails, assigned, created,
            taskComments, knowledgeComments, articles, versions, attachments, whiteboardEdits, activity, auditEntries, calendarFeed, projectCalendarFeeds);
    }

    private static IQueryable<ExportTask> ToExport(IQueryable<ProjectTask> tasks) =>
        tasks
            .OrderBy(t => t.CreatedAt)
            .Select(t => new ExportTask(t.Id, t.ProjectId, t.Title, t.Status, t.Priority, t.StartDate, t.DueDate, t.CreatedAt, t.DeletedAt));
}
