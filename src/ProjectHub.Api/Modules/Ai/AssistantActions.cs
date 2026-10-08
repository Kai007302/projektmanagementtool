using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Modules.Identity;
using ProjectHub.Api.Modules.Knowledge;
using ProjectHub.Api.Modules.Projects;
using ProjectHub.Api.Modules.Tasks;
using ProjectHub.Api.Modules.Departments;
using ProjectHub.Api.Modules.Users;
using ProjectHub.Api.Modules.Whiteboard;

namespace ProjectHub.Api.Modules.Ai;

public sealed record ActionDetail(string Label, string Value);

/// <summary>A change the assistant wants to make, as the person sees it before approving it.</summary>
public sealed record AssistantAction(
    string Id,
    string Tool,
    string Title,
    bool Destructive,
    IReadOnlyList<ActionDetail> Details,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text);

/// <summary>
/// Describes proposed tool calls in the person's words: German labels and values, names instead of ids (resolved as the person,
/// so nothing they may not see shows up), and the full Markdown of an article text.
/// </summary>
public sealed class AssistantActions(
    UserContext user,
    ProjectHubDbContext db,
    ProjectService projects,
    TaskService tasks,
    KnowledgeArticleService articles,
    KnowledgeSpaceService spaces,
    DepartmentService departments,
    WhiteboardService whiteboards)
{
    private static readonly Dictionary<string, (string Title, bool ReadOnly, bool Destructive)> Tools =
        ProjectHubTools.Methods(includeWriteTools: true).ToDictionary(
            t => t.Attribute.Name!, t => (t.Attribute.Title ?? t.Attribute.Name!, t.Attribute.ReadOnly, t.Attribute.Destructive));

    private static readonly Dictionary<string, string> Labels = new()
    {
        ["projectId"] = "Projekt", ["taskId"] = "Aufgabe", ["articleId"] = "Artikel", ["departmentId"] = "Abteilung",
        ["title"] = "Titel", ["name"] = "Name", ["description"] = "Beschreibung", ["summary"] = "Zusammenfassung",
        ["status"] = "Status", ["priority"] = "Priorität", ["assigneeIds"] = "Zuständig", ["addAssigneeIds"] = "Zuständig hinzufügen",
        ["removeAssigneeIds"] = "Zuständig entfernen", ["unassign"] = "Alle Zuständigen entfernen",
        ["parentTaskId"] = "Übergeordnete Aufgabe", ["startDate"] = "Start", ["endDate"] = "Ende", ["dueDate"] = "Fällig",
        ["date"] = "Datum", ["text"] = "Text", ["userId"] = "Person", ["role"] = "Rolle",
        ["articleType"] = "Art", ["spaceId"] = "Kategorie", ["tags"] = "Schlagwörter", ["changeNote"] = "Änderungsnotiz",
        ["resourceType"] = "Verknüpfen mit", ["resourceId"] = "Ziel", ["sourceTaskId"] = "Zuerst", ["targetTaskId"] = "Danach",
        ["dependencyType"] = "Art",
    };

    private static readonly Dictionary<string, string> Values = new()
    {
        ["todo"] = "Offen", ["in_progress"] = "In Arbeit", ["done"] = "Erledigt",
        ["low"] = "Niedrig", ["normal"] = "Normal", ["high"] = "Hoch", ["urgent"] = "Dringend",
        ["planned"] = "Geplant", ["active"] = "Aktiv", ["on_hold"] = "Pausiert", ["completed"] = "Abgeschlossen",
        ["draft"] = "Entwurf", ["review"] = "In Prüfung", ["published"] = "Veröffentlicht", ["archived"] = "Archiviert",
        ["admin"] = "Admin", ["editor"] = "Bearbeiten", ["member"] = "Mitglied", ["viewer"] = "Lesen", ["guest"] = "Gast", ["owner"] = "Verantwortlich", ["lead"] = "Leitung",
        ["project"] = "Projekt", ["task"] = "Aufgabe", ["department"] = "Abteilung", ["whiteboard"] = "Whiteboard",
        ["finish_to_start"] = "Ende → Anfang", ["start_to_start"] = "Anfang → Anfang", ["finish_to_finish"] = "Ende → Ende", ["start_to_finish"] = "Anfang → Ende",
        ["article"] = "Artikel", ["how_to"] = "Anleitung", ["best_practice"] = "Best Practice", ["process"] = "Prozess", ["policy"] = "Richtlinie",
        ["faq"] = "FAQ", ["template"] = "Vorlage", ["checklist"] = "Checkliste", ["glossary"] = "Glossar",
    };

    public static bool IsKnown(string tool) => Tools.ContainsKey(tool);

    /// <summary>Read-only tools never need the person: when the model asks for one together with a change, it runs without asking.</summary>
    public static bool NeedsApproval(string tool) => !Tools.TryGetValue(tool, out var info) || !info.ReadOnly;

    public async Task<AssistantAction> DescribeAsync(string requestId, FunctionCallContent call, CancellationToken ct)
    {
        var (title, _, destructive) = Tools.TryGetValue(call.Name, out var info) ? info : (call.Name, false, true);
        var arguments = (call.Arguments ?? new Dictionary<string, object?>())
            .Where(a => a.Value is not null)
            .ToDictionary(a => a.Key, a => JsonSerializer.SerializeToElement(a.Value, AIJsonUtilities.DefaultOptions));

        var details = new List<ActionDetail>();
        string? text = null;
        foreach (var (name, value) in arguments)
        {
            if (value.ValueKind is JsonValueKind.Null || (value.ValueKind == JsonValueKind.False && name == "unassign"))
            {
                continue;
            }

            if (name == "markdown")
            {
                text = value.GetString();
                continue;
            }

            var shown = await ValueAsync(name, value, arguments, ct);
            details.Add(new ActionDetail(Labels.GetValueOrDefault(name, name), shown));
        }

        return new AssistantAction(requestId, call.Name, title, destructive, details, text);
    }

    private async Task<string> ValueAsync(string name, JsonElement value, Dictionary<string, JsonElement> arguments, CancellationToken ct)
    {
        if (value.ValueKind == JsonValueKind.True)
        {
            return "Ja";
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            var shown = new List<string>();
            foreach (var item in value.EnumerateArray())
            {
                // People to assign are named, not shown as ids.
                shown.Add(name.EndsWith("Ids", StringComparison.Ordinal) && Guid.TryParse(item.ToString(), out var itemId)
                    ? await NameAsync("userId", itemId, ct) ?? "(nicht gefunden)"
                    : item.ToString());
            }

            return string.Join(", ", shown);
        }

        var raw = value.ToString();
        if (name.EndsWith("Id", StringComparison.Ordinal) && Guid.TryParse(raw, out var id))
        {
            var kind = name == "resourceId" && arguments.TryGetValue("resourceType", out var type) ? type.GetString() : name;
            return await NameAsync(kind, id, ct) ?? "(nicht gefunden)";
        }

        if (name.EndsWith("Date", StringComparison.Ordinal) || name == "date")
        {
            return DateOnly.TryParse(raw, CultureInfo.InvariantCulture, out var date) ? date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : raw;
        }

        return Values.GetValueOrDefault(raw, raw);
    }

    private async Task<string?> NameAsync(string? kind, Guid id, CancellationToken ct) => kind switch
    {
        "projectId" or KnowledgeResourceType.Project => (await projects.GetAsync(user, id, ct)).Value?.Name,
        "taskId" or "parentTaskId" or "sourceTaskId" or "targetTaskId" or KnowledgeResourceType.Task => (await tasks.GetAsync(user, id, ct)).Value?.Title,
        "articleId" => (await articles.GetAsync(user, id, ct)).Value?.Article.Title,
        "departmentId" or KnowledgeResourceType.Department => (await departments.GetAsync(user, id, ct)).Value?.Name,
        "spaceId" => (await spaces.ListAsync(user, ct)).FirstOrDefault(s => s.Id == id)?.Name,
        KnowledgeResourceType.Whiteboard => (await whiteboards.GetAsync(user, id, ct)).Value?.Name,
        "userId" => await db.Set<AppUser>().AsNoTracking()
            .Where(u => u.Id == id && u.OrganizationId == user.OrganizationId)
            .Select(u => u.DisplayName)
            .SingleOrDefaultAsync(ct),
        _ => null,
    };
}
