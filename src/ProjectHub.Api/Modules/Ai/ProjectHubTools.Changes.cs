using System.ComponentModel;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Infrastructure.Outcomes;
using ProjectHub.Api.Modules.Comments;
using ProjectHub.Api.Modules.Departments;
using ProjectHub.Api.Modules.Gantt;
using ProjectHub.Api.Modules.Identity.Authorization;
using ProjectHub.Api.Modules.Knowledge;
using ProjectHub.Api.Modules.Projects;
using ProjectHub.Api.Modules.Tasks;
using ProjectHub.Api.Modules.Whiteboard;

namespace ProjectHub.Api.Modules.Ai;

public sealed record Changed(string Done, Guid? Id = null, string? Title = null);

/// <summary>
/// The tools that change something (ADR 0016). They do what the person could do in the interface, through the same
/// services. The assistant asks the person before every call; MCP clients ask with their own confirmation.
/// </summary>
public sealed partial class ProjectHubTools
{
    // Projects

    [McpServerTool(Name = "create_project", Title = "Projekt anlegen", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Creates a project; the user becomes its admin.")]
    public async Task<object> CreateProject(
        [Description("Name of the project.")] string name,
        [Description("Optional description.")] string? description = null,
        [Description("Optional status: planned (default), active, on_hold, completed.")] string? status = null,
        [Description("Optional start date (yyyy-MM-dd).")] DateOnly? startDate = null,
        [Description("Optional end date (yyyy-MM-dd).")] DateOnly? endDate = null,
        [Description("Optional department id from list_departments; default: the user's first department.")] Guid? departmentId = null,
        CancellationToken ct = default)
    {
        var result = await projects.CreateAsync(user, new CreateProjectRequest(name, description, status, startDate, endDate, departmentId), ct);
        return result.Succeeded ? new Changed("Project created.", result.Value!.Id, result.Value.Name) : Error(result);
    }

    [McpServerTool(Name = "update_project", Title = "Projekt ändern", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Changes name, description, status or dates of a project. Only the given fields change.")]
    public async Task<object> UpdateProject(
        [Description("Project id.")] Guid projectId,
        [Description("New name.")] string? name = null,
        [Description("New description.")] string? description = null,
        [Description("New status: planned, active, on_hold, completed or archived.")] string? status = null,
        [Description("New start date (yyyy-MM-dd).")] DateOnly? startDate = null,
        [Description("New end date (yyyy-MM-dd).")] DateOnly? endDate = null,
        CancellationToken ct = default)
    {
        var current = await projects.GetAsync(user, projectId, ct);
        if (!current.Succeeded)
        {
            return Error(current);
        }

        var patch = Patch(current.Value!.Version, ("name", name), ("description", description), ("status", status), ("startDate", startDate), ("endDate", endDate));
        var result = await projects.UpdateAsync(user, projectId, patch, ct);
        return result.Succeeded ? new Changed("Project updated.", projectId, result.Value!.Name) : Error(result);
    }

    [McpServerTool(Name = "delete_project", Title = "Projekt löschen", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Deletes a project with its tasks. Only when the user asks for exactly this.")]
    public async Task<object> DeleteProject([Description("Project id.")] Guid projectId, CancellationToken ct = default)
    {
        var result = await projects.DeleteAsync(user, projectId, ct);
        return result.Succeeded ? new Changed("Project deleted.", projectId) : Error(result);
    }

    [McpServerTool(Name = "add_project_member", Title = "Projektmitglied hinzufügen", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Adds a person to a project. Find the person with find_people first.")]
    public async Task<object> AddProjectMember(
        [Description("Project id.")] Guid projectId,
        [Description("User id from find_people.")] Guid userId,
        [Description("Role: admin, editor, member (default), viewer or guest.")] string? role = null,
        CancellationToken ct = default)
    {
        var result = await projects.AddMemberAsync(user, projectId, userId, role ?? ProjectRole.Member, ct);
        return result.Succeeded ? new Changed("Member added.", userId) : Error(result);
    }

    [McpServerTool(Name = "change_project_member_role", Title = "Rolle im Projekt ändern", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Changes the role of a project member.")]
    public async Task<object> ChangeProjectMemberRole(
        [Description("Project id.")] Guid projectId,
        [Description("User id of the member.")] Guid userId,
        [Description("New role: admin, editor, member, viewer or guest.")] string role,
        CancellationToken ct = default)
    {
        var result = await projects.ChangeMemberRoleAsync(user, projectId, userId, role, ct);
        return result.Succeeded ? new Changed("Role changed.", userId) : Error(result);
    }

    [McpServerTool(Name = "remove_project_member", Title = "Projektmitglied entfernen", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Removes a person from a project.")]
    public async Task<object> RemoveProjectMember(
        [Description("Project id.")] Guid projectId,
        [Description("User id of the member.")] Guid userId,
        CancellationToken ct = default)
    {
        var result = await projects.RemoveMemberAsync(user, projectId, userId, ct);
        return result.Succeeded ? new Changed("Member removed.", userId) : Error(result);
    }

    // Tasks

    [McpServerTool(Name = "create_task", Title = "Aufgabe anlegen", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Creates a task (or a subtask) in a project.")]
    public async Task<object> CreateTask(
        [Description("Project id from list_projects.")] Guid projectId,
        [Description("Short title of the task.")] string title,
        [Description("Optional description.")] string? description = null,
        [Description("Optional status: todo (default), in_progress or done.")] string? status = null,
        [Description("Optional priority: low, normal, high or urgent.")] string? priority = null,
        [Description("Optional assignees: user ids from find_people.")] Guid[]? assigneeIds = null,
        [Description("Optional parent task id, to create a subtask.")] Guid? parentTaskId = null,
        [Description("Optional start date (yyyy-MM-dd).")] DateOnly? startDate = null,
        [Description("Optional due date (yyyy-MM-dd).")] DateOnly? dueDate = null,
        CancellationToken ct = default)
    {
        var result = await tasks.CreateAsync(
            user, projectId, new CreateTaskRequest(title, description, status, priority, assigneeIds, parentTaskId, startDate, dueDate, null), ct);
        return result.Succeeded ? new Changed("Task created.", result.Value!.Id, result.Value.Title) : Error(result);
    }

    [McpServerTool(Name = "update_task", Title = "Aufgabe ändern", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Changes a task: title, description, status (moves it on the board), priority, assignees or dates. Only the given fields change. Progress is calculated from status and subtasks.")]
    public async Task<object> UpdateTask(
        [Description("Task id.")] Guid taskId,
        [Description("New title.")] string? title = null,
        [Description("New description.")] string? description = null,
        [Description("New status: todo, in_progress or done.")] string? status = null,
        [Description("New priority: low, normal, high or urgent.")] string? priority = null,
        [Description("People to add to the assignees: user ids from find_people.")] Guid[]? addAssigneeIds = null,
        [Description("People to remove from the assignees: user ids from get_task.")] Guid[]? removeAssigneeIds = null,
        [Description("true removes all assignees.")] bool unassign = false,
        [Description("New start date (yyyy-MM-dd).")] DateOnly? startDate = null,
        [Description("New due date (yyyy-MM-dd).")] DateOnly? dueDate = null,
        CancellationToken ct = default)
    {
        var current = await tasks.GetAsync(user, taskId, ct);
        if (!current.Succeeded)
        {
            return Error(current);
        }

        var patch = JsonPatch(
            current.Value!.Version, ("title", title), ("description", description), ("status", status), ("priority", priority),
            ("startDate", startDate), ("dueDate", dueDate));
        if (unassign || addAssigneeIds is { Length: > 0 } || removeAssigneeIds is { Length: > 0 })
        {
            var assignees = unassign ? [] : current.Value.Assignees.Select(a => a.Id).ToList();
            assignees.AddRange(addAssigneeIds ?? []);
            assignees.RemoveAll(id => removeAssigneeIds?.Contains(id) == true);
            patch["assigneeIds"] = new JsonArray(assignees.Distinct().Select(id => (JsonNode)JsonValue.Create(id)).ToArray());
        }

        var result = await tasks.UpdateAsync(user, taskId, PatchDocument.From(patch), ct);
        return result.Succeeded ? new Changed("Task updated.", taskId, result.Value!.Title) : Error(result);
    }

    [McpServerTool(Name = "delete_task", Title = "Aufgabe löschen", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Deletes a task with its subtasks. Only when the user asks for exactly this.")]
    public async Task<object> DeleteTask([Description("Task id.")] Guid taskId, CancellationToken ct = default)
    {
        var result = await tasks.DeleteAsync(user, taskId, ct);
        return result.Succeeded ? new Changed("Task deleted.", taskId) : Error(result);
    }

    [McpServerTool(Name = "add_task_comment", Title = "Aufgabe kommentieren", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Adds a comment to a task as the user.")]
    public async Task<object> AddTaskComment(
        [Description("Task id.")] Guid taskId,
        [Description("Comment text.")] string text,
        CancellationToken ct = default)
    {
        var result = await comments.AddAsync(user, taskId, new CreateCommentRequest(text, null), ct);
        return result.Succeeded ? new Changed("Comment added.", result.Value!.Id) : Error(result);
    }

    [McpServerTool(Name = "add_task_dependency", Title = "Abhängigkeit anlegen", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Links two tasks of the same project in the timeline: the target task depends on the source task.")]
    public async Task<object> AddTaskDependency(
        [Description("Project id.")] Guid projectId,
        [Description("The task that comes first.")] Guid sourceTaskId,
        [Description("The task that depends on it.")] Guid targetTaskId,
        [Description("finish_to_start (default), start_to_start, finish_to_finish or start_to_finish.")] string? dependencyType = null,
        CancellationToken ct = default)
    {
        var result = await gantt.CreateDependencyAsync(
            user, projectId, new CreateDependencyRequest(sourceTaskId, targetTaskId, dependencyType ?? DependencyTypes.FinishToStart), ct);
        return result.Succeeded ? new Changed("Dependency created.", result.Value!.Id) : Error(result);
    }

    [McpServerTool(Name = "create_milestone", Title = "Meilenstein anlegen", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Creates a milestone in the timeline of a project.")]
    public async Task<object> CreateMilestone(
        [Description("Project id.")] Guid projectId,
        [Description("Name of the milestone.")] string name,
        [Description("Date (yyyy-MM-dd).")] DateOnly date,
        CancellationToken ct = default)
    {
        var result = await gantt.CreateMilestoneAsync(user, projectId, new CreateMilestoneRequest(name, date), ct);
        return result.Succeeded ? new Changed("Milestone created.", result.Value!.Id, result.Value.Name) : Error(result);
    }

    [McpServerTool(Name = "create_whiteboard", Title = "Whiteboard anlegen", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Creates an empty whiteboard in a project.")]
    public async Task<object> CreateWhiteboard(
        [Description("Project id.")] Guid projectId,
        [Description("Name of the whiteboard.")] string name,
        CancellationToken ct = default)
    {
        var result = await whiteboards.CreateAsync(user, projectId, new CreateWhiteboardRequest(name), ct);
        return result.Succeeded ? new Changed("Whiteboard created.", result.Value!.Id, result.Value.Name) : Error(result);
    }

    // Knowledge

    [McpServerTool(Name = "create_knowledge_article", Title = "Wissensartikel anlegen", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Writes a new knowledge article as a draft owned by the user. The text is Markdown: headings (#, ##, ###), paragraphs, "
                 + "lists (-, 1.), checklists (- [ ]), quotes (>) and code blocks. Publish it with change_article_status when the user wants that.")]
    public async Task<object> CreateKnowledgeArticle(
        [Description("Title of the article.")] string title,
        [Description("Text of the article in Markdown.")] string markdown,
        [Description("Kind: article (default), how_to, best_practice, process, policy, faq, template, checklist or glossary.")] string? articleType = null,
        [Description("Optional one or two sentence summary.")] string? summary = null,
        [Description("Optional knowledge space id from list_knowledge_spaces.")] Guid? spaceId = null,
        [Description("Optional department id from list_departments; default: the space's department, else the user's first.")] Guid? departmentId = null,
        CancellationToken ct = default)
    {
        var result = await articles.CreateAsync(
            user, new CreateArticleRequest(title, articleType, summary, spaceId, null, MarkdownBlocks.ToContent(markdown), departmentId), ct);
        if (!result.Succeeded)
        {
            return Error(result);
        }

        sources.Add(result.Value!.Article.Id, result.Value.Article.Title);
        return new Changed("Draft article created.", result.Value.Article.Id, result.Value.Article.Title);
    }

    [McpServerTool(Name = "update_knowledge_article", Title = "Wissensartikel ändern", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Changes a knowledge article: title, summary, kind, space, or its whole text (Markdown, replaces the text; links, files, "
                 + "images and references in the article stay). Read it with read_knowledge_article first. Every text change becomes a new version.")]
    public async Task<object> UpdateKnowledgeArticle(
        [Description("Article id.")] Guid articleId,
        [Description("New title.")] string? title = null,
        [Description("New summary.")] string? summary = null,
        [Description("New kind, e.g. how_to or process.")] string? articleType = null,
        [Description("New knowledge space id.")] Guid? spaceId = null,
        [Description("New full text in Markdown.")] string? markdown = null,
        [Description("Short note what changed (for the version history).")] string? changeNote = null,
        CancellationToken ct = default)
    {
        var current = await articles.GetAsync(user, articleId, ct);
        if (!current.Succeeded)
        {
            return Error(current);
        }

        var details = current.Value!;
        var version = details.Article.Version;
        if (title is not null || summary is not null || articleType is not null || spaceId is not null)
        {
            var updated = await articles.UpdateAsync(
                user, articleId, Patch(version, ("title", title), ("summary", summary), ("articleType", articleType), ("spaceId", spaceId)), ct);
            if (!updated.Succeeded)
            {
                return Error(updated);
            }

            details = updated.Value!;
            version = details.Article.Version;
        }

        if (markdown is not null)
        {
            var saved = await articles.SaveContentAsync(
                user, articleId, new SaveContentRequest(version, MarkdownBlocks.ReplaceText(details.Content, markdown), changeNote ?? "Mit dem KI-Assistenten geändert"), ct);
            if (!saved.Succeeded)
            {
                return Error(saved);
            }

            details = saved.Value!;
        }

        sources.Add(articleId, details.Article.Title);
        return new Changed("Article updated.", articleId, details.Article.Title);
    }

    [McpServerTool(Name = "change_article_status", Title = "Status eines Artikels ändern", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Moves a knowledge article through its lifecycle: draft, review, published or archived.")]
    public async Task<object> ChangeArticleStatus(
        [Description("Article id.")] Guid articleId,
        [Description("New status: draft, review, published or archived.")] string status,
        CancellationToken ct = default)
    {
        var current = await articles.GetAsync(user, articleId, ct);
        if (!current.Succeeded)
        {
            return Error(current);
        }

        var result = await articles.ChangeStatusAsync(user, articleId, new ChangeStatusRequest(current.Value!.Article.Version, status), ct);
        return result.Succeeded ? new Changed($"Status is now {result.Value!.Article.Status}.", articleId, result.Value.Article.Title) : Error(result);
    }

    [McpServerTool(Name = "set_article_tags", Title = "Schlagwörter setzen", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Replaces the tags of a knowledge article.")]
    public async Task<object> SetArticleTags(
        [Description("Article id.")] Guid articleId,
        [Description("All tags the article should have.")] IReadOnlyList<string> tags,
        CancellationToken ct = default)
    {
        var result = await articles.SetTagsAsync(user, articleId, new SetTagsRequest(tags), ct);
        return result.Succeeded ? new Changed("Tags set.", articleId, result.Value!.Article.Title) : Error(result);
    }

    [McpServerTool(Name = "link_article", Title = "Artikel verknüpfen", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Links a knowledge article to a project, task, department or whiteboard, so it shows up there.")]
    public async Task<object> LinkArticle(
        [Description("Article id.")] Guid articleId,
        [Description("project, task, department or whiteboard.")] string resourceType,
        [Description("Id of the project, task, department or whiteboard.")] Guid resourceId,
        CancellationToken ct = default)
    {
        var result = await links.AddReferenceAsync(user, articleId, new CreateReferenceRequest(resourceType, resourceId), ct);
        return result.Succeeded ? new Changed("Article linked.", result.Value!.Id, result.Value.Title) : Error(result);
    }

    [McpServerTool(Name = "add_article_comment", Title = "Artikel kommentieren", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Adds a comment to a knowledge article as the user.")]
    public async Task<object> AddArticleComment(
        [Description("Article id.")] Guid articleId,
        [Description("Comment text.")] string text,
        CancellationToken ct = default)
    {
        var result = await articleComments.AddAsync(user, articleId, new CreateKnowledgeCommentRequest(text, null), ct);
        return result.Succeeded ? new Changed("Comment added.", result.Value!.Id) : Error(result);
    }

    [McpServerTool(Name = "delete_knowledge_article", Title = "Wissensartikel löschen", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Deletes a knowledge article. Only when the user asks for exactly this.")]
    public async Task<object> DeleteKnowledgeArticle([Description("Article id.")] Guid articleId, CancellationToken ct = default)
    {
        var result = await articles.DeleteAsync(user, articleId, ct);
        return result.Succeeded ? new Changed("Article deleted.", articleId) : Error(result);
    }

    // Departments

    [McpServerTool(Name = "create_department", Title = "Abteilung anlegen", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Creates a department (organization admins only).")]
    public async Task<object> CreateDepartment(
        [Description("Name of the department.")] string name,
        [Description("Optional description.")] string? description = null,
        CancellationToken ct = default)
    {
        var result = await departments.CreateAsync(user, new CreateDepartmentRequest(name, description), ct);
        return result.Succeeded ? new Changed("Department created.", result.Value!.Id, result.Value.Name) : Error(result);
    }

    [McpServerTool(Name = "add_department_member", Title = "Person in Abteilung aufnehmen", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Adds a person to a department (organization admins and the department's leads). Find the person with find_people first.")]
    public async Task<object> AddDepartmentMember(
        [Description("Department id from list_departments.")] Guid departmentId,
        [Description("User id from find_people.")] Guid userId,
        [Description("lead, member (default) or guest.")] string? role = null,
        CancellationToken ct = default)
    {
        var result = await departments.AddMemberAsync(user, departmentId, new AddDepartmentMemberRequest(userId, role), ct);
        return result.Succeeded ? new Changed("Member added.", userId) : Error(result);
    }

    /// <summary>A merge-patch body with the version and the given fields; null values are left out (unchanged).</summary>
    private static JsonObject JsonPatch(long version, params (string Name, object? Value)[] fields)
    {
        var patch = new JsonObject { ["version"] = version };
        foreach (var (name, value) in fields)
        {
            if (value is not null)
            {
                patch[name] = JsonValue.Create(value);
            }
        }

        return patch;
    }

    private static PatchDocument Patch(long version, params (string Name, object? Value)[] fields) => PatchDocument.From(JsonPatch(version, fields));
}
