using Microsoft.EntityFrameworkCore;
using ProjectHub.Api.Infrastructure.Database;

namespace ProjectHub.Api.Modules.Tasks;

/// <summary>
/// The progress of a task is calculated, nobody sets it by hand (ADR 0022). A task without subtasks counts by its
/// status (open 0 %, in progress 50 %, done 100 %); a task with subtasks is done at 100 % and otherwise at the rounded
/// mean of its subtasks. It is kept in <c>task.progress</c>, so Gantt, Kanban, exports and the assistant read it as
/// before; the version of a task does not change with it, because nobody edits it.
/// </summary>
public sealed class TaskProgress(ProjectHubDbContext db)
{
    public sealed record Node(Guid Id, Guid? ParentTaskId, string Status);

    public static short ForStatus(string status) => status switch
    {
        TaskStatus.Done => 100,
        TaskStatus.InProgress => 50,
        _ => 0,
    };

    /// <summary>The progress of every given task, from its status and its subtasks among <paramref name="tasks"/>.</summary>
    public static IReadOnlyDictionary<Guid, short> Calculate(IReadOnlyCollection<Node> tasks)
    {
        var children = tasks.Where(t => t.ParentTaskId is not null).ToLookup(t => t.ParentTaskId!.Value);
        var progress = new Dictionary<Guid, short>(tasks.Count);
        var visiting = new HashSet<Guid>();

        short Of(Node task)
        {
            if (progress.TryGetValue(task.Id, out var known))
            {
                return known;
            }

            // A cycle cannot be saved (TaskService checks the parent), but corrupt data must not hang the request.
            if (!visiting.Add(task.Id))
            {
                return ForStatus(task.Status);
            }

            var subtasks = children[task.Id].ToList();
            var value = subtasks.Count == 0 || task.Status == TaskStatus.Done
                ? ForStatus(task.Status)
                : (short)Math.Round(subtasks.Average(s => (double)Of(s)), MidpointRounding.AwayFromZero);
            visiting.Remove(task.Id);
            progress[task.Id] = value;
            return value;
        }

        foreach (var task in tasks)
        {
            Of(task);
        }

        return progress;
    }

    /// <summary>Calculates the progress of all active tasks of a project again and saves what changed.</summary>
    public async Task RecalculateAsync(Guid organizationId, Guid projectId, CancellationToken ct)
    {
        var tasks = await db.Set<ProjectTask>().AsNoTracking()
            .Where(t => t.OrganizationId == organizationId && t.ProjectId == projectId && t.DeletedAt == null)
            .Select(t => new { t.Id, t.ParentTaskId, t.Status, t.Progress })
            .ToListAsync(ct);

        var calculated = Calculate(tasks.Select(t => new Node(t.Id, t.ParentTaskId, t.Status)).ToList());
        var changed = tasks.Where(t => calculated[t.Id] != t.Progress).ToList();
        if (changed.Count == 0)
        {
            return;
        }

        var ids = changed.Select(t => t.Id).ToArray();
        var values = changed.Select(t => calculated[t.Id]).ToArray();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            update task set progress = calculated.progress
            from unnest({ids}::uuid[], {values}::smallint[]) as calculated(id, progress)
            where task.id = calculated.id and task.organization_id = {organizationId}
            """,
            ct);
    }
}
