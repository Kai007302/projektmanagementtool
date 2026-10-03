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
using TaskStatuses = ProjectHub.Api.Modules.Tasks.TaskStatus;

namespace ProjectHub.Api.Modules.Kanban;

public sealed record KanbanCard(
    Guid Id,
    string Title,
    string Status,
    string Priority,
    Guid? AssigneeId,
    string? AssigneeName,
    DateOnly? DueDate,
    short Progress,
    int SubtaskCount,
    long Version);

public sealed record KanbanColumnResponse(Guid Id, string Name, string TaskStatus, int? WipLimit, long Version, IReadOnlyList<KanbanCard> Cards);

public sealed record KanbanBoardResponse(Guid Id, Guid ProjectId, string Name, IReadOnlyList<KanbanColumnResponse> Columns);

public sealed record CreateColumnRequest(string? Name, string? TaskStatus, int? WipLimit);

public sealed record MoveTaskRequest(long? Version, Guid? ColumnId, int? Index);

public sealed record MoveColumnRequest(int? Index);

public sealed class KanbanService(
    ProjectHubDbContext db,
    ProjectAccess projectAccess,
    TaskAccess taskAccess,
    IAuditLog audit,
    IActivityLog activity,
    IDomainEventPublisher events,
    TimeProvider clock)
{
    public const int MaxColumnNameLength = 100;

    /// <summary>Default columns of a new board, one per task status.</summary>
    private static readonly (string Name, string Status)[] DefaultColumns =
    [
        ("Offen", TaskStatuses.Todo),
        ("In Arbeit", TaskStatuses.InProgress),
        ("Erledigt", TaskStatuses.Done),
    ];

    public async Task<ServiceResult<KanbanBoardResponse>> GetAsync(UserContext user, Guid projectId, CancellationToken ct)
    {
        if (await projectAccess.RequireAsync(user, projectId, ProjectPermission.View, ct) is { } failure)
        {
            return failure;
        }

        return await ReadAsync(user, await EnsureBoardAsync(user, projectId, ct), ct);
    }

    /// <summary>Puts a task into a column at a position; the task takes over the column's status.</summary>
    public async Task<ServiceResult<KanbanBoardResponse>> MoveTaskAsync(UserContext user, Guid taskId, MoveTaskRequest request, CancellationToken ct)
    {
        var (projectId, failure) = await taskAccess.RequireAsync(user, taskId, ProjectPermission.Contribute, ct);
        if (failure is not null)
        {
            return failure;
        }

        if (request.Version is not { } expectedVersion)
        {
            return ServiceFailure.Invalid("version", "Required: the version the change is based on.");
        }

        if (request.ColumnId is not { } columnId)
        {
            return ServiceFailure.Invalid("columnId", "Required.");
        }

        if (request.Index is not { } index || index < 0)
        {
            return ServiceFailure.Invalid("index", "Required, at least 0.");
        }

        var board = await EnsureBoardAsync(user, projectId, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(board, ct);

        var columns = await Columns(board).ToListAsync(ct);
        var column = columns.SingleOrDefault(c => c.Id == columnId);
        if (column is null)
        {
            return ServiceFailure.Invalid("columnId", "Must be a column of this project's board.");
        }

        var task = await db.Set<ProjectTask>().AsTracking().SingleAsync(t => t.Id == taskId, ct);
        if (task.Version != expectedVersion)
        {
            return ServiceFailure.StaleVersion(task.Version);
        }

        var layout = BoardLayout.Arrange(Slots(columns), await PlacementsAsync(projectId, ct));
        var previousColumn = BoardLayout.ColumnOf(layout, taskId);
        var order = layout[column.Id].Where(c => c.TaskId != taskId).ToList();
        order.Insert(Math.Min(index, order.Count), new CardPlacement(taskId, column.TaskStatus, column.Id, null, task.CreatedAt));

        var now = clock.GetUtcNow();
        for (var i = 0; i < order.Count; i++)
        {
            var card = order[i];
            decimal position = i + 1;
            if (card.TaskId == taskId || (card.ColumnId == column.Id && card.Position == position))
            {
                continue;
            }

            // Re-numbering neighbours only touches their layout, so their version stays as it is.
            await db.Set<ProjectTask>().Where(t => t.Id == card.TaskId).ExecuteUpdateAsync(
                set => set.SetProperty(t => t.KanbanColumnId, column.Id).SetProperty(t => t.BoardPosition, position), ct);
        }

        task.KanbanColumnId = column.Id;
        task.BoardPosition = order.FindIndex(c => c.TaskId == taskId) + 1;
        if (task.Status != column.TaskStatus)
        {
            task.Status = column.TaskStatus;
            db.Touch(task, expectedVersion, now);
        }

        if (previousColumn != column.Id)
        {
            activity.Record(user, projectId, ActivityActions.TaskMoved, "task", task.Id, new { task.Title, Column = column.Name, task.Status });
        }

        if (await db.SaveVersionedAsync(task, ct) is { } conflict)
        {
            return conflict;
        }

        await transaction.CommitAsync(ct);
        await events.PublishAsync(new ProjectContentChanged(projectId, ProjectContentChanged.Tasks), ct);
        return await ReadAsync(user, board, ct);
    }

    public async Task<ServiceResult<KanbanBoardResponse>> CreateColumnAsync(UserContext user, Guid projectId, CreateColumnRequest request, CancellationToken ct)
    {
        if (await projectAccess.RequireAsync(user, projectId, ProjectPermission.Edit, ct) is { } failure)
        {
            return failure;
        }

        var board = await EnsureBoardAsync(user, projectId, ct);
        var now = clock.GetUtcNow();
        var column = new KanbanColumn
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = user.OrganizationId,
            BoardId = board,
            Name = request.Name?.Trim() ?? string.Empty,
            TaskStatus = request.TaskStatus ?? TaskStatuses.Todo,
            WipLimit = request.WipLimit,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };

        if (Validate(column) is { } invalid)
        {
            return invalid;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(board, ct);
        column.Position = (await Columns(board).MaxAsync(c => (decimal?)c.Position, ct) ?? 0) + 1;
        db.Set<KanbanColumn>().Add(column);
        activity.Record(user, projectId, ActivityActions.BoardColumnCreated, "kanban_column", column.Id, new { column.Name, column.TaskStatus });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return await BoardChangedAsync(user, projectId, board, ct);
    }

    public async Task<ServiceResult<KanbanBoardResponse>> UpdateColumnAsync(UserContext user, Guid columnId, PatchDocument patch, CancellationToken ct)
    {
        var (column, projectId, failure) = await RequireColumnAsync(user, columnId, ct);
        if (failure is not null)
        {
            return failure;
        }

        if (!patch.TryGetVersion(out var expectedVersion, out var versionError))
        {
            return versionError!;
        }

        if (column!.Version != expectedVersion)
        {
            return ServiceFailure.StaleVersion(column.Version);
        }

        var previousStatus = column.TaskStatus;
        var changed = new List<string>();
        if (!patch.TryApply<string>("name", v => column.Name = v?.Trim() ?? string.Empty, changed, out var error)
            || !patch.TryApply<string>("taskStatus", v => column.TaskStatus = v ?? string.Empty, changed, out error)
            || !patch.TryApply<int?>("wipLimit", v => column.WipLimit = v, changed, out error))
        {
            return error!;
        }

        if (Validate(column) is { } invalid)
        {
            return invalid;
        }

        if (changed.Count == 0)
        {
            return await ReadAsync(user, column.BoardId, ct);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(column.BoardId, ct);
        if (column.TaskStatus != previousStatus && await IsLastColumnOfStatusAsync(column.BoardId, column.Id, previousStatus, ct))
        {
            return LastColumnOfStatus;
        }

        db.Touch(column, expectedVersion, clock.GetUtcNow());
        activity.Record(user, projectId, ActivityActions.BoardColumnUpdated, "kanban_column", column.Id, new { column.Name, Fields = changed });
        if (await db.SaveVersionedAsync(column, ct) is { } conflict)
        {
            return conflict;
        }

        await transaction.CommitAsync(ct);
        return await BoardChangedAsync(user, projectId, column.BoardId, ct);
    }

    /// <summary>Moves a column to <paramref name="request"/>.Index among the board's columns.</summary>
    public async Task<ServiceResult<KanbanBoardResponse>> MoveColumnAsync(UserContext user, Guid columnId, MoveColumnRequest request, CancellationToken ct)
    {
        var (column, projectId, failure) = await RequireColumnAsync(user, columnId, ct);
        if (failure is not null)
        {
            return failure;
        }

        if (request.Index is not { } index || index < 0)
        {
            return ServiceFailure.Invalid("index", "Required, at least 0.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(column!.BoardId, ct);
        var columns = await Columns(column.BoardId).AsTracking().ToListAsync(ct);
        var moved = columns.Single(c => c.Id == columnId);
        columns.Remove(moved);
        columns.Insert(Math.Min(index, columns.Count), moved);
        for (var i = 0; i < columns.Count; i++)
        {
            columns[i].Position = i + 1;
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return await BoardChangedAsync(user, projectId, column.BoardId, ct);
    }

    /// <summary>Deletes a column; its cards fall back to the first column of their status.</summary>
    public async Task<ServiceResult<Done>> DeleteColumnAsync(UserContext user, Guid columnId, CancellationToken ct)
    {
        var (column, projectId, failure) = await RequireColumnAsync(user, columnId, ct);
        if (failure is not null)
        {
            return failure;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(column!.BoardId, ct);
        if (await IsLastColumnOfStatusAsync(column.BoardId, column.Id, column.TaskStatus, ct))
        {
            return LastColumnOfStatus;
        }

        // Includes soft-deleted tasks: the foreign key does not allow dangling references.
        await db.Set<ProjectTask>().Where(t => t.KanbanColumnId == columnId).ExecuteUpdateAsync(
            set => set.SetProperty(t => t.KanbanColumnId, (Guid?)null).SetProperty(t => t.BoardPosition, (decimal?)null), ct);
        db.Set<KanbanColumn>().Remove(column);
        activity.Record(user, projectId, ActivityActions.BoardColumnDeleted, "kanban_column", column.Id, new { column.Name });
        audit.Record(user, AuditActions.BoardColumnDeleted, "kanban_column", column.Id, new { ProjectId = projectId, column.Name });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await events.PublishAsync(new ProjectContentChanged(projectId, ProjectContentChanged.Board), ct);
        return Done.Value;
    }

    private static readonly ServiceFailure LastColumnOfStatus =
        ServiceFailure.Conflict("Every task status needs at least one column on the board.");

    /// <summary>Returns the board id of the project, creating the board with its default columns on first use.</summary>
    private async Task<Guid> EnsureBoardAsync(UserContext user, Guid projectId, CancellationToken ct)
    {
        if (await BoardIdAsync(projectId, ct) is { } existing)
        {
            return existing;
        }

        var boardId = Guid.CreateVersion7();
        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // A concurrent first request waits here on the unique project_id and then finds the board.
        var created = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             insert into kanban_board (id, organization_id, project_id, name, created_at, updated_at, version)
             values ({boardId}, {user.OrganizationId}, {projectId}, {"Board"}, {now}, {now}, 1)
             on conflict (project_id) do nothing
             """,
            ct);
        if (created == 1)
        {
            db.Set<KanbanColumn>().AddRange(DefaultColumns.Select((column, i) => new KanbanColumn
            {
                Id = Guid.CreateVersion7(),
                OrganizationId = user.OrganizationId,
                BoardId = boardId,
                Name = column.Name,
                TaskStatus = column.Status,
                Position = i + 1,
                CreatedAt = now,
                UpdatedAt = now,
                Version = 1,
            }));
            await db.SaveChangesAsync(ct);
        }

        await transaction.CommitAsync(ct);
        return await BoardIdAsync(projectId, ct) ?? throw new InvalidOperationException("Board was not created.");
    }

    private Task<Guid?> BoardIdAsync(Guid projectId, CancellationToken ct) =>
        db.Set<KanbanBoard>().Where(b => b.ProjectId == projectId).Select(b => (Guid?)b.Id).SingleOrDefaultAsync(ct);

    /// <summary>Serializes layout changes per board, so concurrent moves never interleave their numbering.</summary>
    private Task LockAsync(Guid boardId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"select 1 from kanban_board where id = {boardId} for update", ct);

    private IQueryable<KanbanColumn> Columns(Guid boardId) =>
        db.Set<KanbanColumn>().AsNoTracking().Where(c => c.BoardId == boardId).OrderBy(c => c.Position).ThenBy(c => c.Id);

    private static List<ColumnSlot> Slots(IEnumerable<KanbanColumn> columns) =>
        columns.Select(c => new ColumnSlot(c.Id, c.TaskStatus)).ToList();

    /// <summary>The board shows top-level tasks; subtasks appear as a count on their card.</summary>
    private IQueryable<ProjectTask> BoardTasks(Guid projectId) =>
        db.Set<ProjectTask>().AsNoTracking().Where(t => t.ProjectId == projectId && t.ParentTaskId == null && t.DeletedAt == null);

    private Task<List<CardPlacement>> PlacementsAsync(Guid projectId, CancellationToken ct) =>
        BoardTasks(projectId)
            .Select(t => new CardPlacement(t.Id, t.Status, t.KanbanColumnId, t.BoardPosition, t.CreatedAt))
            .ToListAsync(ct);

    private async Task<KanbanBoardResponse> ReadAsync(UserContext user, Guid boardId, CancellationToken ct)
    {
        var board = await db.Set<KanbanBoard>().AsNoTracking().SingleAsync(b => b.Id == boardId && b.OrganizationId == user.OrganizationId, ct);
        var columns = await Columns(boardId).ToListAsync(ct);
        var cards = await (
                from task in BoardTasks(board.ProjectId)
                join assignee in db.Set<AppUser>() on task.AssigneeId equals assignee.Id into assignees
                from assignee in assignees.DefaultIfEmpty()
                select new
                {
                    Placement = new CardPlacement(task.Id, task.Status, task.KanbanColumnId, task.BoardPosition, task.CreatedAt),
                    Card = new KanbanCard(
                        task.Id, task.Title, task.Status, task.Priority, task.AssigneeId,
                        assignee == null ? null : assignee.DisplayName, task.DueDate, task.Progress,
                        db.Set<ProjectTask>().Count(s => s.ParentTaskId == task.Id && s.DeletedAt == null),
                        task.Version),
                })
            .ToListAsync(ct);

        var byId = cards.ToDictionary(c => c.Card.Id, c => c.Card);
        var layout = BoardLayout.Arrange(Slots(columns), cards.Select(c => c.Placement));
        return new KanbanBoardResponse(
            board.Id,
            board.ProjectId,
            board.Name,
            columns.Select(c => new KanbanColumnResponse(
                c.Id, c.Name, c.TaskStatus, c.WipLimit, c.Version, layout[c.Id].Select(p => byId[p.TaskId]).ToList())).ToList());
    }

    private async Task<KanbanBoardResponse> BoardChangedAsync(UserContext user, Guid projectId, Guid boardId, CancellationToken ct)
    {
        await events.PublishAsync(new ProjectContentChanged(projectId, ProjectContentChanged.Board), ct);
        return await ReadAsync(user, boardId, ct);
    }

    /// <summary>Finds a column of the caller's organization and checks Edit on its project.</summary>
    private async Task<(KanbanColumn? Column, Guid ProjectId, ServiceFailure? Failure)> RequireColumnAsync(UserContext user, Guid columnId, CancellationToken ct)
    {
        var found = await (
                from column in db.Set<KanbanColumn>().AsTracking()
                join board in db.Set<KanbanBoard>() on column.BoardId equals board.Id
                where column.Id == columnId && column.OrganizationId == user.OrganizationId
                select new { Column = column, board.ProjectId })
            .SingleOrDefaultAsync(ct);
        if (found is null || await projectAccess.RequireAsync(user, found.ProjectId, ProjectPermission.View, ct) is not null)
        {
            return (null, Guid.Empty, ServiceFailure.NotFound("Column"));
        }

        return (found.Column, found.ProjectId, await projectAccess.RequireAsync(user, found.ProjectId, ProjectPermission.Edit, ct));
    }

    private async Task<bool> IsLastColumnOfStatusAsync(Guid boardId, Guid columnId, string status, CancellationToken ct) =>
        !await db.Set<KanbanColumn>().AnyAsync(c => c.BoardId == boardId && c.Id != columnId && c.TaskStatus == status, ct);

    private static ServiceFailure? Validate(KanbanColumn column)
    {
        if (column.Name.Length is 0 or > MaxColumnNameLength)
        {
            return ServiceFailure.Invalid("name", $"Required, at most {MaxColumnNameLength} characters.");
        }

        if (!TaskStatuses.All.Contains(column.TaskStatus))
        {
            return ServiceFailure.Invalid("taskStatus", $"Must be one of: {string.Join(", ", TaskStatuses.All)}.");
        }

        return column.WipLimit is <= 0 ? ServiceFailure.Invalid("wipLimit", "Must be greater than 0.") : null;
    }
}
