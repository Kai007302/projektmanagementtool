using Microsoft.EntityFrameworkCore;
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

namespace ProjectHub.Api.Modules.Whiteboard;

public sealed record WhiteboardResponse(
    Guid Id,
    Guid ProjectId,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Version);

public sealed record CreateWhiteboardRequest(string? Name);

/// <summary>Live data of a task shown as a card on a whiteboard; never stored in the document.</summary>
public sealed record WhiteboardTask(Guid Id, string Title, string Status, string? AssigneeName, DateOnly? DueDate);

public sealed record TaskWhiteboard(Guid Id, string Name);

/// <summary>Whiteboards of a project: list, create, rename, delete, and the live data of their task cards.</summary>
public sealed class WhiteboardService(
    ProjectHubDbContext db,
    ProjectAccess projectAccess,
    WhiteboardDocumentStore documents,
    IAuditLog audit,
    IActivityLog activity,
    IDomainEventPublisher events,
    TimeProvider clock)
{
    public const int MaxNameLength = 200;
    public const int MaxTaskLookup = 100;

    public async Task<ServiceResult<PagedResponse<WhiteboardResponse>>> ListAsync(UserContext user, Guid projectId, Paging paging, CancellationToken ct)
    {
        if (await projectAccess.RequireAsync(user, projectId, ProjectPermission.View, ct) is { } failure)
        {
            return failure;
        }

        var rows = await db.Set<ProjectWhiteboard>()
            .Where(w => w.OrganizationId == user.OrganizationId && w.ProjectId == projectId)
            .OrderBy(w => w.Name).ThenBy(w => w.Id)
            .Skip(paging.Skip).Take(paging.Take + 1)
            .Select(w => new WhiteboardResponse(w.Id, w.ProjectId, w.Name, w.CreatedAt, w.UpdatedAt, w.Version))
            .ToListAsync(ct);
        return paging.ToPage(rows);
    }

    public async Task<ServiceResult<WhiteboardResponse>> CreateAsync(UserContext user, Guid projectId, CreateWhiteboardRequest request, CancellationToken ct)
    {
        if (await projectAccess.RequireAsync(user, projectId, ProjectPermission.Edit, ct) is { } failure)
        {
            return failure;
        }

        var name = request.Name?.Trim() ?? string.Empty;
        if (ValidateName(name) is { } invalid)
        {
            return invalid;
        }

        var now = clock.GetUtcNow();
        var id = Guid.CreateVersion7();
        var board = new ProjectWhiteboard
        {
            Id = id,
            OrganizationId = user.OrganizationId,
            ProjectId = projectId,
            Name = name,
            CollaborationDocumentId = $"yjs:{id:N}",
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
        db.Set<ProjectWhiteboard>().Add(board);
        activity.Record(user, projectId, ActivityActions.WhiteboardCreated, "whiteboard", board.Id, new { board.Name });
        await db.SaveChangesAsync(ct);
        await events.PublishAsync(new ProjectContentChanged(projectId, ProjectContentChanged.Whiteboards), ct);
        return ToResponse(board);
    }

    public async Task<ServiceResult<WhiteboardResponse>> GetAsync(UserContext user, Guid whiteboardId, CancellationToken ct)
    {
        var (board, failure) = await RequireAsync(user, whiteboardId, ProjectPermission.View, ct);
        return failure is null ? ToResponse(board!) : failure;
    }

    public async Task<ServiceResult<WhiteboardResponse>> UpdateAsync(UserContext user, Guid whiteboardId, PatchDocument patch, CancellationToken ct)
    {
        var (board, failure) = await RequireAsync(user, whiteboardId, ProjectPermission.Edit, ct, tracking: true);
        if (failure is not null)
        {
            return failure;
        }

        if (!patch.TryGetVersion(out var expectedVersion, out var versionError))
        {
            return versionError!;
        }

        if (board!.Version != expectedVersion)
        {
            return ServiceFailure.StaleVersion(board.Version);
        }

        var changed = new List<string>();
        if (!patch.TryApply<string>("name", v => board.Name = v?.Trim() ?? string.Empty, changed, out var error))
        {
            return error!;
        }

        if (ValidateName(board.Name) is { } invalid)
        {
            return invalid;
        }

        if (changed.Count > 0)
        {
            db.Touch(board, expectedVersion, clock.GetUtcNow());
            activity.Record(user, board.ProjectId, ActivityActions.WhiteboardRenamed, "whiteboard", board.Id, new { board.Name });
            if (await db.SaveVersionedAsync(board, ct) is { } conflict)
            {
                return conflict;
            }

            await events.PublishAsync(new ProjectContentChanged(board.ProjectId, ProjectContentChanged.Whiteboards), ct);
        }

        return ToResponse(board);
    }

    public async Task<ServiceResult<Done>> DeleteAsync(UserContext user, Guid whiteboardId, CancellationToken ct)
    {
        var (board, failure) = await RequireAsync(user, whiteboardId, ProjectPermission.Edit, ct, tracking: true);
        if (failure is not null)
        {
            return failure;
        }

        IReadOnlyList<string> keys;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            // Waits for a running compaction, so no snapshot is written for a whiteboard that no longer exists.
            await documents.LockAsync(board!.Id, ct);
            keys = await documents.SnapshotKeysAsync(board.Id, ct);
            db.Set<ProjectWhiteboard>().Remove(board);
            activity.Record(user, board.ProjectId, ActivityActions.WhiteboardDeleted, "whiteboard", board.Id, new { board.Name });
            audit.Record(user, AuditActions.WhiteboardDeleted, "whiteboard", board.Id, new { board.ProjectId, board.Name });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        await documents.DeleteFilesAsync(keys);
        await events.PublishAsync(new ProjectContentChanged(board.ProjectId, ProjectContentChanged.Whiteboards), ct);
        return Done.Value;
    }

    /// <summary>Live data of the given tasks, as far as they are active tasks of the whiteboard's project.</summary>
    public async Task<ServiceResult<IReadOnlyList<WhiteboardTask>>> TasksAsync(UserContext user, Guid whiteboardId, string? ids, CancellationToken ct)
    {
        var (board, failure) = await RequireAsync(user, whiteboardId, ProjectPermission.View, ct);
        if (failure is not null)
        {
            return failure;
        }

        var parsed = new List<Guid>();
        foreach (var part in (ids ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Guid.TryParse(part, out var id))
            {
                return ServiceFailure.Invalid("ids", "Must be a comma-separated list of task ids.");
            }

            parsed.Add(id);
        }

        parsed = parsed.Distinct().ToList();
        if (parsed.Count > MaxTaskLookup)
        {
            return ServiceFailure.Invalid("ids", $"At most {MaxTaskLookup} ids.");
        }

        var tasks = await (
                from task in db.Set<ProjectTask>()
                where parsed.Contains(task.Id) && task.OrganizationId == user.OrganizationId
                      && task.ProjectId == board!.ProjectId && task.DeletedAt == null
                join assignee in db.Set<AppUser>() on task.AssigneeId equals assignee.Id into assignees
                from assignee in assignees.DefaultIfEmpty()
                orderby task.Title, task.Id
                select new WhiteboardTask(task.Id, task.Title, task.Status, assignee == null ? null : assignee.DisplayName, task.DueDate))
            .ToListAsync(ct);
        return tasks;
    }

    /// <summary>Whiteboards that show the task as a card (as of their latest compaction).</summary>
    public async Task<ServiceResult<IReadOnlyList<TaskWhiteboard>>> WhiteboardsOfTaskAsync(UserContext user, Guid taskId, CancellationToken ct)
    {
        var task = await db.Set<ProjectTask>()
            .SingleOrDefaultAsync(t => t.Id == taskId && t.OrganizationId == user.OrganizationId && t.DeletedAt == null, ct);
        if (task is null || await projectAccess.RequireAsync(user, task.ProjectId, ProjectPermission.View, ct) is not null)
        {
            return ServiceFailure.NotFound("Task");
        }

        var boards = await db.Set<ProjectWhiteboard>()
            .Where(w => w.ProjectId == task.ProjectId && db.Set<WhiteboardReference>().Any(r => r.WhiteboardId == w.Id && r.TaskId == taskId))
            .OrderBy(w => w.Name).ThenBy(w => w.Id)
            .Take(MaxTaskLookup)
            .Select(w => new TaskWhiteboard(w.Id, w.Name))
            .ToListAsync(ct);
        return boards;
    }

    /// <summary>
    /// Finds a whiteboard of the caller's organization and checks the permission on its project.
    /// Whiteboards of projects the caller cannot see do not exist for them (404).
    /// </summary>
    public async Task<(ProjectWhiteboard? Board, ServiceFailure? Failure)> RequireAsync(
        UserContext user, Guid whiteboardId, ProjectPermission permission, CancellationToken ct, bool tracking = false)
    {
        var query = tracking ? db.Set<ProjectWhiteboard>().AsTracking() : db.Set<ProjectWhiteboard>().AsNoTracking();
        var board = await query.SingleOrDefaultAsync(w => w.Id == whiteboardId && w.OrganizationId == user.OrganizationId, ct);
        if (board is null || await projectAccess.RequireAsync(user, board.ProjectId, ProjectPermission.View, ct) is not null)
        {
            return (null, ServiceFailure.NotFound("Whiteboard"));
        }

        return permission == ProjectPermission.View
            ? (board, null)
            : (board, await projectAccess.RequireAsync(user, board.ProjectId, permission, ct));
    }

    private static ServiceFailure? ValidateName(string name) =>
        name.Length is 0 or > MaxNameLength
            ? ServiceFailure.Invalid("name", $"Required, at most {MaxNameLength} characters.")
            : null;

    private static WhiteboardResponse ToResponse(ProjectWhiteboard board) =>
        new(board.Id, board.ProjectId, board.Name, board.CreatedAt, board.UpdatedAt, board.Version);
}
