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
    Guid? AssigneeId,
    string? AssigneeName,
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
    Guid? AssigneeId,
    Guid? ParentTaskId,
    DateOnly? StartDate,
    DateOnly? DueDate,
    short? Progress,
    decimal? EstimatedHours);

public sealed record TaskCounts(int Todo, int InProgress, int Done, int Overdue);

public sealed record TaskFilter(Guid? ParentTaskId, bool TopLevelOnly, string? Status, Guid? AssigneeId);

public sealed class TaskService(
    ProjectHubDbContext db,
    ProjectAccess access,
    IProjectHubAuthorization authorization,
    IAuditLog audit,
    IActivityLog activity,
    IDomainEventPublisher events,
    TimeProvider clock)
{
    public const int MaxTitleLength = 500;
    public const int MaxDescriptionLength = 20_000;

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
            tasks = tasks.Where(t => t.AssigneeId == assigneeId);
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
        var task = new ProjectTask
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = user.OrganizationId,
            ProjectId = projectId,
            ParentTaskId = request.ParentTaskId,
            Title = request.Title?.Trim() ?? string.Empty,
            Description = Normalize(request.Description),
            Status = request.Status ?? TaskStatus.Todo,
            Priority = request.Priority ?? TaskPriority.Normal,
            AssigneeId = request.AssigneeId,
            CreatorId = user.UserId,
            StartDate = request.StartDate,
            DueDate = request.DueDate,
            Progress = request.Progress ?? 0,
            EstimatedHours = request.EstimatedHours,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };

        if (await ValidateAsync(user, task, assigneeChanged: true, parentChanged: true, ct) is { } invalid)
        {
            return invalid;
        }

        db.Set<ProjectTask>().Add(task);
        activity.Record(user, projectId, ActivityActions.TaskCreated, "task", task.Id, new { task.Title, task.ParentTaskId });
        await db.SaveChangesAsync(ct);
        await events.PublishAsync(new ProjectContentChanged(projectId, ProjectContentChanged.Tasks), ct);
        if (task.AssigneeId is { } assigneeId)
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

        var previousAssignee = task.AssigneeId;
        var changed = new List<string>();
        if (!patch.TryApply<string>("title", v => task.Title = v?.Trim() ?? string.Empty, changed, out var error)
            || !patch.TryApply<string>("description", v => task.Description = Normalize(v), changed, out error)
            || !patch.TryApply<string>("status", v => task.Status = v ?? string.Empty, changed, out error)
            || !patch.TryApply<string>("priority", v => task.Priority = v ?? string.Empty, changed, out error)
            || !patch.TryApply<Guid?>("assigneeId", v => task.AssigneeId = v, changed, out error)
            || !patch.TryApply<Guid?>("parentTaskId", v => task.ParentTaskId = v, changed, out error)
            || !patch.TryApply<DateOnly?>("startDate", v => task.StartDate = v, changed, out error)
            || !patch.TryApply<DateOnly?>("dueDate", v => task.DueDate = v, changed, out error)
            || !patch.TryApply<short?>("progress", v => task.Progress = v ?? 0, changed, out error)
            || !patch.TryApply<decimal?>("estimatedHours", v => task.EstimatedHours = v, changed, out error))
        {
            return error!;
        }

        var invalid = await ValidateAsync(user, task, changed.Contains("assigneeId"), changed.Contains("parentTaskId"), ct);
        if (invalid is not null)
        {
            return invalid;
        }

        if (changed.Count > 0)
        {
            db.Touch(task, expectedVersion, clock.GetUtcNow());
            activity.Record(user, task.ProjectId, ActivityActions.TaskUpdated, "task", task.Id, new { task.Title, Fields = changed });
            if (await db.SaveVersionedAsync(task, ct) is { } conflict)
            {
                return conflict;
            }

            await events.PublishAsync(new ProjectContentChanged(task.ProjectId, ProjectContentChanged.Tasks), ct);
            if (changed.Contains("assigneeId") && task.AssigneeId is { } assigneeId && assigneeId != previousAssignee)
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
        if (await db.SaveVersionedAsync(task, ct) is { } conflict)
        {
            return conflict;
        }

        await events.PublishAsync(new ProjectContentChanged(task.ProjectId, ProjectContentChanged.Tasks), ct);
        return Done.Value;
    }

    private IQueryable<ProjectTask> ActiveTasks(UserContext user) =>
        db.Set<ProjectTask>().AsNoTracking().Where(t => t.OrganizationId == user.OrganizationId && t.DeletedAt == null);

    private IQueryable<TaskResponse> Project(IQueryable<ProjectTask> tasks) =>
        from task in tasks
        join assignee in db.Set<AppUser>() on task.AssigneeId equals assignee.Id into assignees
        from assignee in assignees.DefaultIfEmpty()
        select new TaskResponse(
            task.Id, task.ProjectId, task.ParentTaskId, task.Title, task.Description, task.Status, task.Priority,
            task.AssigneeId, assignee == null ? null : assignee.DisplayName, task.CreatorId, task.StartDate, task.DueDate,
            task.Progress, task.EstimatedHours,
            db.Set<ProjectTask>().Count(s => s.ParentTaskId == task.Id && s.DeletedAt == null),
            task.CreatedAt, task.UpdatedAt, task.Version);

    private async Task<ServiceFailure?> ValidateAsync(UserContext user, ProjectTask task, bool assigneeChanged, bool parentChanged, CancellationToken ct)
    {
        if (ValidateFields(task) is { } invalid)
        {
            return invalid;
        }

        if (assigneeChanged && task.AssigneeId is { } assigneeId
            && !await authorization.CanBeAssignedAsync(user.OrganizationId, task.ProjectId, assigneeId, ct))
        {
            return ServiceFailure.Invalid("assigneeId", "The user cannot be assigned tasks in this project.");
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

        if (task.Progress is < 0 or > 100)
        {
            return ServiceFailure.Invalid("progress", "Must be between 0 and 100.");
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
