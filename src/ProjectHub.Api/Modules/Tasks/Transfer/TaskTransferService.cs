using Microsoft.EntityFrameworkCore;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Infrastructure.Events;
using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Infrastructure.Outcomes;
using ProjectHub.Api.Modules.Audit;
using ProjectHub.Api.Modules.Departments;
using ProjectHub.Api.Modules.Identity;
using ProjectHub.Api.Modules.Identity.Authorization;
using ProjectHub.Api.Modules.Projects;
using ProjectHub.Api.Modules.Users;

namespace ProjectHub.Api.Modules.Tasks.Transfer;

public enum TaskFileFormat
{
    Csv,
    Xlsx,
}

public sealed record TaskFile(string FileName, string ContentType, byte[] Content);

/// <param name="Rows">Data rows found in the file.</param>
/// <param name="Created">Tasks created; 0 for a preview and whenever there are errors (nothing is imported then).</param>
public sealed record TaskImportResult(int Rows, int Created, IReadOnlyList<string> IgnoredColumns, IReadOnlyList<TaskImportError> Errors);

/// <summary>
/// Tasks of a project as CSV or Excel table (ADR 0018). The export contains every active task including subtasks;
/// the import creates new top-level tasks, all rows or none.
/// </summary>
public sealed class TaskTransferService(
    ProjectHubDbContext db,
    ProjectAccess access,
    IActivityLog activity,
    IDomainEventPublisher events,
    TimeProvider clock)
{
    public const int MaxExportRows = 10_000;
    public const int MaxImportRows = 1_000;
    public const long MaxImportBytes = 2 * 1024 * 1024;

    private static readonly TableLimits ImportLimits = new(MaxImportRows, 50, TaskService.MaxDescriptionLength);

    public async Task<ServiceResult<TaskFile>> ExportAsync(UserContext user, Guid projectId, TaskFileFormat format, CancellationToken ct)
    {
        if (await access.RequireAsync(user, projectId, ProjectPermission.View, ct) is { } failure)
        {
            return failure;
        }

        var tasks = await (
                from task in db.Set<ProjectTask>().AsNoTracking()
                where task.OrganizationId == user.OrganizationId && task.ProjectId == projectId && task.DeletedAt == null
                join assignee in db.Set<AppUser>() on task.AssigneeId equals assignee.Id into assignees
                from assignee in assignees.DefaultIfEmpty()
                orderby task.CreatedAt, task.Id
                select new ExportRow(task, assignee == null ? null : assignee.Email, assignee == null ? null : assignee.DisplayName))
            .Take(MaxExportRows + 1)
            .ToListAsync(ct);
        if (tasks.Count > MaxExportRows)
        {
            return ServiceFailure.Invalid("projectId", $"The project has more than {MaxExportRows} tasks; the export is limited to that.");
        }

        var ordered = TreeOrder(tasks);
        var titles = tasks.ToDictionary(t => t.Task.Id, t => t.Task.Title);
        var project = await db.Set<Project>().AsNoTracking().Where(p => p.Id == projectId).Select(p => p.Name).SingleAsync(ct);
        var baseName = $"{FileNames.Safe(project, "projekt")} Aufgaben {clock.GetUtcNow():yyyy-MM-dd}";

        return format == TaskFileFormat.Csv
            ? new TaskFile(baseName + ".csv", CsvTable.ContentType, CsvTable.Write([
                TaskSheet.Columns.Select(c => (string?)c.Header).ToList(),
                .. ordered.Select(row => CsvRow(row, titles)),
            ]))
            : new TaskFile(baseName + ".xlsx", XlsxTable.ContentType, XlsxTable.Write(
                "Aufgaben",
                TaskSheet.Columns.Select(c => c.Header).ToList(),
                ordered.Select(row => XlsxRow(row, titles)).ToList(),
                TaskSheet.Columns.Select(c => c.Width).ToList()));
    }

    public async Task<ServiceResult<TaskImportResult>> ImportAsync(
        UserContext user, Guid projectId, string fileName, byte[] content, bool dryRun, CancellationToken ct)
    {
        if (await access.RequireAsync(user, projectId, ProjectPermission.Contribute, ct) is { } failure)
        {
            return failure;
        }

        IReadOnlyList<IReadOnlyList<string>> table;
        try
        {
            table = Path.GetExtension(fileName).ToLowerInvariant() switch
            {
                ".csv" or ".txt" => CsvTable.Read(content, ImportLimits),
                ".xlsx" => XlsxTable.Read(content, ImportLimits),
                _ => throw new TableFormatException("Only .csv and .xlsx files can be imported."),
            };
        }
        catch (TableFormatException e)
        {
            return ServiceFailure.Invalid("file", e.Message);
        }

        if (table.Count == 0)
        {
            return ServiceFailure.Invalid("file", "The file is empty.");
        }

        var (columns, ignored) = TaskSheet.MapHeader(table[0]);
        if (!columns.ContainsKey(TaskColumn.Title))
        {
            return new TaskImportResult(table.Count - 1, 0, ignored, [
                new TaskImportError(1, TaskSheet.Header(TaskColumn.Title), TaskImportErrorCodes.MissingTitleColumn,
                    "The first row must name the columns and contain a column 'Titel' (or 'Title')."),
            ]);
        }

        var people = await AssignablePeopleAsync(user, projectId, ct);
        var now = clock.GetUtcNow();
        var errors = new List<TaskImportError>();
        var tasks = new List<ProjectTask>();
        for (var i = 1; i < table.Count; i++)
        {
            var (draft, rowErrors) = TaskSheet.ReadRow(i + 1, table[i], columns);
            errors.AddRange(rowErrors);
            if (draft is null)
            {
                continue;
            }

            var task = NewTask(user, projectId, draft, now);
            if (draft.Assignee is { } assignee)
            {
                if (people.Find(assignee) is { } assigneeId)
                {
                    task.AssigneeId = assigneeId;
                }
                else
                {
                    errors.Add(new TaskImportError(draft.Row, TaskSheet.Header(columns.ContainsKey(TaskColumn.AssigneeEmail) ? TaskColumn.AssigneeEmail : TaskColumn.Assignee),
                        TaskImportErrorCodes.UnknownAssignee, $"'{assignee}' cannot be assigned tasks in this project."));
                }
            }

            if (TaskService.ValidateFields(task) is { } invalid)
            {
                errors.Add(new TaskImportError(draft.Row, invalid.Field ?? string.Empty, TaskImportErrorCodes.Invalid, invalid.Message ?? "Invalid value."));
            }

            tasks.Add(task);
        }

        var result = new TaskImportResult(table.Count - 1, 0, ignored, errors);
        if (dryRun || errors.Count > 0 || tasks.Count == 0)
        {
            return result;
        }

        db.Set<ProjectTask>().AddRange(tasks);
        activity.Record(user, projectId, ActivityActions.TasksImported, "project", projectId, new { Count = tasks.Count, FileName = Path.GetFileName(fileName) });
        await db.SaveChangesAsync(ct);

        // No assignment notifications: an import of hundreds of rows would flood people with mails (Vorschlag DEC-040).
        await events.PublishAsync(new ProjectContentChanged(projectId, ProjectContentChanged.Tasks), ct);
        return result with { Created = tasks.Count };
    }

    private static ProjectTask NewTask(UserContext user, Guid projectId, TaskDraft draft, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        OrganizationId = user.OrganizationId,
        ProjectId = projectId,
        Title = draft.Title,
        Description = draft.Description,
        Status = draft.Status ?? TaskStatus.Todo,
        Priority = draft.Priority ?? TaskPriority.Normal,
        CreatorId = user.UserId,
        StartDate = draft.StartDate,
        DueDate = draft.DueDate,
        Progress = draft.Progress ?? 0,
        EstimatedHours = draft.EstimatedHours,
        CreatedAt = now,
        UpdatedAt = now,
        Version = 1,
    };

    /// <summary>
    /// The people who may be assigned tasks in the project (as <see cref="IProjectHubAuthorization.CanBeAssignedAsync"/>):
    /// active users of the organization who are organization admins, leads of the project's department or members with
    /// a contributing role.
    /// </summary>
    private async Task<AssignablePeople> AssignablePeopleAsync(UserContext user, Guid projectId, CancellationToken ct)
    {
        var contributing = new[] { ProjectRole.Admin, ProjectRole.Editor, ProjectRole.Member };
        var departmentId = await db.Set<Project>().Where(p => p.Id == projectId).Select(p => p.DepartmentId).SingleAsync(ct);
        var people = await db.Set<AppUser>().AsNoTracking()
            .Where(u => u.OrganizationId == user.OrganizationId && u.Status == UserStatus.Active)
            .Where(u => u.OrganizationRole == OrganizationRole.Admin
                        || db.Set<DepartmentMember>().Any(m => m.DepartmentId == departmentId && m.UserId == u.Id && m.Role == DepartmentRole.Lead)
                        || db.Set<ProjectMember>().Any(m => m.ProjectId == projectId && m.UserId == u.Id && contributing.Contains(m.Role)))
            .Select(u => new { u.Id, u.Email, u.DisplayName })
            .ToListAsync(ct);

        return new AssignablePeople(people.Select(p => (p.Id, p.Email, p.DisplayName)).ToList());
    }

    /// <summary>Parents before their subtasks, siblings by creation, as in the task list.</summary>
    private static List<ExportRow> TreeOrder(List<ExportRow> rows)
    {
        var ids = rows.Select(r => r.Task.Id).ToHashSet();
        var children = rows.ToLookup(r => r.Task.ParentTaskId is { } parent && ids.Contains(parent) ? parent : (Guid?)null);
        var ordered = new List<ExportRow>(rows.Count);
        void Visit(Guid? parent)
        {
            foreach (var row in children[parent])
            {
                ordered.Add(row);
                Visit(row.Task.Id);
            }
        }

        Visit(null);
        return ordered;
    }

    private static List<string?> CsvRow(ExportRow row, IReadOnlyDictionary<Guid, string> titles)
    {
        var task = row.Task;
        return
        [
            task.Title, task.Description, TaskSheet.StatusLabels.GetValueOrDefault(task.Status, task.Status),
            TaskSheet.PriorityLabels.GetValueOrDefault(task.Priority, task.Priority), row.AssigneeEmail, row.AssigneeName,
            TaskSheet.Day(task.StartDate), TaskSheet.Day(task.DueDate), TaskSheet.Number(task.Progress), TaskSheet.Number(task.EstimatedHours),
            task.ParentTaskId is { } parent ? titles.GetValueOrDefault(parent) : null, task.Id.ToString(),
        ];
    }

    private static List<SheetCell?> XlsxRow(ExportRow row, IReadOnlyDictionary<Guid, string> titles)
    {
        var task = row.Task;
        return
        [
            SheetCell.Of(task.Title), SheetCell.Of(task.Description),
            SheetCell.Of(TaskSheet.StatusLabels.GetValueOrDefault(task.Status, task.Status)),
            SheetCell.Of(TaskSheet.PriorityLabels.GetValueOrDefault(task.Priority, task.Priority)),
            SheetCell.Of(row.AssigneeEmail), SheetCell.Of(row.AssigneeName),
            task.StartDate is { } start ? new SheetCell.Day(start) : null,
            task.DueDate is { } due ? new SheetCell.Day(due) : null,
            new SheetCell.Number(task.Progress),
            task.EstimatedHours is { } hours ? new SheetCell.Number(hours) : null,
            SheetCell.Of(task.ParentTaskId is { } parent ? titles.GetValueOrDefault(parent) : null),
            SheetCell.Of(task.Id.ToString()),
        ];
    }

    private sealed record ExportRow(ProjectTask Task, string? AssigneeEmail, string? AssigneeName);

    /// <summary>Finds a person by e-mail address or, if no one else has it, by display name (both ignoring case).</summary>
    private sealed class AssignablePeople(IReadOnlyList<(Guid Id, string Email, string DisplayName)> people)
    {
        public Guid? Find(string text)
        {
            var key = text.Trim();
            var byEmail = people.Where(p => string.Equals(p.Email, key, StringComparison.OrdinalIgnoreCase)).ToList();
            if (byEmail.Count == 1)
            {
                return byEmail[0].Id;
            }

            var byName = people.Where(p => string.Equals(p.DisplayName, key, StringComparison.OrdinalIgnoreCase)).ToList();
            return byName.Count == 1 ? byName[0].Id : null;
        }
    }
}
