using System.Globalization;
using System.Text;

namespace ProjectHub.Api.Modules.Tasks.Transfer;

/// <summary>The fields a task table can carry. Export writes all of them; import reads those it finds.</summary>
public enum TaskColumn
{
    Title,
    Description,
    Status,
    Priority,
    AssigneeEmail,
    Assignee,
    StartDate,
    DueDate,
    Progress,
    EstimatedHours,
    ParentTask,
    Id,
}

/// <summary>One row of an import, read but not yet checked against the project (assignees, field limits).</summary>
public sealed record TaskDraft(
    int Row,
    string Title,
    string? Description,
    string? Status,
    string? Priority,
    IReadOnlyList<string> Assignees,
    DateOnly? StartDate,
    DateOnly? DueDate,
    decimal? EstimatedHours)
{
    // Records compare lists by reference; tests compare drafts by value.
    public bool Equals(TaskDraft? other) =>
        other is not null && (Row, Title, Description, Status, Priority, StartDate, DueDate, EstimatedHours)
            == (other.Row, other.Title, other.Description, other.Status, other.Priority, other.StartDate, other.DueDate, other.EstimatedHours)
        && Assignees.SequenceEqual(other.Assignees);

    public override int GetHashCode() => HashCode.Combine(Row, Title, Status, StartDate, DueDate);
}

/// <summary>Why a row cannot be imported. <see cref="Code"/> is stable for the UI; <see cref="Message"/> is for logs and API users.</summary>
public sealed record TaskImportError(int Row, string Column, string Code, string Message);

public static class TaskImportErrorCodes
{
    public const string Required = "required";
    public const string Invalid = "invalid";
    public const string UnknownStatus = "unknown_status";
    public const string UnknownPriority = "unknown_priority";
    public const string InvalidDate = "invalid_date";
    public const string InvalidNumber = "invalid_number";
    public const string UnknownAssignee = "unknown_assignee";
    public const string MissingTitleColumn = "missing_title_column";
}

/// <summary>
/// Column names, value labels and value parsing of task tables. The labels are the ones of the user interface, so an
/// export can be edited in Excel and imported again; English names and the API values are accepted as well.
/// </summary>
public static class TaskSheet
{
    public static readonly IReadOnlyList<(TaskColumn Column, string Header, double Width)> Columns =
    [
        (TaskColumn.Title, "Titel", 40),
        (TaskColumn.Description, "Beschreibung", 50),
        (TaskColumn.Status, "Status", 12),
        (TaskColumn.Priority, "Priorität", 12),
        (TaskColumn.AssigneeEmail, "Zuständig (E-Mail)", 28),
        (TaskColumn.Assignee, "Zuständig", 22),
        (TaskColumn.StartDate, "Start", 12),
        (TaskColumn.DueDate, "Fällig", 12),
        (TaskColumn.Progress, "Fortschritt (%)", 14),
        (TaskColumn.EstimatedHours, "Aufwand (h)", 12),
        (TaskColumn.ParentTask, "Übergeordnete Aufgabe", 30),
        (TaskColumn.Id, "ID", 38),
    ];

    /// <summary>Several people in one cell, as Excel and MS Project write them: "anna@example.org; ben@example.org".</summary>
    public const string ListSeparator = "; ";

    public static readonly IReadOnlyDictionary<string, string> StatusLabels = new Dictionary<string, string>
    {
        [TaskStatus.Todo] = "Offen",
        [TaskStatus.InProgress] = "In Arbeit",
        [TaskStatus.Done] = "Erledigt",
    };

    public static readonly IReadOnlyDictionary<string, string> PriorityLabels = new Dictionary<string, string>
    {
        [TaskPriority.Low] = "Niedrig",
        [TaskPriority.Normal] = "Normal",
        [TaskPriority.High] = "Hoch",
        [TaskPriority.Urgent] = "Dringend",
    };

