using Microsoft.EntityFrameworkCore;
using ProjectHub.Api.Modules.Identity;
using ProjectHub.Api.Modules.Identity.Authorization;
using ProjectHub.Api.Modules.Notifications;
using ProjectHub.Api.Modules.Users;

namespace ProjectHub.Api.Modules.Departments;

/// <summary>
/// Someone signed in for the first time and no Entra group put them into a department: organization admins and all
/// department leads hear about it, so one of them takes the person in (ADR 0021).
/// </summary>
internal sealed class DepartmentOnboarding(NotificationDispatcher dispatcher)
{
    public Task NotifyIfUnassignedAsync(UserContext newcomer, CancellationToken ct) =>
        dispatcher.DispatchAsync(newcomer.OrganizationId, newcomer.UserId, async db =>
        {
            if (await db.Set<DepartmentMember>().AnyAsync(m => m.UserId == newcomer.UserId, ct))
            {
                return [];
            }

            var recipients = await db.Set<AppUser>().AsNoTracking()
                .Where(u => u.OrganizationId == newcomer.OrganizationId && u.Status == UserStatus.Active
                            && (u.OrganizationRole == OrganizationRole.Admin
                                || db.Set<DepartmentMember>().Any(m => m.UserId == u.Id && m.Role == DepartmentRole.Lead)))
                .Select(u => u.Id)
                .ToListAsync(ct);

            return recipients
                .Select(id => new NotificationDraft(
                    id, NotificationTypes.PersonWithoutDepartment, $"{newcomer.DisplayName} wartet auf eine Abteilung", null,
                    NotificationResources.User, newcomer.UserId))
                .ToList();
        }, ct);
}
