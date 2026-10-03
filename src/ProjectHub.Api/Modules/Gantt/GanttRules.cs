namespace ProjectHub.Api.Modules.Gantt;

/// <summary>Scheduling rules. Days are inclusive: a task occupies every day from its start to its due date.</summary>
public static class GanttRules
{
    /// <summary>The days a task occupies; a task with only one date takes a single day.</summary>
    public static (DateOnly Start, DateOnly End)? Span(DateOnly? start, DateOnly? due) =>
        (start ?? due, due ?? start) is ({ } s, { } e) ? (s, e) : null;

    /// <summary>
    /// Whether the dates break the dependency. Dependencies only warn (DEC-023); tasks without dates never violate one.
    /// </summary>
    public static bool IsViolated(string dependencyType, (DateOnly Start, DateOnly End)? source, (DateOnly Start, DateOnly End)? target)
    {
        if (source is not { } s || target is not { } t)
        {
            return false;
        }

        return dependencyType switch
        {
            DependencyTypes.FinishToStart => t.Start <= s.End,
            DependencyTypes.StartToStart => t.Start < s.Start,
            DependencyTypes.FinishToFinish => t.End < s.End,
            DependencyTypes.StartToFinish => t.End < s.Start,
            _ => false,
        };
    }

    /// <summary>Whether adding source → target closes a cycle, i.e. target already leads to source.</summary>
    public static bool WouldCreateCycle(IEnumerable<(Guid Source, Guid Target)> edges, Guid source, Guid target)
    {
        var successors = edges.ToLookup(e => e.Source, e => e.Target);
        var visited = new HashSet<Guid>();
        var pending = new Stack<Guid>([target]);
        while (pending.TryPop(out var current))
        {
            if (current == source)
            {
                return true;
            }

            if (visited.Add(current))
            {
                foreach (var next in successors[current])
                {
                    pending.Push(next);
                }
            }
        }

        return false;
    }

    /// <summary>Whether <paramref name="ancestor"/> is a parent, grandparent, … of <paramref name="task"/>.</summary>
    public static bool IsAncestor(IReadOnlyDictionary<Guid, Guid?> parents, Guid ancestor, Guid task)
    {
        var current = parents.GetValueOrDefault(task);
        for (var depth = 0; current is { } id && depth <= parents.Count; depth++)
        {
            if (id == ancestor)
            {
                return true;
            }

            current = parents.GetValueOrDefault(id);
        }

        return false;
    }
}
