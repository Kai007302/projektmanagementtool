using System.ComponentModel;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Infrastructure.Outcomes;
using ProjectHub.Api.Modules.Comments;
using ProjectHub.Api.Modules.Gantt;
using ProjectHub.Api.Modules.Identity;
using ProjectHub.Api.Modules.Knowledge;
using ProjectHub.Api.Modules.Projects;
using ProjectHub.Api.Modules.Tasks;
using ProjectHub.Api.Modules.Departments;
using ProjectHub.Api.Modules.Users;
using ProjectHub.Api.Modules.Whiteboard;

namespace ProjectHub.Api.Modules.Ai;

public sealed record ToolError(string Error);

public sealed record KnowledgeHit(Guid ArticleId, string Title, string? Space, string Passage);

public sealed record KnowledgeArticleText(
    Guid ArticleId, string Title, string ArticleType, string Status, string? Space, string? Summary, DateTimeOffset UpdatedAt, string Text, bool Truncated);

public sealed record ProjectItem(Guid ProjectId, string Name, string Status, DateOnly? StartDate, DateOnly? EndDate, string? MyRole);

public sealed record ProjectOverview(
    Guid ProjectId, string Name, string? Description, string Status, DateOnly? StartDate, DateOnly? EndDate,
    IReadOnlyList<string> Members, int OpenTasks, int TasksInProgress, int DoneTasks, int OverdueTasks);

public sealed record TaskItem(
    Guid TaskId, Guid ProjectId, string Title, string Status, string Priority, IReadOnlyList<string> Assignees, DateOnly? StartDate,
    DateOnly? DueDate, short Progress, int Subtasks);

public sealed record TaskDetailsItem(
    Guid TaskId, Guid ProjectId, string Title, string? Description, string Status, string Priority, IReadOnlyList<PersonRef> Assignees,
    DateOnly? StartDate, DateOnly? DueDate, short Progress, IReadOnlyList<string> RecentComments);

public sealed record PersonRef(Guid UserId, string Name);

public sealed record PersonItem(Guid UserId, string Name, string Email, string? Department);

public sealed record DepartmentItem(Guid DepartmentId, string Name, string? Description, int Members, string? MyRole);

public sealed record SpaceItem(Guid SpaceId, string Name, string? Description);

/// <summary>Knowledge articles the assistant looked at during one request, the sources of its answer.</summary>
public sealed class AssistantSources
{
    private readonly Dictionary<Guid, string> _articles = [];

    public IReadOnlyList<(Guid Id, string Title)> Articles => _articles.Select(a => (a.Key, a.Value)).ToList();

    public void Add(Guid articleId, string title)
    {
        lock (_articles)
        {
            _articles.TryAdd(articleId, title);
        }
    }
}