    /// <summary>Header names (normalized: lower case letters only) the import understands.</summary>
    private static readonly IReadOnlyDictionary<string, TaskColumn> HeaderAliases = new Dictionary<string, TaskColumn>
    {
        ["titel"] = TaskColumn.Title, ["title"] = TaskColumn.Title, ["aufgabe"] = TaskColumn.Title, ["name"] = TaskColumn.Title,
        ["summary"] = TaskColumn.Title, ["zusammenfassung"] = TaskColumn.Title, ["vorgangsname"] = TaskColumn.Title,
        ["beschreibung"] = TaskColumn.Description, ["description"] = TaskColumn.Description, ["notizen"] = TaskColumn.Description,
        ["notes"] = TaskColumn.Description,
        ["status"] = TaskColumn.Status,
        ["priorität"] = TaskColumn.Priority, ["prioritaet"] = TaskColumn.Priority, ["priority"] = TaskColumn.Priority,
        ["zuständigemail"] = TaskColumn.AssigneeEmail, ["zustaendigemail"] = TaskColumn.AssigneeEmail, ["email"] = TaskColumn.AssigneeEmail,
        ["assigneeemail"] = TaskColumn.AssigneeEmail,
        ["zuständig"] = TaskColumn.Assignee, ["zustaendig"] = TaskColumn.Assignee, ["verantwortlich"] = TaskColumn.Assignee,
        ["assignee"] = TaskColumn.Assignee, ["ressourcennamen"] = TaskColumn.Assignee,
        ["start"] = TaskColumn.StartDate, ["startdatum"] = TaskColumn.StartDate, ["startdate"] = TaskColumn.StartDate,
        ["anfang"] = TaskColumn.StartDate, ["beginn"] = TaskColumn.StartDate,
        ["fällig"] = TaskColumn.DueDate, ["faellig"] = TaskColumn.DueDate, ["fälligam"] = TaskColumn.DueDate,
        ["fälligkeit"] = TaskColumn.DueDate, ["fälligkeitsdatum"] = TaskColumn.DueDate, ["enddatum"] = TaskColumn.DueDate,
        ["ende"] = TaskColumn.DueDate, ["due"] = TaskColumn.DueDate, ["duedate"] = TaskColumn.DueDate, ["end"] = TaskColumn.DueDate,
        ["fortschritt"] = TaskColumn.Progress, ["progress"] = TaskColumn.Progress, ["abgeschlossen"] = TaskColumn.Progress,
        ["complete"] = TaskColumn.Progress,
        ["aufwand"] = TaskColumn.EstimatedHours, ["aufwandh"] = TaskColumn.EstimatedHours, ["aufwandstunden"] = TaskColumn.EstimatedHours,
        ["stunden"] = TaskColumn.EstimatedHours, ["estimatedhours"] = TaskColumn.EstimatedHours, ["estimate"] = TaskColumn.EstimatedHours,
        ["übergeordneteaufgabe"] = TaskColumn.ParentTask, ["parent"] = TaskColumn.ParentTask, ["parenttask"] = TaskColumn.ParentTask,
        ["id"] = TaskColumn.Id,
    };

    private static readonly IReadOnlyDictionary<string, string> StatusAliases = Aliases(StatusLabels, new Dictionary<string, string>
    {
        ["inbearbeitung"] = TaskStatus.InProgress, ["fertig"] = TaskStatus.Done, ["todo"] = TaskStatus.Todo,
        ["open"] = TaskStatus.Todo, ["inprogress"] = TaskStatus.InProgress, ["done"] = TaskStatus.Done,
    });

    private static readonly IReadOnlyDictionary<string, string> PriorityAliases = Aliases(PriorityLabels, new Dictionary<string, string>
    {
        ["mittel"] = TaskPriority.Normal, ["medium"] = TaskPriority.Normal, ["kritisch"] = TaskPriority.Urgent,
        ["critical"] = TaskPriority.Urgent, ["highest"] = TaskPriority.Urgent, ["lowest"] = TaskPriority.Low,
    });

    private static readonly string[] DateFormats = ["yyyy-MM-dd", "d.M.yyyy", "d.M.yy", "yyyy/M/d", "M/d/yyyy"];

    /// <summary>Lower case letters (including umlauts) only: "Zuständig (E-Mail)" becomes "zuständigemail".</summary>
    public static string Normalize(string text) =>
        new(text.Trim().ToLowerInvariant().Where(char.IsLetter).ToArray());

    /// <summary>Which column of the file holds which field. Unknown columns are reported and left out.</summary>
    public static (IReadOnlyDictionary<TaskColumn, int> Columns, IReadOnlyList<string> Ignored) MapHeader(IReadOnlyList<string> header)
    {
        var columns = new Dictionary<TaskColumn, int>();
        var ignored = new List<string>();
        for (var i = 0; i < header.Count; i++)
        {
            // Parent and id are exported for reference only: an import always creates new top-level tasks. Progress is
            // calculated from the status (ADR 0022), so the import leaves it out as well.
            if (HeaderAliases.TryGetValue(Normalize(header[i]), out var column)
                && column is not TaskColumn.ParentTask and not TaskColumn.Id and not TaskColumn.Progress
                && columns.TryAdd(column, i))
            {
                continue;
            }

            if (header[i].Trim().Length > 0)
            {
                ignored.Add(header[i].Trim());
            }
        }

        return (columns, ignored);
    }

