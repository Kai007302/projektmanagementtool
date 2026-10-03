using System.Net;
using System.Net.Http.Json;
using ProjectHub.Api.Modules.Kanban;
using ProjectHub.Api.Modules.Tasks;
using static ProjectHub.Api.Modules.Identity.Development.DevelopmentSeedData;

namespace ProjectHub.Api.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class KanbanEndpointTests(InfrastructureFixture infrastructure) : ApiTestBase(infrastructure)
{
    [Fact]
    public async Task First_visit_creates_the_board_with_one_column_per_status()
    {
        var project = await CreateTeamProjectAsync();

        var board = await GetBoardAsync(Eva, project.Id);

        Assert.Equal(["Offen", "In Arbeit", "Erledigt"], board.Columns.Select(c => c.Name));
        Assert.Equal(["todo", "in_progress", "done"], board.Columns.Select(c => c.TaskStatus));
        Assert.Equal(board.Id, (await GetBoardAsync(Ben, project.Id)).Id);
    }

    [Fact]
    public async Task Parallel_first_visits_create_a_single_board()
    {
        var project = await CreateTeamProjectAsync();

        var boards = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => GetBoardAsync(Ben, project.Id)));

        Assert.Single(boards.Select(b => b.Id).Distinct());
        Assert.Equal(1L, await ScalarAsync("select count(*) from kanban_board where project_id = $1", project.Id));
        Assert.Equal(3L, await ScalarAsync("select count(*) from kanban_column c join kanban_board b on b.id = c.board_id where b.project_id = $1", project.Id));
    }

    [Theory]
    [InlineData("dev-felix")]
    [InlineData("dev-fritz")]
    public async Task Outsiders_cannot_see_the_board(string objectId)
    {
        var project = await CreateTeamProjectAsync();

        var response = await As(Users.Single(u => u.ObjectId == objectId)).GetAsync($"/api/v1/projects/{project.Id}/board");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Cards_are_top_level_tasks_in_the_column_of_their_status()
    {
        var project = await CreateTeamProjectAsync();
        var open = await CreateTaskAsync(Ben, project.Id, NewTask("Offen"));
        var running = await CreateTaskAsync(Ben, project.Id, NewTask("Läuft", status: "in_progress"));
        await CreateTaskAsync(Ben, project.Id, NewTask("Unteraufgabe", parentTaskId: open.Id));

        var board = await GetBoardAsync(Ben, project.Id);

        var card = Assert.Single(board.Columns[0].Cards);
        Assert.Equal(open.Id, card.Id);
        Assert.Equal(1, card.SubtaskCount);
        Assert.Equal(running.Id, Assert.Single(board.Columns[1].Cards).Id);
        Assert.Empty(board.Columns[2].Cards);
    }

    [Fact]
    public async Task Moving_a_card_to_another_column_changes_the_task_status()
    {
        var project = await CreateTeamProjectAsync();
        var task = await CreateTaskAsync(Ben, project.Id);
        var board = await GetBoardAsync(David, project.Id);

        var response = await MoveAsync(David, task, board.Columns[2].Id, 0);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var moved = (await response.Content.ReadFromJsonAsync<KanbanBoardResponse>())!;
        Assert.Equal(task.Id, Assert.Single(moved.Columns[2].Cards).Id);
        var current = await As(Ben).GetFromJsonAsync<TaskResponse>($"/api/v1/tasks/{task.Id}");
        Assert.Equal("done", current!.Status);
        Assert.Equal(task.Version + 1, current.Version);
        Assert.Equal(1L, await ScalarAsync("select count(*) from activity_log where action = 'TaskMoved' and resource_id = $1", task.Id));
    }

    [Fact]
    public async Task Reordering_within_a_column_keeps_the_task_version()
    {
        var project = await CreateTeamProjectAsync();
        var a = await CreateTaskAsync(Ben, project.Id, NewTask("A"));
        var b = await CreateTaskAsync(Ben, project.Id, NewTask("B"));
        var c = await CreateTaskAsync(Ben, project.Id, NewTask("C"));
        var board = await GetBoardAsync(Ben, project.Id);

        var first = await MoveAsync(Ben, c, board.Columns[0].Id, 0);
        var second = await MoveAsync(Ben, a, board.Columns[0].Id, 99);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var reordered = (await second.Content.ReadFromJsonAsync<KanbanBoardResponse>())!;
        Assert.Equal([c.Id, b.Id, a.Id], reordered.Columns[0].Cards.Select(card => card.Id));
        Assert.Equal(c.Version, reordered.Columns[0].Cards[0].Version);
        Assert.Equal(0L, await ScalarAsync("select count(*) from activity_log where action = 'TaskMoved' and project_id = $1", project.Id));
    }

    [Fact]
    public async Task Status_change_through_the_task_moves_the_card()
    {
        var project = await CreateTeamProjectAsync();
        var task = await CreateTaskAsync(Ben, project.Id);
        var board = await GetBoardAsync(Ben, project.Id);
        var moved = (await (await MoveAsync(Ben, task, board.Columns[1].Id, 0)).Content.ReadFromJsonAsync<KanbanBoardResponse>())!;

        await As(Ben).PatchAsJsonAsync($"/api/v1/tasks/{task.Id}", new { version = moved.Columns[1].Cards[0].Version, status = "todo" });

        Assert.Equal(task.Id, Assert.Single((await GetBoardAsync(Ben, project.Id)).Columns[0].Cards).Id);
    }

    [Theory]
    [InlineData("dev-eva")]
    [InlineData("dev-gina")]
    public async Task Viewers_and_guests_cannot_move_cards(string objectId)
    {
        var project = await CreateTeamProjectAsync();
        var task = await CreateTaskAsync(Ben, project.Id);
        var board = await GetBoardAsync(Ben, project.Id);

        var response = await MoveAsync(Users.Single(u => u.ObjectId == objectId), task, board.Columns[1].Id, 0);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Outsiders_cannot_move_cards()
    {
        var project = await CreateTeamProjectAsync();
        var task = await CreateTaskAsync(Ben, project.Id);
        var board = await GetBoardAsync(Ben, project.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await MoveAsync(Fritz, task, board.Columns[1].Id, 0)).StatusCode);
    }

    [Fact]
    public async Task Moving_with_an_outdated_version_conflicts()
    {
        var project = await CreateTeamProjectAsync();
        var task = await CreateTaskAsync(Ben, project.Id);
        var board = await GetBoardAsync(Ben, project.Id);
        await As(Clara).PatchAsJsonAsync($"/api/v1/tasks/{task.Id}", new { version = task.Version, title = "Neu" });

        var response = await MoveAsync(David, task, board.Columns[1].Id, 0);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("todo", (await As(Ben).GetFromJsonAsync<TaskResponse>($"/api/v1/tasks/{task.Id}"))!.Status);
    }

    [Fact]
    public async Task Parallel_moves_keep_a_consistent_order()
    {
        var project = await CreateTeamProjectAsync();
        var tasks = new List<TaskResponse>();
        for (var i = 0; i < 6; i++)
        {
            tasks.Add(await CreateTaskAsync(Ben, project.Id, NewTask($"Aufgabe {i}")));
        }

        var board = await GetBoardAsync(Ben, project.Id);
        var responses = await Task.WhenAll(tasks.Select(t => MoveAsync(Ben, t, board.Columns[0].Id, 0)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var positions = await ScalarAsync(
            "select count(distinct board_position) from task where project_id = $1 and board_position is not null", project.Id);
        Assert.Equal(6L, positions);
    }

    [Fact]
    public async Task Column_of_another_board_is_rejected()
    {
        var project = await CreateTeamProjectAsync();
        var other = await CreateProjectAsync(Ben);
        var task = await CreateTaskAsync(Ben, project.Id);
        var foreign = await GetBoardAsync(Ben, other.Id);

        Assert.Equal(HttpStatusCode.BadRequest, (await MoveAsync(Ben, task, foreign.Columns[1].Id, 0)).StatusCode);
    }

    [Fact]
    public async Task Move_requires_version_column_and_index()
    {
        var project = await CreateTeamProjectAsync();
        var task = await CreateTaskAsync(Ben, project.Id);
        var board = await GetBoardAsync(Ben, project.Id);

        var noVersion = await As(Ben).PostAsJsonAsync($"/api/v1/tasks/{task.Id}/move", new MoveTaskRequest(null, board.Columns[1].Id, 0));
        var negative = await As(Ben).PostAsJsonAsync($"/api/v1/tasks/{task.Id}/move", new MoveTaskRequest(task.Version, board.Columns[1].Id, -1));

        Assert.Equal(HttpStatusCode.BadRequest, noVersion.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);
    }

    [Fact]
    public async Task Editor_adds_renames_and_reorders_columns()
    {
        var project = await CreateTeamProjectAsync();
        await GetBoardAsync(Ben, project.Id);

        var created = await As(Clara).PostAsJsonAsync($"/api/v1/projects/{project.Id}/board/columns", new CreateColumnRequest("Review", "in_progress", 3));
        var board = (await created.Content.ReadFromJsonAsync<KanbanBoardResponse>())!;
        var review = board.Columns[^1];
        var renamed = await As(Clara).PatchAsJsonAsync($"/api/v1/board-columns/{review.Id}", new { version = review.Version, name = "Prüfung" });
        var moved = await As(Clara).PostAsJsonAsync($"/api/v1/board-columns/{review.Id}/move", new MoveColumnRequest(2));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(("Review", 3), (review.Name, review.WipLimit));
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var final = (await moved.Content.ReadFromJsonAsync<KanbanBoardResponse>())!;
        Assert.Equal(["Offen", "In Arbeit", "Prüfung", "Erledigt"], final.Columns.Select(c => c.Name));
    }

    [Fact]
    public async Task Members_cannot_change_columns()
    {
        var project = await CreateTeamProjectAsync();
        var board = await GetBoardAsync(Ben, project.Id);

        var create = await As(David).PostAsJsonAsync($"/api/v1/projects/{project.Id}/board/columns", new CreateColumnRequest("X", "todo", null));
        var rename = await As(David).PatchAsJsonAsync($"/api/v1/board-columns/{board.Columns[0].Id}", new { version = 1, name = "X" });
        var delete = await As(David).DeleteAsync($"/api/v1/board-columns/{board.Columns[0].Id}");
        var outsider = await As(Fritz).DeleteAsync($"/api/v1/board-columns/{board.Columns[0].Id}");

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, rename.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, outsider.StatusCode);
    }

    [Theory]
    [InlineData("", "todo", null)]
    [InlineData("Spalte", "blocked", null)]
    [InlineData("Spalte", "todo", 0)]
    public async Task Invalid_column_is_rejected(string name, string status, int? wipLimit)
    {
        var project = await CreateTeamProjectAsync();

        var response = await As(Ben).PostAsJsonAsync($"/api/v1/projects/{project.Id}/board/columns", new CreateColumnRequest(name, status, wipLimit));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Stale_column_update_conflicts()
    {
        var project = await CreateTeamProjectAsync();
        var column = (await GetBoardAsync(Ben, project.Id)).Columns[0];
        await As(Ben).PatchAsJsonAsync($"/api/v1/board-columns/{column.Id}", new { version = column.Version, name = "Backlog" });

        var stale = await As(Clara).PatchAsJsonAsync($"/api/v1/board-columns/{column.Id}", new { version = column.Version, wipLimit = 5 });

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    [Fact]
    public async Task Every_status_keeps_at_least_one_column()
    {
        var project = await CreateTeamProjectAsync();
        var column = (await GetBoardAsync(Ben, project.Id)).Columns[0];

        var delete = await As(Ben).DeleteAsync($"/api/v1/board-columns/{column.Id}");
        var restatus = await As(Ben).PatchAsJsonAsync($"/api/v1/board-columns/{column.Id}", new { version = column.Version, taskStatus = "done" });

        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, restatus.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_column_moves_its_cards_to_another_column_of_the_status()
    {
        var project = await CreateTeamProjectAsync();
        var task = await CreateTaskAsync(Ben, project.Id);
        await GetBoardAsync(Ben, project.Id);
        var withReview = (await (await As(Ben).PostAsJsonAsync(
            $"/api/v1/projects/{project.Id}/board/columns", new CreateColumnRequest("Review", "in_progress", null)))
            .Content.ReadFromJsonAsync<KanbanBoardResponse>())!;
        var review = withReview.Columns[^1];
        await MoveAsync(Ben, task, review.Id, 0);

        var response = await As(Ben).DeleteAsync($"/api/v1/board-columns/{review.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var board = await GetBoardAsync(Ben, project.Id);
        Assert.Equal(task.Id, Assert.Single(board.Columns.Single(c => c.Name == "In Arbeit").Cards).Id);
        Assert.Equal(1L, await ScalarAsync("select count(*) from audit_log where action = 'BoardColumnDeleted' and resource_id = $1", review.Id));
    }

    [Fact]
    public async Task Deleting_the_project_hides_its_board()
    {
        var project = await CreateTeamProjectAsync();
        var board = await GetBoardAsync(Ben, project.Id);
        await As(Ben).DeleteAsync($"/api/v1/projects/{project.Id}");

        Assert.Equal(HttpStatusCode.NotFound, (await As(Ben).GetAsync($"/api/v1/projects/{project.Id}/board")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await As(Ben).DeleteAsync($"/api/v1/board-columns/{board.Columns[0].Id}")).StatusCode);
    }

    private async Task<KanbanBoardResponse> GetBoardAsync(SeedUser user, Guid projectId)
    {
        var response = await As(user).GetAsync($"/api/v1/projects/{projectId}/board");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<KanbanBoardResponse>())!;
    }

    private Task<HttpResponseMessage> MoveAsync(SeedUser user, TaskResponse task, Guid columnId, int index) =>
        As(user).PostAsJsonAsync($"/api/v1/tasks/{task.Id}/move", new MoveTaskRequest(task.Version, columnId, index));
}
