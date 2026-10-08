using Microsoft.EntityFrameworkCore;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Infrastructure.Events;
using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Infrastructure.Outcomes;
using ProjectHub.Api.Modules.Audit;
using ProjectHub.Api.Modules.Identity;
using ProjectHub.Api.Modules.Identity.Authorization;
using ProjectHub.Api.Modules.Projects;
using ProjectHub.Api.Modules.Users;

namespace ProjectHub.Api.Modules.Tasks;

public sealed record TaskResponse(
    Guid Id,
    Guid ProjectId,
    Guid? ParentTaskId,
    string Title,
    string? Description,
    string Status,
    string Priority,
    IReadOnlyList<TaskPerson> Assignees,
    Guid CreatorId,
    DateOnly? StartDate,
    DateOnly? DueDate,
    short Progress,
    decimal? EstimatedHours,
    int SubtaskCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Version);

public sealed record CreateTaskRequest(
    string? Title,
    string? Description,
    string? Status,
    string? Priority,
    IReadOnlyList<Guid>? AssigneeIds,
    Guid? ParentTaskId,
    DateOnly? StartDate,
    DateOnly? DueDate,
    decimal? EstimatedHours);

/// <summary>Someone a task is assigned to, as shown with the task.</summary>
public sealed record TaskPerson(Guid Id, string DisplayName);

public sealed record TaskCounts(int Todo, int InProgress, int Done, int Overdue);

public sealed record TaskFilter(Guid? ParentTaskId, bool TopLevelOnly, string? Status, Guid? AssigneeId);

