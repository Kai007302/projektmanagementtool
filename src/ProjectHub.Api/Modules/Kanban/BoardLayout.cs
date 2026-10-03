namespace ProjectHub.Api.Modules.Kanban;

/// <summary>Where a task currently sits on the board, as stored on the task.</summary>
public sealed record CardPlacement(Guid TaskId, string Status, Guid? ColumnId, decimal? Position, DateTimeOffset CreatedAt);

/// <summary>A column as far as the layout is concerned.</summary>
public sealed record ColumnSlot(Guid Id, string TaskStatus);

/// <summary>
/// Derives the board from the tasks. The task status is the truth: a card stays in the column
/// stored on the task only while that column still stands for the task's status; otherwise it
/// goes to the first column of its status, after the cards placed there explicitly.
/// </summary>
public static class BoardLayout
{
    public static IReadOnlyDictionary<Guid, List<CardPlacement>> Arrange(
        IReadOnlyList<ColumnSlot> columns, IEnumerable<CardPlacement> cards)
    {
        var result = columns.ToDictionary(c => c.Id, _ => new List<CardPlacement>());
        if (columns.Count == 0)
        {
            return result;
        }

        var keys = new Dictionary<Guid, (decimal? Position, DateTimeOffset CreatedAt)>();
        foreach (var card in cards)
        {
            var stored = card.ColumnId is { } id ? columns.FirstOrDefault(c => c.Id == id && c.TaskStatus == card.Status) : null;
            var column = stored ?? columns.FirstOrDefault(c => c.TaskStatus == card.Status) ?? columns[0];
            result[column.Id].Add(card);
            keys[card.TaskId] = (stored is null ? null : card.Position, card.CreatedAt);
        }

        foreach (var list in result.Values)
        {
            list.Sort((a, b) =>
            {
                var (pa, ca) = keys[a.TaskId];
                var (pb, cb) = keys[b.TaskId];
                if (pa != pb)
                {
                    return pa is null ? 1 : pb is null ? -1 : pa.Value.CompareTo(pb.Value);
                }

                var byCreation = ca.CompareTo(cb);
                return byCreation != 0 ? byCreation : a.TaskId.CompareTo(b.TaskId);
            });
        }

        return result;
    }

    /// <summary>The column a task is shown in.</summary>
    public static Guid? ColumnOf(IReadOnlyDictionary<Guid, List<CardPlacement>> layout, Guid taskId) =>
        layout.FirstOrDefault(entry => entry.Value.Any(c => c.TaskId == taskId)) is { Value: not null } match ? match.Key : null;
}
