using System.Net;
using System.Net.Http.Json;
using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Modules.Tasks;
using static ProjectHub.Api.Modules.Identity.Development.DevelopmentSeedData;

namespace ProjectHub.Api.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class TaskEndpointTests(InfrastructureFixture infrastructure) : ApiTestBase(infrastructure)
{
    [Fact]
    public async Task Member_creates_and_assigns_a_task()
    {
        var project = await CreateTeamProjectAsync();

        var task = await CreateTaskAsync(David, project.Id, NewTask("Texte schreiben", assigneeId: Clara.Id, progress: 10, priority: "high"));

        Assert.Equal(Clara.Id, task.AssigneeId);
        Assert.Equal("Clara Editor", task.AssigneeName);
        Assert.Equal(David.Id, task.CreatorId);
        Assert.Equal("todo", task.Status);
        Assert.Equal(1L, await ScalarAsync("select count(*) from activity_log where action = 'TaskCreated' and resource_id = $1", task.Id));
    }

    [Theory]
    [InlineData("dev-eva")]
    [InlineData("dev-gina")]
    public async Task Viewers_and_guests_cannot_create_tasks(string objectId)
    {
        var project = await CreateTeamProjectAsync();

        var response = await As(Users.Single(u => u.ObjectId == objectId))
            .PostAsJsonAsync($"/api/v1/projects/{project.Id}/tasks", NewTask("Nicht erlaubt"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Outsiders_cannot_see_tasks()
    {
        var project = await CreateTeamProjectAsync();
        var task = await CreateTaskAsync(Ben, project.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await As(Felix).GetAsync($"/api/v1/tasks/{task.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await As(Fritz).GetAsync($"/api/v1/tasks/{task.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await As(Fritz).GetAsync($"/api/v1/projects/{project.Id}/tasks")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await As(Fritz).PatchAsJsonAsync($"/api/v1/tasks/{task.Id}", new { version = 1, title = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await As(Fritz).DeleteAsync($"/api/v1/tasks/{task.Id}")).StatusCode);
    }

    [Theory]
    [InlineData("", null, null, null, null)]
    [InlineData("Titel", (short)101, null, null, null)]
    [InlineData("Titel", (short)-1, null, null, null)]
    [InlineData("Titel", null, "2026-10-10", "2026-10-01", null)]
    [InlineData("Titel", null, null, null, "critical")]
    public async Task Invalid_task_is_rejected(string title, short? progress, string? start, string? due, string? priority)
    {
        var project = await CreateTeamProjectAsync();
        var request = NewTask(
            title,
            progress: progress,
            startDate: start is null ? null : DateOnly.Parse(start),
            dueDate: due is null ? null : DateOnly.Parse(due),
            priority: priority);

        var response = await As(Ben).PostAsJsonAsync($"/api/v1/projects/{project.Id}/tasks", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("dev-felix")] // not a project member
    [InlineData("dev-eva")] // viewer: may not work on tasks
    [InlineData("dev-fritz")] // other organization
    public async Task Assignee_must_be_allowed_to_work_in_the_project(string objectId)
    {
        var project = await CreateTeamProjectAsync();
        var assignee = Users.Single(u => u.ObjectId == objectId);

        var response = await As(Ben).PostAsJsonAsync($"/api/v1/projects/{project.Id}/tasks", NewTask("Zuweisung", assigneeId: assignee.Id));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Subtasks_belong_to_their_parent_and_list_can_filter_them()
    {
        var project = await CreateTeamProjectAsync();
        var parent = await CreateTaskAsync(Ben, project.Id, NewTask("Oberaufgabe"));
        var child = await CreateTaskAsync(David, project.Id, NewTask("Unteraufgabe", parentTaskId: parent.Id));

        var topLevel = await As(Eva).GetFromJsonAsync<PagedResponse<TaskResponse>>($"/api/v1/projects/{project.Id}/tasks?topLevelOnly=true");
        var children = await As(Eva).GetFromJsonAsync<PagedResponse<TaskResponse>>($"/api/v1/projects/{project.Id}/tasks?parentTaskId={parent.Id}");

        Assert.Equal(parent.Id, Assert.Single(topLevel!.Items).Id);
        Assert.Equal(1, topLevel.Items[0].SubtaskCount);
        Assert.Equal(child.Id, Assert.Single(children!.Items).Id);
    }

    [Fact]
    public async Task Parent_task_must_be_in_the_same_project()
    {
        var project = await CreateTeamProjectAsync();
        var other = await CreateProjectAsync(Ben);
        var foreignParent = await CreateTaskAsync(Ben, other.Id);

        var response = await As(Ben).PostAsJsonAsync($"/api/v1/projects/{project.Id}/tasks", NewTask("Falsch", parentTaskId: foreignParent.Id));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Task_hierarchy_cannot_become_circular()
    {
        var project = await CreateTeamProjectAsync();
        var parent = await CreateTaskAsync(Ben, project.Id, NewTask("A"));
        var child = await CreateTaskAsync(Ben, project.Id, NewTask("B", parentTaskId: parent.Id));

        var self = await As(Ben).PatchAsJsonAsync($"/api/v1/tasks/{parent.Id}", new { version = parent.Version, parentTaskId = parent.Id });
        var cycle = await As(Ben).PatchAsJsonAsync($"/api/v1/tasks/{parent.Id}", new { version = parent.Version, parentTaskId = child.Id });

        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, cycle.StatusCode);
    }

    [Fact]
    public async Task Member_updates_a_task_and_null_clears_a_field()
    {
        var project = await CreateTeamProjectAsync();
        var task = await CreateTaskAsync(Ben, project.Id, NewTask("Aufgabe", assigneeId: David.Id));

        var response = await As(David).PatchAsJsonAsync(
            $"/api/v1/tasks/{task.Id}", new { version = task.Version, status = "in_progress", progress = 40, assigneeId = (Guid?)null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<TaskResponse>();
        Assert.Equal("in_progress", updated!.Status);
        Assert.Equal(40, updated.Progress);
        Assert.Null(updated.AssigneeId);
        Assert.Equal("Aufgabe", updated.Title);
        Assert.Equal(task.Version + 1, updated.Version);
    }

    [Fact]
    public async Task Viewer_cannot_update_a_task()
    {
        var project = await CreateTeamProjectAsync();
        var task = await CreateTaskAsync(Ben, project.Id);

        var response = await As(Eva).PatchAsJsonAsync($"/api/v1/tasks/{task.Id}", new { version = task.Version, title = "x" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Concurrent_update_with_the_same_version_conflicts_and_keeps_the_newer_value()
    {
        var project = await CreateTeamProjectAsync();
        var task = await CreateTaskAsync(Ben, project.Id);

        var first = await As(Clara).PatchAsJsonAsync($"/api/v1/tasks/{task.Id}", new { version = task.Version, title = "Clara war schneller" });
        var second = await As(David).PatchAsJsonAsync($"/api/v1/tasks/{task.Id}", new { version = task.Version, title = "David ist zu spät" });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var current = await As(Ben).GetFromJsonAsync<TaskResponse>($"/api/v1/tasks/{task.Id}");
        Assert.Equal("Clara war schneller", current!.Title);
        Assert.Equal(task.Version + 1, current.Version);
    }

    [Fact]
    public async Task Parallel_updates_never_both_succeed()
    {
        var project = await CreateTeamProjectAsync();
        var task = await CreateTaskAsync(Ben, project.Id);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(i =>
            As(Ben).PatchAsJsonAsync($"/api/v1/tasks/{task.Id}", new { version = task.Version, progress = i * 10 + 1 })));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal(task.Version + 1, await ScalarAsync("select version from task where id = $1", task.Id));
    }

    [Fact]
    public async Task Update_without_version_is_rejected()
    {
        var project = await CreateTeamProjectAsync();
        var task = await CreateTaskAsync(Ben, project.Id);

        var response = await As(Ben).PatchAsJsonAsync($"/api/v1/tasks/{task.Id}", new { title = "x" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_task_removes_it_and_its_subtasks_from_lists()
    {
        var project = await CreateTeamProjectAsync();
        var parent = await CreateTaskAsync(Ben, project.Id, NewTask("Oberaufgabe"));
        var child = await CreateTaskAsync(Ben, project.Id, NewTask("Unteraufgabe", parentTaskId: parent.Id));

        var byMember = await As(David).DeleteAsync($"/api/v1/tasks/{parent.Id}");
        var byEditor = await As(Clara).DeleteAsync($"/api/v1/tasks/{parent.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, byMember.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, byEditor.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await As(Ben).GetAsync($"/api/v1/tasks/{child.Id}")).StatusCode);
        var list = await As(Ben).GetFromJsonAsync<PagedResponse<TaskResponse>>($"/api/v1/projects/{project.Id}/tasks");
        Assert.Empty(list!.Items);
        Assert.Equal(1L, await ScalarAsync("select count(*) from audit_log where action = 'TaskDeleted' and resource_id = $1", parent.Id));
    }

    [Fact]
    public async Task Tasks_of_a_deleted_project_are_inaccessible()
    {
        var project = await CreateTeamProjectAsync();
        var task = await CreateTaskAsync(Ben, project.Id);
        await As(Ben).DeleteAsync($"/api/v1/projects/{project.Id}");

        Assert.Equal(HttpStatusCode.NotFound, (await As(Ben).GetAsync($"/api/v1/tasks/{task.Id}")).StatusCode);
    }

    [Fact]
    public async Task Task_list_is_paged()
    {
        var project = await CreateTeamProjectAsync();
        for (var i = 0; i < 3; i++)
        {
            await CreateTaskAsync(Ben, project.Id, NewTask($"Aufgabe {i}"));
        }

        var page = await As(Ben).GetFromJsonAsync<PagedResponse<TaskResponse>>($"/api/v1/projects/{project.Id}/tasks?limit=2");

        Assert.Equal(2, page!.Items.Count);
        Assert.Equal(2, page.NextOffset);
    }
}