public sealed class TaskService(
    ProjectHubDbContext db,
    ProjectAccess access,
    IProjectHubAuthorization authorization,
    IAuditLog audit,
    IActivityLog activity,
    IDomainEventPublisher events,
    TaskProgress progress,
    TimeProvider clock)
{
    public const int MaxTitleLength = 500;
    public const int MaxDescriptionLength = 20_000;
    public const int MaxAssignees = 20;

    /// <summary>Guards against corrupt data when walking up the parent chain.</summary>
    private const int MaxHierarchyDepth = 100;

    public async Task<ServiceResult<IReadOnlyList<TaskResponse>>> ListAsync(
        UserContext user, Guid projectId, TaskFilter filter, Paging paging, CancellationToken ct)
    {
        if (await access.RequireAsync(user, projectId, ProjectPermission.View, ct) is { } failure)
        {
            return failure;
        }

        var tasks = ActiveTasks(user).Where(t => t.ProjectId == projectId);
        if (filter.ParentTaskId is { } parentId)
        {
            tasks = tasks.Where(t => t.ParentTaskId == parentId);
        }
        else if (filter.TopLevelOnly)
        {
            tasks = tasks.Where(t => t.ParentTaskId == null);
        }

        if (filter.Status is { } status)
        {
            tasks = tasks.Where(t => t.Status == status);
        }

        if (filter.AssigneeId is { } assigneeId)
        {
            tasks = tasks.Where(t => db.Set<TaskAssignee>().Any(a => a.TaskId == t.Id && a.UserId == assigneeId));
        }

        return await Project(tasks.OrderBy(t => t.CreatedAt).ThenBy(t => t.Id))
            .Skip(paging.Skip).Take(paging.Take + 1)
            .ToListAsync(ct);
    }

    /// <summary>Task counts of a project by status, plus open tasks past their due date (assistant overview).</summary>
    public async Task<TaskCounts> CountsAsync(UserContext user, Guid projectId, DateOnly today, CancellationToken ct)
    {
        if (await access.RequireAsync(user, projectId, ProjectPermission.View, ct) is not null)
        {
            return new TaskCounts(0, 0, 0, 0);
        }

        var tasks = ActiveTasks(user).Where(t => t.ProjectId == projectId);
        var byStatus = await tasks.GroupBy(t => t.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var overdue = await tasks.CountAsync(t => t.Status != TaskStatus.Done && t.DueDate != null && t.DueDate < today, ct);
        int Count(string status) => byStatus.SingleOrDefault(s => s.Key == status)?.Count ?? 0;
        return new TaskCounts(Count(TaskStatus.Todo), Count(TaskStatus.InProgress), Count(TaskStatus.Done), overdue);
    }

    public async Task<ServiceResult<TaskResponse>> GetAsync(UserContext user, Guid taskId, CancellationToken ct)
    {
        var projectId = await ActiveTasks(user).Where(t => t.Id == taskId).Select(t => (Guid?)t.ProjectId).SingleOrDefaultAsync(ct);
        if (projectId is null)
        {
            return ServiceFailure.NotFound("Task");
        }

        if (await access.RequireAsync(user, projectId.Value, ProjectPermission.View, ct) is not null)
        {
            return ServiceFailure.NotFound("Task");
        }

        return await Project(ActiveTasks(user).Where(t => t.Id == taskId)).SingleAsync(ct);
    }

    public async Task<ServiceResult<TaskResponse>> CreateAsync(UserContext user, Guid projectId, CreateTaskRequest request, CancellationToken ct)
    {
        if (await access.RequireAsync(user, projectId, ProjectPermission.Contribute, ct) is { } failure)
        {
            return failure;
        }

        var now = clock.GetUtcNow();
        var status = request.Status ?? TaskStatus.Todo;
        var task = new ProjectTask
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = user.OrganizationId,
            ProjectId = projectId,
            ParentTaskId = request.ParentTaskId,
            Title = request.Title?.Trim() ?? string.Empty,
            Description = Normalize(request.Description),
            Status = status,
            Priority = request.Priority ?? TaskPriority.Normal,
            CreatorId = user.UserId,
            StartDate = request.StartDate,
            DueDate = request.DueDate,
            Progress = TaskProgress.ForStatus(status),
            EstimatedHours = request.EstimatedHours,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };

        var assigneeIds = request.AssigneeIds ?? [];
        if (await ValidateAsync(user, task, assigneeIds, parentChanged: true, ct) is { } invalid)
        {
            return invalid;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.Set<ProjectTask>().Add(task);
        db.Set<TaskAssignee>().AddRange(assigneeIds.Distinct().Select(id => NewAssignee(user, task.Id, id, now)));
        activity.Record(user, projectId, ActivityActions.TaskCreated, "task", task.Id, new { task.Title, task.ParentTaskId });
        await db.SaveChangesAsync(ct);
        await progress.RecalculateAsync(user.OrganizationId, projectId, ct);
        await transaction.CommitAsync(ct);

        await events.PublishAsync(new ProjectContentChanged(projectId, ProjectContentChanged.Tasks), ct);
        foreach (var assigneeId in assigneeIds.Distinct())
        {
            await events.PublishAsync(new TaskAssigned(user.OrganizationId, projectId, task.Id, assigneeId, user.UserId), ct);
        }

        return await Project(ActiveTasks(user).Where(t => t.Id == task.Id)).SingleAsync(ct);
    }

    public async Task<ServiceResult<TaskResponse>> UpdateAsync(UserContext user, Guid taskId, PatchDocument patch, CancellationToken ct)
    {
        var task = await ActiveTasks(user).AsTracking().SingleOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null || await access.RequireAsync(user, task.ProjectId, ProjectPermission.View, ct) is not null)
        {
            return ServiceFailure.NotFound("Task");
        }

        if (await access.RequireAsync(user, task.ProjectId, ProjectPermission.Contribute, ct) is { } failure)
        {
            return failure;
        }

        if (!patch.TryGetVersion(out var expectedVersion, out var versionError))
        {
            return versionError!;
        }

        if (task.Version != expectedVersion)
        {
            return ServiceFailure.StaleVersion(task.Version);
        }

        if (patch.Has("progress"))
        {
            return ServiceFailure.Invalid("progress", "Calculated from the status and the subtasks; it cannot be set.");
        }

        if (patch.Has("assigneeId"))
        {
            return ServiceFailure.Invalid("assigneeId", "Use assigneeIds: a task can have several assignees.");
        }

        var previousAssignees = await db.Set<TaskAssignee>().AsNoTracking().Where(a => a.TaskId == task.Id).Select(a => a.UserId).ToListAsync(ct);
        IReadOnlyList<Guid> assigneeIds = previousAssignees;
        var changed = new List<string>();
        if (!patch.TryApply<string>("title", v => task.Title = v?.Trim() ?? string.Empty, changed, out var error)
            || !patch.TryApply<string>("description", v => task.Description = Normalize(v), changed, out error)
            || !patch.TryApply<string>("status", v => task.Status = v ?? string.Empty, changed, out error)
            || !patch.TryApply<string>("priority", v => task.Priority = v ?? string.Empty, changed, out error)
            || !patch.TryApply<List<Guid>>("assigneeIds", v => assigneeIds = v ?? [], changed, out error)
            || !patch.TryApply<Guid?>("parentTaskId", v => task.ParentTaskId = v, changed, out error)
            || !patch.TryApply<DateOnly?>("startDate", v => task.StartDate = v, changed, out error)
            || !patch.TryApply<DateOnly?>("dueDate", v => task.DueDate = v, changed, out error)
            || !patch.TryApply<decimal?>("estimatedHours", v => task.EstimatedHours = v, changed, out error))
        {
            return error!;
        }

        var added = assigneeIds.Distinct().Except(previousAssignees).ToList();
        var removed = previousAssignees.Except(assigneeIds).ToList();
        var invalid = await ValidateAsync(user, task, added, changed.Contains("parentTaskId"), ct, assigneeIds.Distinct().Count());
        if (invalid is not null)
        {
            return invalid;
        }

        if (changed.Contains("assigneeIds") && added.Count == 0 && removed.Count == 0)
        {
            changed.Remove("assigneeIds");
        }

        if (changed.Count > 0)
        {
            var now = clock.GetUtcNow();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            db.Touch(task, expectedVersion, now);
            db.Set<TaskAssignee>().AddRange(added.Select(id => NewAssignee(user, task.Id, id, now)));
            if (removed.Count > 0)
            {
                await db.Set<TaskAssignee>().Where(a => a.TaskId == task.Id && removed.Contains(a.UserId)).ExecuteDeleteAsync(ct);
            }

            activity.Record(user, task.ProjectId, ActivityActions.TaskUpdated, "task", task.Id, new { task.Title, Fields = changed });
            if (await db.SaveVersionedAsync(task, ct) is { } conflict)
            {
                return conflict;
            }

            if (changed.Contains("status") || changed.Contains("parentTaskId"))
            {
                await progress.RecalculateAsync(user.OrganizationId, task.ProjectId, ct);
            }

            await transaction.CommitAsync(ct);
            await events.PublishAsync(new ProjectContentChanged(task.ProjectId, ProjectContentChanged.Tasks), ct);
            foreach (var assigneeId in added)
            {
                await events.PublishAsync(new TaskAssigned(user.OrganizationId, task.ProjectId, task.Id, assigneeId, user.UserId), ct);
            }
        }

        return await Project(ActiveTasks(user).Where(t => t.Id == task.Id)).SingleAsync(ct);
    }

    /// <summary>Soft-deletes the task and all of its subtasks.</summary>
    public async Task<ServiceResult<Done>> DeleteAsync(UserContext user, Guid taskId, CancellationToken ct)
    {
        var task = await ActiveTasks(user).AsTracking().SingleOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null || await access.RequireAsync(user, task.ProjectId, ProjectPermission.View, ct) is not null)
        {
            return ServiceFailure.NotFound("Task");
        }

        if (await access.RequireAsync(user, task.ProjectId, ProjectPermission.Edit, ct) is { } failure)
        {
            return failure;
        }

        var now = clock.GetUtcNow();
        var deleted = new List<ProjectTask> { task };
        var frontier = new List<Guid> { task.Id };
        while (frontier.Count > 0)
        {
            var children = await ActiveTasks(user).AsTracking().Where(t => t.ParentTaskId != null && frontier.Contains(t.ParentTaskId.Value)).ToListAsync(ct);
            deleted.AddRange(children);
            frontier = children.Select(c => c.Id).ToList();
        }

        foreach (var item in deleted)
        {
            item.DeletedAt = now;
            db.Touch(item, item.Version, now);
        }

        activity.Record(user, task.ProjectId, ActivityActions.TaskDeleted, "task", task.Id, new { task.Title, Subtasks = deleted.Count - 1 });
        audit.Record(user, AuditActions.TaskDeleted, "task", task.Id, new { task.ProjectId, Subtasks = deleted.Count - 1 });
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await db.SaveVersionedAsync(task, ct) is { } conflict)
        {
            return conflict;
        }

        await progress.RecalculateAsync(user.OrganizationId, task.ProjectId, ct);
        await transaction.CommitAsync(ct);

        await events.PublishAsync(new ProjectContentChanged(task.ProjectId, ProjectContentChanged.Tasks), ct);
        return Done.Value;
    }

    private IQueryable<ProjectTask> ActiveTasks(UserContext user) =>
        db.Set<ProjectTask>().AsNoTracking().Where(t => t.OrganizationId == user.OrganizationId && t.DeletedAt == null);

    private IQueryable<TaskResponse> Project(IQueryable<ProjectTask> tasks) =>
        from task in tasks
        select new TaskResponse(
            task.Id, task.ProjectId, task.ParentTaskId, task.Title, task.Description, task.Status, task.Priority,
            db.Set<TaskAssignee>().Where(a => a.TaskId == task.Id)
                .Join(db.Set<AppUser>(), a => a.UserId, u => u.Id, (a, u) => u)
                .OrderBy(u => u.DisplayName).ThenBy(u => u.Id)
                .Select(u => new TaskPerson(u.Id, u.DisplayName))
                .ToList(),
            task.CreatorId, task.StartDate, task.DueDate,
            task.Progress, task.EstimatedHours,
            db.Set<ProjectTask>().Count(s => s.ParentTaskId == task.Id && s.DeletedAt == null),
            task.CreatedAt, task.UpdatedAt, task.Version);

    private static TaskAssignee NewAssignee(UserContext user, Guid taskId, Guid userId, DateTimeOffset now) =>
        new() { OrganizationId = user.OrganizationId, TaskId = taskId, UserId = userId, CreatedAt = now };

    /// <param name="newAssignees">People added to the task: they must be allowed to work in the project.</param>
    /// <param name="assigneeCount">Everyone the task is assigned to afterwards.</param>
    private async Task<ServiceFailure?> ValidateAsync(
        UserContext user, ProjectTask task, IReadOnlyCollection<Guid> newAssignees, bool parentChanged, CancellationToken ct, int? assigneeCount = null)
    {
        if (ValidateFields(task) is { } invalid)
        {
            return invalid;
        }

        if ((assigneeCount ?? newAssignees.Distinct().Count()) > MaxAssignees)
        {
            return ServiceFailure.Invalid("assigneeIds", $"At most {MaxAssignees} people.");
        }

        foreach (var assigneeId in newAssignees.Distinct())
        {
            if (!await authorization.CanBeAssignedAsync(user.OrganizationId, task.ProjectId, assigneeId, ct))
            {
                return ServiceFailure.Invalid("assigneeIds", "The user cannot be assigned tasks in this project.");
            }
        }

        if (parentChanged && task.ParentTaskId is { } parentId)
        {
            return await ValidateParentAsync(user, task, parentId, ct);
        }

        return null;
    }

    /// <summary>The checks that need no database: also used by the import (TaskImportService).</summary>
    public static ServiceFailure? ValidateFields(ProjectTask task)
    {
        if (task.Title.Length is 0 or > MaxTitleLength)
        {
            return ServiceFailure.Invalid("title", $"Required, at most {MaxTitleLength} characters.");
        }

        if (task.Description?.Length > MaxDescriptionLength)
        {
            return ServiceFailure.Invalid("description", $"At most {MaxDescriptionLength} characters.");
        }

        if (!TaskStatus.All.Contains(task.Status))
        {
            return ServiceFailure.Invalid("status", $"Must be one of: {string.Join(", ", TaskStatus.All)}.");
        }

        if (!TaskPriority.All.Contains(task.Priority))
        {
            return ServiceFailure.Invalid("priority", $"Must be one of: {string.Join(", ", TaskPriority.All)}.");
        }

        if (task.EstimatedHours is < 0)
        {
            return ServiceFailure.Invalid("estimatedHours", "Must not be negative.");
        }

        if (task.StartDate is { } start && task.DueDate is { } due && due < start)
        {
            return ServiceFailure.Invalid("dueDate", "Must not be before the start date.");
        }

        return null;
    }

    /// <summary>The parent must be an active task of the same project and must not be the task itself or one of its subtasks.</summary>
    private async Task<ServiceFailure?> ValidateParentAsync(UserContext user, ProjectTask task, Guid parentId, CancellationToken ct)
    {
        var invalid = ServiceFailure.Invalid("parentTaskId", "Must be another task of the same project, not one of its subtasks.");
        Guid? current = parentId;
        for (var depth = 0; current is { } id; depth++)
        {
            if (id == task.Id || depth > MaxHierarchyDepth)
            {
                return invalid;
            }

            var ancestor = await ActiveTasks(user)
                .Where(t => t.Id == id && t.ProjectId == task.ProjectId)
                .Select(t => new { t.ParentTaskId })
                .SingleOrDefaultAsync(ct);
            if (ancestor is null)
            {
                return invalid;
            }

            current = ancestor.ParentTaskId;
        }

        return null;
    }

    private static string? Normalize(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