/// <summary>
/// What language models may do in ProjectHub (ADR 0015): the same tools serve the built-in assistant (as
/// <see cref="AIFunction"/>s) and external agents (as MCP tools). Every tool runs as the signed-in person
/// through the existing services, so authorization, validation and audit are the ones of the API. Results are
/// small records; failures come back as <see cref="ToolError"/> so the model can react instead of the request failing.
/// </summary>
[McpServerToolType]
public sealed partial class ProjectHubTools(
    UserContext user,
    ProjectHubDbContext db,
    KnowledgeAccess knowledgeAccess,
    IKnowledgeRetrieval retrieval,
    ProjectService projects,
    TaskService tasks,
    CommentService comments,
    KnowledgeArticleService articles,
    KnowledgeCommentService articleComments,
    KnowledgeLinkService links,
    KnowledgeSpaceService spaces,
    GanttService gantt,
    DepartmentService departments,
    WhiteboardService whiteboards,
    AssistantSources sources,
    TimeProvider clock)
{
    public const int MaxArticleChars = 20_000;

    /// <summary>Results go to a model, not into HTML: umlauts stay readable instead of \u escapes (fewer tokens).</summary>
    public static readonly JsonSerializerOptions SerializerOptions = new(AIJsonUtilities.DefaultOptions)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    private const int MaxListSize = 50;

    [McpServerTool(Name = "search_knowledge", Title = "Wissen durchsuchen", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Searches the knowledge hub (articles, how-tos, decisions) the user may read. Returns matching passages with "
                 + "article ids. Use it before answering any question about processes, decisions or documentation, and cite the articles.")]
    public async Task<object> SearchKnowledge(
        [Description("Search words or a question, preferably in German.")] string query,
        [Description("Maximum number of passages (1-20, default 8).")] int limit = 8,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new ToolError("query must not be empty.");
        }

        var reader = await knowledgeAccess.ReaderAsync(user, ct);
        var passages = await retrieval.RetrieveAsync(reader, query, limit, ct);
        foreach (var passage in passages)
        {
            sources.Add(passage.ArticleId, passage.Title);
        }

        return passages.Select(p => new KnowledgeHit(p.ArticleId, p.Title, p.SpaceName, p.Text)).ToList();
    }

    [McpServerTool(Name = "read_knowledge_article", Title = "Wissensartikel lesen", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Reads one knowledge article by id as Markdown, when the passages from search_knowledge are not enough or before changing it.")]
    public async Task<object> ReadKnowledgeArticle(
        [Description("Article id from search_knowledge.")] Guid articleId,
        CancellationToken ct = default)
    {
        var result = await articles.GetAsync(user, articleId, ct);
        if (!result.Succeeded)
        {
            return new ToolError("Article not found or not readable for the user.");
        }

        var article = result.Value!.Article;
        sources.Add(article.Id, article.Title);
        var text = MarkdownBlocks.FromContent(result.Value.Content);
        var truncated = text.Length > MaxArticleChars;
        return new KnowledgeArticleText(
            article.Id, article.Title, article.ArticleType, article.Status, article.SpaceName, article.Summary, article.UpdatedAt,
            truncated ? text[..MaxArticleChars] : text, truncated);
    }

    [McpServerTool(Name = "list_projects", Title = "Projekte auflisten", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Lists the projects the user can see, with their role in each.")]
    public async Task<object> ListProjects(CancellationToken ct = default)
    {
        var rows = await projects.ListAsync(user, new Paging(0, MaxListSize), ct);
        return rows.Take(MaxListSize).Select(p => new ProjectItem(p.Id, p.Name, p.Status, p.StartDate, p.EndDate, p.MyRole)).ToList();
    }

    [McpServerTool(Name = "get_project", Title = "Projekt ansehen", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Shows one project: description, dates, members and task counts (open, in progress, done, overdue).")]
    public async Task<object> GetProject([Description("Project id from list_projects.")] Guid projectId, CancellationToken ct = default)
    {
        var result = await projects.GetAsync(user, projectId, ct);
        if (!result.Succeeded)
        {
            return Error(result);
        }

        var project = result.Value!;
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var counts = await tasks.CountsAsync(user, projectId, today, ct);
        return new ProjectOverview(
            project.Id, project.Name, project.Description, project.Status, project.StartDate, project.EndDate,
            project.Members.Select(m => $"{m.DisplayName} ({m.Role})").ToList(), counts.Todo, counts.InProgress, counts.Done, counts.Overdue);
    }

    [McpServerTool(Name = "list_tasks", Title = "Aufgaben auflisten", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Lists top-level tasks of a project, optionally filtered by status or to the user's own tasks.")]
    public async Task<object> ListTasks(
        [Description("Project id from list_projects.")] Guid projectId,
        [Description("Optional status filter: todo, in_progress or done.")] string? status = null,
        [Description("Only tasks assigned to the user.")] bool assignedToMe = false,
        CancellationToken ct = default)
    {
        if (status is not null && !Tasks.TaskStatus.All.Contains(status))
        {
            return new ToolError("status must be todo, in_progress or done.");
        }

        var filter = new TaskFilter(null, TopLevelOnly: true, status, assignedToMe ? user.UserId : null);
        var result = await tasks.ListAsync(user, projectId, filter, new Paging(0, MaxListSize), ct);
        return result.Succeeded
            ? result.Value!.Take(MaxListSize).Select(t => new TaskItem(
                t.Id, t.ProjectId, t.Title, t.Status, t.Priority, t.Assignees.Select(a => a.DisplayName).ToList(), t.StartDate, t.DueDate, t.Progress,
                t.SubtaskCount)).ToList()
            : Error(result);
    }

    [McpServerTool(Name = "get_task", Title = "Aufgabe ansehen", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Shows one task with its description and the latest comments.")]
    public async Task<object> GetTask([Description("Task id.")] Guid taskId, CancellationToken ct = default)
    {
        var result = await tasks.GetAsync(user, taskId, ct);
        if (!result.Succeeded)
        {
            return Error(result);
        }

        var task = result.Value!;
        var recent = await comments.ListAsync(user, taskId, new Paging(0, 10), ct);
        return new TaskDetailsItem(
            task.Id, task.ProjectId, task.Title, task.Description, task.Status, task.Priority,
            task.Assignees.Select(a => new PersonRef(a.Id, a.DisplayName)).ToList(), task.StartDate, task.DueDate,
            task.Progress, recent.Succeeded ? recent.Value!.Take(10).Select(c => $"{c.AuthorName}: {c.Content}").ToList() : []);
    }

    [McpServerTool(Name = "find_people", Title = "Personen suchen", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Finds people of the organization by name or e-mail, e.g. to assign a task or add a project member.")]
    public async Task<object> FindPeople(
        [Description("Part of the name or e-mail address.")] string search,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return new ToolError("search must not be empty.");
        }

        var pattern = $"%{search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
        return await db.Set<AppUser>().AsNoTracking()
            .Where(u => u.OrganizationId == user.OrganizationId && u.Status == UserStatus.Active)
            .Where(u => EF.Functions.ILike(u.DisplayName, pattern, "\\") || EF.Functions.ILike(u.Email, pattern, "\\"))
            .OrderBy(u => u.DisplayName)
            .Take(20)
            .Select(u => new PersonItem(u.Id, u.DisplayName, u.Email, u.Department))
            .ToListAsync(ct);
    }

    [McpServerTool(Name = "list_departments", Title = "Abteilungen auflisten", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Lists the departments of the organization, with the user's role in each (lead, member, guest or none).")]
    public async Task<object> ListDepartments(CancellationToken ct = default) =>
        (await departments.ListAsync(user, new Paging(0, MaxListSize), ct))
        .Select(d => new DepartmentItem(d.Id, d.Name, d.Description, d.MemberCount, d.MyRole)).ToList();

    [McpServerTool(Name = "list_knowledge_spaces", Title = "Wissenskategorien auflisten", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Lists the knowledge spaces (sections of the knowledge hub) an article can belong to.")]
    public async Task<object> ListKnowledgeSpaces(CancellationToken ct = default) =>
        (await spaces.ListAsync(user, ct)).Select(s => new SpaceItem(s.Id, s.Name, s.Description)).ToList();

    /// <summary>
    /// The tools as <see cref="AIFunction"/>s for a chat client; write tools only when asked for, and then wrapped so that
    /// the function-invoking client asks for approval instead of running them (ADR 0016).
    /// </summary>
    public IReadOnlyList<AITool> AsAiFunctions(bool includeWriteTools) =>
        Methods(includeWriteTools)
            .Select(m =>
            {
                var function = AIFunctionFactory.Create(m.Method, this, new AIFunctionFactoryOptions { Name = m.Attribute.Name, SerializerOptions = SerializerOptions });
                return m.Attribute.ReadOnly ? (AITool)function : new ApprovalRequiredAIFunction(function);
            })
            .ToList();

    /// <summary>Tool methods with their MCP metadata; read-only ones unless <paramref name="includeWriteTools"/>.</summary>
    public static IEnumerable<(MethodInfo Method, McpServerToolAttribute Attribute)> Methods(bool includeWriteTools) =>
        typeof(ProjectHubTools).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => (Method: m, Attribute: m.GetCustomAttribute<McpServerToolAttribute>()))
            .Where(m => m.Attribute is not null && (includeWriteTools || m.Attribute.ReadOnly))
            .Select(m => (m.Method, m.Attribute!));

    private static ToolError Error<T>(ServiceResult<T> result) => new(result.Error switch
    {
        ServiceError.NotFound => $"{result.Message ?? "Resource"} not found or not visible for the user.",
        ServiceError.Forbidden => "The user is not allowed to do this.",
        ServiceError.Validation => $"Invalid {result.Field}: {result.Message}",
        _ => result.Message ?? "The request could not be completed.",
    });
}