    /// <summary>Reads a data row (1-based <paramref name="rowNumber"/> as shown in the spreadsheet) into a draft, or the errors in it.</summary>
    public static (TaskDraft? Draft, IReadOnlyList<TaskImportError> Errors) ReadRow(
        int rowNumber, IReadOnlyList<string> cells, IReadOnlyDictionary<TaskColumn, int> columns)
    {
        var errors = new List<TaskImportError>();
        string? Cell(TaskColumn column) =>
            columns.TryGetValue(column, out var index) && index < cells.Count && cells[index].Trim() is { Length: > 0 } value ? value : null;
        void Fail(TaskColumn column, string code, string message) => errors.Add(new TaskImportError(rowNumber, Header(column), code, message));

        var title = Cell(TaskColumn.Title);
        if (title is null)
        {
            Fail(TaskColumn.Title, TaskImportErrorCodes.Required, "A title is required.");
        }

        string? status = null;
        if (Cell(TaskColumn.Status) is { } statusText && !StatusAliases.TryGetValue(Normalize(statusText), out status))
        {
            Fail(TaskColumn.Status, TaskImportErrorCodes.UnknownStatus, $"Unknown status '{statusText}'.");
        }

        string? priority = null;
        if (Cell(TaskColumn.Priority) is { } priorityText && !PriorityAliases.TryGetValue(Normalize(priorityText), out priority))
        {
            Fail(TaskColumn.Priority, TaskImportErrorCodes.UnknownPriority, $"Unknown priority '{priorityText}'.");
        }

        DateOnly? Date(TaskColumn column)
        {
            if (Cell(column) is not { } text)
            {
                return null;
            }

            if (ParseDate(text) is { } date)
            {
                return date;
            }

            Fail(column, TaskImportErrorCodes.InvalidDate, $"'{text}' is not a date (e.g. 2026-11-30 or 30.11.2026).");
            return null;
        }

        var start = Date(TaskColumn.StartDate);
        var due = Date(TaskColumn.DueDate);

        decimal? hours = null;
        if (Cell(TaskColumn.EstimatedHours) is { } hoursText)
        {
            if (ParseNumber(hoursText) is { } value && value >= 0)
            {
                hours = value;
            }
            else
            {
                Fail(TaskColumn.EstimatedHours, TaskImportErrorCodes.InvalidNumber, $"'{hoursText}' is not a number of hours.");
            }
        }

        if (errors.Count > 0 || title is null)
        {
            return (null, errors);
        }

        var assignees = (Cell(TaskColumn.AssigneeEmail) ?? Cell(TaskColumn.Assignee))?
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
        return (new TaskDraft(rowNumber, title, Cell(TaskColumn.Description), status, priority, assignees, start, due, hours), errors);
    }

    public static string Header(TaskColumn column) => Columns.Single(c => c.Column == column).Header;

    /// <summary>ISO, German and US dates; a plain number is an Excel serial date (how .xlsx stores dates).</summary>
    public static DateOnly? ParseDate(string text)
    {
        text = text.Trim();
        if (DateOnly.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        // ISO date with a time (2026-11-30T00:00:00 or "2026-11-30 00:00").
        if (text.Length > 10 && DateOnly.TryParseExact(text[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return date;
        }

        if (double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var serial) && serial is >= 1 and < 2_958_466)
        {
            return DateOnly.FromDateTime(DateTime.FromOADate(Math.Floor(serial)));
        }

        return null;
    }

    /// <summary>Accepts decimal point and decimal comma ("1.5", "1,5"); no thousands separators.</summary>
    public static decimal? ParseNumber(string text)
    {
        text = text.Trim();
        if (text.Count(c => c is ',' or '.') > 1)
        {
            return null;
        }

        return decimal.TryParse(text.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    public static string Day(DateOnly? day) => day?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;

    public static string Number(decimal? value) =>
        value?.ToString("0.##", CultureInfo.GetCultureInfo("de-DE")) ?? string.Empty;

    private static IReadOnlyDictionary<string, string> Aliases(IReadOnlyDictionary<string, string> labels, Dictionary<string, string> extra)
    {
        foreach (var (value, label) in labels)
        {
            extra[Normalize(label)] = value;
            extra[Normalize(value)] = value;
        }

        return extra;
    }
}
