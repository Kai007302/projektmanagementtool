using Microsoft.EntityFrameworkCore;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Infrastructure.Outcomes;
using ProjectHub.Api.Modules.Gantt;
using ProjectHub.Api.Modules.Identity;
using ProjectHub.Api.Modules.Identity.Authorization;
using ProjectHub.Api.Modules.Notifications;
using ProjectHub.Api.Modules.Projects;
using ProjectHub.Api.Modules.Tasks;

namespace ProjectHub.Api.Modules.Calendar;

public sealed record CalendarDownload(string FileName, byte[] Content);

/// <summary>
/// Calendar entries for tasks and milestones (Vorschlag DEC-028). No calendar access through Graph: the person
/// imports the file into their own calendar. Like mails, entries carry only the title and a link.
/// </summary>
public sealed class CalendarService(
    ProjectHubDbContext db, TaskAccess taskAccess, ProjectAccess projectAccess, NotificationOptions app, TimeProvider clock)
{
    public async Task<ServiceResult<CalendarDownload>> TaskAsync(UserContext user, Guid taskId, CancellationToken ct)
    {
        var (_, failure) = await taskAccess.RequireAsync(user, taskId, ProjectPermission.View, ct);
        if (failure is not null)
        {
            return failure;
        }

        var task = await db.Set<ProjectTask>().SingleAsync(t => t.Id == taskId, ct);
        if ((task.StartDate ?? task.DueDate) is not { } first || (task.DueDate ?? task.StartDate) is not { } last)
        {
            return ServiceFailure.Invalid("dueDate", "The task has neither a start nor a due date.");
        }

        return Download(new CalendarEntry($"task-{task.Id}@projecthub", task.Version, task.Title, first, last, app.AppUrl), task.Title);
    }

    public async Task<ServiceResult<CalendarDownload>> MilestoneAsync(UserContext user, Guid milestoneId, CancellationToken ct)
    {
        var milestone = await db.Set<GanttMilestone>()
            .SingleOrDefaultAsync(m => m.Id == milestoneId && m.OrganizationId == user.OrganizationId, ct);
        if (milestone is null || await projectAccess.RequireAsync(user, milestone.ProjectId, ProjectPermission.View, ct) is not null)
        {
            return ServiceFailure.NotFound("Milestone");
        }

        return Download(
            new CalendarEntry($"milestone-{milestone.Id}@projecthub", milestone.Version, milestone.Name, milestone.MilestoneDate, milestone.MilestoneDate, app.AppUrl),
            milestone.Name);
    }

    private CalendarDownload Download(CalendarEntry entry, string title) =>
        new(CalendarFile.FileName(title), CalendarFile.Write(entry, clock.GetUtcNow()));
}

public static class CalendarEndpoints
{
    public static IServiceCollection AddCalendarModule(this IServiceCollection services) =>
        services.AddScoped<CalendarService>();

    public static RouteGroupBuilder MapCalendarEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/tasks/{id:guid}/calendar.ics", async (Guid id, UserContext user, CalendarService service, CancellationToken ct) =>
            ApiResults.From(await service.TaskAsync(user, id, ct), File));

        api.MapGet("/gantt-milestones/{id:guid}/calendar.ics", async (Guid id, UserContext user, CalendarService service, CancellationToken ct) =>
            ApiResults.From(await service.MilestoneAsync(user, id, ct), File));

        return api;
    }

    private static IResult File(CalendarDownload download) =>
        Results.File(download.Content, CalendarFile.ContentType, download.FileName);
}
