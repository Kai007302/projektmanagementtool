using Microsoft.EntityFrameworkCore;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Infrastructure.Outcomes;
using ProjectHub.Api.Modules.Audit;
using ProjectHub.Api.Modules.Calendar;
using ProjectHub.Api.Modules.Identity;
using ProjectHub.Api.Modules.Identity.Authorization;
using ProjectHub.Api.Modules.Knowledge;
using ProjectHub.Api.Modules.Notifications;
using ProjectHub.Api.Modules.Projects;
using ProjectHub.Api.Modules.Tasks;
using ProjectHub.Api.Modules.Teams;
using ProjectHub.Api.Modules.Users;

namespace ProjectHub.Api.Modules.Privacy;

/// <summary>
/// Erasure of a person (Art. 17 GDPR, ADR 0017) by an organization admin. The user row stays, so tasks, comments,
/// versions and log entries keep a neutral author ("Ehemalige Person"); everything that identifies the person or
/// only exists for them is overwritten or deleted. What other people wrote about the person is not touched.
/// </summary>
public sealed class UserAnonymizationService(ProjectHubDbContext db, IAuditLog audit, TimeProvider clock)
{
    public const string AnonymizedName = "Ehemalige Person";

    public static string AnonymizedEmail(Guid userId) => $"anonymized-{userId:N}@invalid";

    public static string AnonymizedObjectId(Guid userId) => $"anonymized:{userId:N}";

    public async Task<ServiceResult<Done>> AnonymizeAsync(UserContext admin, Guid userId, CancellationToken ct)
    {
        if (!admin.IsOrganizationAdmin)
        {
            return ServiceFailure.Forbidden("Only organization admins can anonymize people.");
        }

        if (userId == admin.UserId)
        {
            // Keeps the organization from losing its last admin; another admin has to do it.
            return ServiceFailure.Invalid("userId", "You cannot anonymize yourself.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var user = await db.Set<AppUser>().SingleOrDefaultAsync(u => u.Id == userId && u.OrganizationId == admin.OrganizationId, ct);
        if (user is null)
        {
            return ServiceFailure.NotFound("User");
        }

        if (user.AnonymizedAt is not null)
        {
            return Done.Value;
        }

        var (org, now) = (admin.OrganizationId, clock.GetUtcNow());
        user.DisplayName = AnonymizedName;
        user.Email = AnonymizedEmail(userId);
        user.EntraObjectId = AnonymizedObjectId(userId);
        user.Department = null;
        user.Status = UserStatus.Inactive;
        user.OrganizationRole = OrganizationRole.Member;
        user.LastLoginAt = null;
        user.AnonymizedAt = now;
        user.UpdatedAt = now;
        user.Version++;

        await db.Set<Notification>().Where(n => n.OrganizationId == org && n.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Set<NotificationPreference>().Where(p => p.OrganizationId == org && p.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Set<MailOutboxEntry>().Where(m => m.OrganizationId == org && m.RecipientId == userId).ExecuteDeleteAsync(ct);
        await db.Set<CalendarFeed>().Where(f => f.OrganizationId == org && f.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Set<TeamMember>().Where(m => m.OrganizationId == org && m.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Set<ProjectMember>().Where(m => m.OrganizationId == org && m.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Set<KnowledgePermission>()
            .Where(p => p.OrganizationId == org && p.PrincipalType == "user" && p.PrincipalId == userId)
            .ExecuteDeleteAsync(ct);

        // Open work must not stay with someone who is gone; the project sees the task as unassigned.
        await db.Set<ProjectTask>()
            .Where(t => t.OrganizationId == org && t.AssigneeId == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.AssigneeId, (Guid?)null)
                .SetProperty(t => t.UpdatedAt, now)
                .SetProperty(t => t.Version, t => t.Version + 1), ct);
        await db.Set<KnowledgeArticle>()
            .Where(a => a.OrganizationId == org && a.OwnerId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.OwnerId, (Guid?)null), ct);
        await db.Set<KnowledgeSpace>()
            .Where(k => k.OrganizationId == org && k.OwnerId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(k => k.OwnerId, (Guid?)null), ct);

        audit.Record(admin, AuditActions.UserAnonymized, "app_user", userId);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Done.Value;
    }
}
