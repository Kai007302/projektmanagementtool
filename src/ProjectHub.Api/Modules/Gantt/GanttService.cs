using Microsoft.EntityFrameworkCore;
using Npgsql;
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

namespace ProjectHub.Api.Modules.Gantt;

public sealed record GanttTask(
    Guid Id,
    Guid? ParentTaskId,
    string Title,
    string Status,
    string? AssigneeName,
    DateOnly? StartDate,
    DateOnly? DueDate,
    short Progress,
    long Version);

public sealed record GanttDependency(Guid Id, Guid SourceTaskId, Guid TargetTaskId, string DependencyType, bool Violated);

public sealed record GanttMilestoneResponse(Guid Id, string Name, DateOnly Date, long Version);

public sealed record GanttResponse(
    Guid ProjectId,
    IReadOnlyList<GanttTask> Tasks,
    IReadOnlyList<GanttDependency> Dependencies,
    IReadOnlyList<GanttMilestoneResponse> Milestones);

public sealed record GanttPdfFile(string FileName, byte[] Content);

public sealed record CreateDependencyRequest(Guid? SourceTaskId, Guid? TargetTaskId, string? DependencyType);

public sealed record CreateMilestoneRequest(string? Name, DateOnly? Date);

/// <summary>
/// The Gantt chart is a view on the project's tasks: bars are tasks, dates are their start and due dates
/// (moved through the task API). It adds dependencies between tasks and milestones.
/// </summary>
public sealed class GanttService(
    ProjectHubDbContext db,
    ProjectAccess projectAccess,
    IAuditLog audit,
    IActivityLog activity,
    IDomainEventPublisher events,
    TimeProvider clock)
{
    public const int MaxMilestoneNameLength = 200;

    public async Task<ServiceResult<GanttResponse>> GetAsync(UserContext user, Guid projectId, CancellationToken ct)
    {
        if (await projectAccess.RequireAsync(user, projectId, ProjectPermission.View, ct) is { } failure)
        {
            return failure;
        }

        return await ReadAsync(user, projectId, ct);
    }

    /// <summary>The chart as PDF (A4 landscape) for printing and sending; same visibility as the chart itself.</summary>
    public async Task<ServiceResult<GanttPdfFile>> ExportPdfAsync(UserContext user, Guid projectId, CancellationToken ct)
    {
        if (await projectAccess.RequireAsync(user, projectId, ProjectPermission.View, ct) is { } failure)
        {
            return failure;
        }

        var gantt = await ReadAsync(user, projectId, ct);
        var name = await db.Set<Project>().AsNoTracking().Where(p => p.Id == projectId).Select(p => p.Name).SingleAsync(ct);
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        return new GanttPdfFile($"{FileNames.Safe(name, "projekt")} Gantt {today:yyyy-MM-dd}.pdf", GanttPdf.Render(name, gantt, today, now));
    }

    public async Task<ServiceResult<GanttDependency>> CreateDependencyAsync(
        UserContext user, Guid projectId, CreateDependencyRequest request, CancellationToken ct)
    {
        if (await projectAccess.RequireAsync(user, projectId, ProjectPermission.Contribute, ct) is { } failure)
        {
            return failure;
        }

        if (request.SourceTaskId is not { } sourceId)
        {
            return ServiceFailure.Invalid("sourceTaskId", "Required.");
        }

        if (request.TargetTaskId is not { } targetId)
        {
            return ServiceFailure.Invalid("targetTaskId", "Required.");
        }

        var type = request.DependencyType ?? DependencyTypes.FinishToStart;
        if (!DependencyTypes.All.Contains(type))
        {
            return ServiceFailure.Invalid("dependencyType", $"Must be one of: {string.Join(", ", DependencyTypes.All)}.");
        }

        if (sourceId == targetId)
        {
            return ServiceFailure.Invalid("targetTaskId", "A task cannot depend on itself.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(projectId, ct);

        var parents = await ActiveTasks(user, projectId).ToDictionaryAsync(t => t.Id, t => t.ParentTaskId, ct);
        if (!parents.ContainsKey(sourceId))
        {
            return ServiceFailure.Invalid("sourceTaskId", "Must be a task of this project.");
        }

        if (!parents.ContainsKey(targetId))
        {
            return ServiceFailure.Invalid("targetTaskId", "Must be a task of this project.");
        }

        if (GanttRules.IsAncestor(parents, sourceId, targetId) || GanttRules.IsAncestor(parents, targetId, sourceId))
        {
            return ServiceFailure.Invalid("targetTaskId", "A task cannot depend on its own parent or subtask.");
        }

        var edges = await Dependencies(user, projectId).Select(d => new { d.SourceTaskId, d.TargetTaskId }).ToListAsync(ct);
        if (edges.Any(e => (e.SourceTaskId == sourceId && e.TargetTaskId == targetId) || (e.SourceTaskId == targetId && e.TargetTaskId == sourceId)))
        {
            return ServiceFailure.Conflict("These tasks are already linked by a dependency.");
        }

        if (GanttRules.WouldCreateCycle(edges.Select(e => (e.SourceTaskId, e.TargetTaskId)), sourceId, targetId))
        {
            return ServiceFailure.Conflict("The dependency would create a cycle.");
        }

        var dependency = new TaskDependency
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = user.OrganizationId,
            SourceTaskId = sourceId,
            TargetTaskId = targetId,
            DependencyType = type,
            CreatedAt = clock.GetUtcNow(),
        };
        db.Set<TaskDependency>().Add(dependency);
        activity.Record(user, projectId, ActivityActions.DependencyAdded, "task_dependency", dependency.Id,
            new { SourceTaskId = sourceId, TargetTaskId = targetId, DependencyType = type });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Dependencies on soft-deleted tasks stay in the table and still count for uniqueness.
            return ServiceFailure.Conflict("These tasks are already linked by a dependency.");
        }

        await transaction.CommitAsync(ct);
        await events.PublishAsync(new ProjectContentChanged(projectId, ProjectContentChanged.Gantt), ct);
        var gantt = await ReadAsync(user, projectId, ct);
        return gantt.Dependencies.Single(d => d.Id == dependency.Id);
    }

    public async Task<ServiceResult<Done>> DeleteDependencyAsync(UserContext user, Guid dependencyId, CancellationToken ct)
    {
        var found = await (
                from dependency in db.Set<TaskDependency>().AsTracking()
                join target in db.Set<ProjectTask>() on dependency.TargetTaskId equals target.Id
                where dependency.Id == dependencyId && dependency.OrganizationId == user.OrganizationId && target.DeletedAt == null
                select new { Dependency = dependency, target.ProjectId })
            .SingleOrDefaultAsync(ct);
        if (found is null || await projectAccess.RequireAsync(user, found.ProjectId, ProjectPermission.View, ct) is not null)
        {
            return ServiceFailure.NotFound("Dependency");
        }

        if (await projectAccess.RequireAsync(user, found.ProjectId, ProjectPermission.Contribute, ct) is { } failure)
        {
            return failure;
        }

        db.Set<TaskDependency>().Remove(found.Dependency);
        activity.Record(user, found.ProjectId, ActivityActions.DependencyRemoved, "task_dependency", dependencyId,
            new { found.Dependency.SourceTaskId, found.Dependency.TargetTaskId });
        await db.SaveChangesAsync(ct);
        await events.PublishAsync(new ProjectContentChanged(found.ProjectId, ProjectContentChanged.Gantt), ct);
        return Done.Value;
    }

    public async Task<ServiceResult<GanttMilestoneResponse>> CreateMilestoneAsync(
        UserContext user, Guid projectId, CreateMilestoneRequest request, CancellationToken ct)
    {
        if (await projectAccess.RequireAsync(user, projectId, ProjectPermission.Edit, ct) is { } failure)
        {
            return failure;
        }

        if (request.Date is not { } date)
        {
            return ServiceFailure.Invalid("date", "Required.");
        }

        var now = clock.GetUtcNow();
        var milestone = new GanttMilestone
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = user.OrganizationId,
            ProjectId = projectId,
            Name = request.Name?.Trim() ?? string.Empty,
            MilestoneDate = date,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
        if (Validate(milestone) is { } invalid)
        {
            return invalid;
        }

        db.Set<GanttMilestone>().Add(milestone);
        activity.Record(user, projectId, ActivityActions.MilestoneCreated, "gantt_milestone", milestone.Id, new { milestone.Name, Date = date });
        await db.SaveChangesAsync(ct);
        await events.PublishAsync(new ProjectContentChanged(projectId, ProjectContentChanged.Gantt), ct);
        return ToResponse(milestone);
    }

    public async Task<ServiceResult<GanttMilestoneResponse>> UpdateMilestoneAsync(UserContext user, Guid milestoneId, PatchDocument patch, CancellationToken ct)
    {
        var (milestone, failure) = await RequireMilestoneAsync(user, milestoneId, ct);
        if (failure is not null)
        {
            return failure;
        }

        if (!patch.TryGetVersion(out var expectedVersion, out var versionError))
        {
            return versionError!;
        }

        if (milestone!.Version != expectedVersion)
        {
            return ServiceFailure.StaleVersion(milestone.Version);
        }

        var changed = new List<string>();
        DateOnly? date = milestone.MilestoneDate;
        if (!patch.TryApply<string>("name", v => milestone.Name = v?.Trim() ?? string.Empty, changed, out var error)
            || !patch.TryApply<DateOnly?>("date", v => date = v, changed, out error))
        {
            return error!;
        }

        if (date is not { } newDate)
        {
            return ServiceFailure.Invalid("date", "Required.");
        }

        milestone.MilestoneDate = newDate;
        if (Validate(milestone) is { } invalid)
        {
            return invalid;
        }

        if (changed.Count > 0)
        {
            db.Touch(milestone, expectedVersion, clock.GetUtcNow());
            activity.Record(user, milestone.ProjectId, ActivityActions.MilestoneUpdated, "gantt_milestone", milestone.Id, new { milestone.Name, Fields = changed });
            if (await db.SaveVersionedAsync(milestone, ct) is { } conflict)
            {
                return conflict;
            }

            await events.PublishAsync(new ProjectContentChanged(milestone.ProjectId, ProjectContentChanged.Gantt), ct);
        }

        return ToResponse(milestone);
    }

    public async Task<ServiceResult<Done>> DeleteMilestoneAsync(UserContext user, Guid milestoneId, CancellationToken ct)
    {
        var (milestone, failure) = await RequireMilestoneAsync(user, milestoneId, ct);
        if (failure is not null)
        {
            return failure;
        }

        db.Set<GanttMilestone>().Remove(milestone!);
        activity.Record(user, milestone!.ProjectId, ActivityActions.MilestoneDeleted, "gantt_milestone", milestone.Id, new { milestone.Name });
        audit.Record(user, AuditActions.MilestoneDeleted, "gantt_milestone", milestone.Id, new { milestone.ProjectId, milestone.Name });
        await db.SaveChangesAsync(ct);
        await events.PublishAsync(new ProjectContentChanged(milestone.ProjectId, ProjectContentChanged.Gantt), ct);
        return Done.Value;
    }

    private async Task<GanttResponse> ReadAsync(UserContext user, Guid projectId, CancellationToken ct)
    {
        var tasks = await (
                from task in ActiveTasks(user, projectId)
                join assignee in db.Set<AppUser>() on task.AssigneeId equals assignee.Id into assignees
                from assignee in assignees.DefaultIfEmpty()
                orderby task.CreatedAt, task.Id
                select new GanttTask(
                    task.Id, task.ParentTaskId, task.Title, task.Status, assignee == null ? null : assignee.DisplayName,
                    task.StartDate, task.DueDate, task.Progress, task.Version))
            .ToListAsync(ct);

        var spans = tasks.ToDictionary(t => t.Id, t => GanttRules.Span(t.StartDate, t.DueDate));
        var dependencies = (await Dependencies(user, projectId).OrderBy(d => d.CreatedAt).ThenBy(d => d.Id).ToListAsync(ct))
            .Select(d => new GanttDependency(
                d.Id, d.SourceTaskId, d.TargetTaskId, d.DependencyType,
                GanttRules.IsViolated(d.DependencyType, spans[d.SourceTaskId], spans[d.TargetTaskId])))
            .ToList();

        var milestones = await db.Set<GanttMilestone>()
            .Where(m => m.ProjectId == projectId && m.OrganizationId == user.OrganizationId)
            .OrderBy(m => m.MilestoneDate).ThenBy(m => m.Id)
            .Select(m => new GanttMilestoneResponse(m.Id, m.Name, m.MilestoneDate, m.Version))
            .ToListAsync(ct);

        return new GanttResponse(projectId, tasks, dependencies, milestones);
    }

    private IQueryable<ProjectTask> ActiveTasks(UserContext user, Guid projectId) =>
        db.Set<ProjectTask>().AsNoTracking()
            .Where(t => t.OrganizationId == user.OrganizationId && t.ProjectId == projectId && t.DeletedAt == null);

    /// <summary>Dependencies whose tasks are both active tasks of the project.</summary>
    private IQueryable<TaskDependency> Dependencies(UserContext user, Guid projectId) =>
        from dependency in db.Set<TaskDependency>().AsNoTracking()
        where ActiveTasks(user, projectId).Any(t => t.Id == dependency.SourceTaskId)
              && ActiveTasks(user, projectId).Any(t => t.Id == dependency.TargetTaskId)
        select dependency;

    /// <summary>Serializes dependency changes per project, so two concurrent links cannot form a cycle together.</summary>
    private Task LockAsync(Guid projectId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"select pg_advisory_xact_lock(hashtextextended({"gantt:" + projectId}, 0))", ct);

    /// <summary>Finds a milestone of the caller's organization and checks Edit on its project.</summary>
    private async Task<(GanttMilestone? Milestone, ServiceFailure? Failure)> RequireMilestoneAsync(UserContext user, Guid milestoneId, CancellationToken ct)
    {
        var milestone = await db.Set<GanttMilestone>().AsTracking()
            .SingleOrDefaultAsync(m => m.Id == milestoneId && m.OrganizationId == user.OrganizationId, ct);
        if (milestone is null || await projectAccess.RequireAsync(user, milestone.ProjectId, ProjectPermission.View, ct) is not null)
        {
            return (null, ServiceFailure.NotFound("Milestone"));
        }

        return (milestone, await projectAccess.RequireAsync(user, milestone.ProjectId, ProjectPermission.Edit, ct));
    }

    private static ServiceFailure? Validate(GanttMilestone milestone) =>
        milestone.Name.Length is 0 or > MaxMilestoneNameLength
            ? ServiceFailure.Invalid("name", $"Required, at most {MaxMilestoneNameLength} characters.")
            : null;

    private static GanttMilestoneResponse ToResponse(GanttMilestone milestone) =>
        new(milestone.Id, milestone.Name, milestone.MilestoneDate, milestone.Version);
}
