using System.Net;
using System.Net.Http.Json;
using ProjectHub.Api.Modules.Gantt;
using ProjectHub.Api.Modules.Tasks;
using static ProjectHub.Api.Modules.Identity.Development.DevelopmentSeedData;

namespace ProjectHub.Api.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class GanttEndpointTests(InfrastructureFixture infrastructure) : ApiTestBase(infrastructure)
{
    private static readonly DateOnly Day = new(2026, 11, 2);

    [Fact]
    public async Task Gantt_shows_all_active_tasks_with_their_dates()
    {
        var project = await CreateTeamProjectAsync();
        var parent = await CreateTaskAsync(Ben, project.Id, NewTask("Phase", startDate: Day, dueDate: Day.AddDays(9)));
        var child = await CreateTaskAsync(Ben, project.Id, NewTask("Schritt", parentTaskId: parent.Id, dueDate: Day.AddDays(2), status: "in_progress"));
        var undated = await CreateTaskAsync(Ben, project.Id, NewTask("Ohne Termin"));
        var deleted = await CreateTaskAsync(Ben, project.Id, NewTask("Gelöscht"));
        await As(Ben).DeleteAsync($"/api/v1/tasks/{deleted.Id}");

        var gantt = await GetAsync(Eva, project.Id);

        Assert.Equal([parent.Id, child.Id, undated.Id], gantt.Tasks.Select(t => t.Id));
        var bar = gantt.Tasks[0];
        // The progress of the phase comes from its subtask in progress (ADR 0022).
        Assert.Equal((Day, Day.AddDays(9), (short)50, parent.Version), (bar.StartDate!.Value, bar.DueDate!.Value, bar.Progress, bar.Version));
        Assert.Equal(parent.Id, gantt.Tasks[1].ParentTaskId);
        Assert.Null(gantt.Tasks[2].StartDate);
    }

    [Theory]
    [InlineData("dev-felix")]
    [InlineData("dev-fritz")]
    public async Task Outsiders_cannot_see_the_gantt_chart(string objectId)
    {
        var project = await CreateTeamProjectAsync();

        var response = await As(Users.Single(u => u.ObjectId == objectId)).GetAsync($"/api/v1/projects/{project.Id}/gantt");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Moving_a_bar_uses_the_task_version()
    {
        var project = await CreateTeamProjectAsync();
        var task = await CreateTaskAsync(Ben, project.Id, NewTask("Balken", startDate: Day, dueDate: Day.AddDays(3)));

        var moved = await As(David).PatchAsJsonAsync($"/api/v1/tasks/{task.Id}",
            new { version = task.Version, startDate = Day.AddDays(5), dueDate = Day.AddDays(8) });
        var stale = await As(Clara).PatchAsJsonAsync($"/api/v1/tasks/{task.Id}",
            new { version = task.Version, dueDate = Day.AddDays(20) });

        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var bar = Assert.Single((await GetAsync(Eva, project.Id)).Tasks);
        Assert.Equal((Day.AddDays(5), Day.AddDays(8)), (bar.StartDate!.Value, bar.DueDate!.Value));
    }

    [Fact]
    public async Task Members_link_tasks_and_viewers_cannot()
    {
        var project = await CreateTeamProjectAsync();
        var first = await CreateTaskAsync(Ben, project.Id, NewTask("Erst"));
        var second = await CreateTaskAsync(Ben, project.Id, NewTask("Dann"));

        var viewer = await LinkAsync(Eva, project.Id, first.Id, second.Id);
        var guest = await LinkAsync(Gina, project.Id, first.Id, second.Id);
        var member = await LinkAsync(David, project.Id, first.Id, second.Id);

        Assert.Equal(HttpStatusCode.Forbidden, viewer.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, guest.StatusCode);
        Assert.Equal(HttpStatusCode.Created, member.StatusCode);
        var created = (await member.Content.ReadFromJsonAsync<GanttDependency>())!;
        Assert.Equal((first.Id, second.Id, DependencyTypes.FinishToStart, false),
            (created.SourceTaskId, created.TargetTaskId, created.DependencyType, created.Violated));
        Assert.Equal(created.Id, Assert.Single((await GetAsync(Eva, project.Id)).Dependencies).Id);
        Assert.Equal(1L, await ScalarAsync("select count(*) from activity_log where action = 'DependencyAdded' and resource_id = $1", created.Id));

        Assert.Equal(HttpStatusCode.Forbidden, (await As(Eva).DeleteAsync($"/api/v1/task-dependencies/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await As(Felix).DeleteAsync($"/api/v1/task-dependencies/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await As(David).DeleteAsync($"/api/v1/task-dependencies/{created.Id}")).StatusCode);
        Assert.Empty((await GetAsync(Eva, project.Id)).Dependencies);
    }

    [Fact]
    public async Task Duplicate_and_reverse_links_are_conflicts()
    {
        var project = await CreateTeamProjectAsync();
        var a = await CreateTaskAsync(Ben, project.Id, NewTask("A"));
        var b = await CreateTaskAsync(Ben, project.Id, NewTask("B"));
        Assert.Equal(HttpStatusCode.Created, (await LinkAsync(Ben, project.Id, a.Id, b.Id)).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await LinkAsync(Ben, project.Id, a.Id, b.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await LinkAsync(Ben, project.Id, a.Id, b.Id, DependencyTypes.StartToStart)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await LinkAsync(Ben, project.Id, b.Id, a.Id)).StatusCode);
    }

    [Fact]
    public async Task Cycles_are_rejected()
    {
        var project = await CreateTeamProjectAsync();
        var a = await CreateTaskAsync(Ben, project.Id, NewTask("A"));
        var b = await CreateTaskAsync(Ben, project.Id, NewTask("B"));
        var c = await CreateTaskAsync(Ben, project.Id, NewTask("C"));
        await LinkAsync(Ben, project.Id, a.Id, b.Id);
        await LinkAsync(Ben, project.Id, b.Id, c.Id);

        var cycle = await LinkAsync(Ben, project.Id, c.Id, a.Id);

        Assert.Equal(HttpStatusCode.Conflict, cycle.StatusCode);
        Assert.Contains("cycle", await cycle.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Concurrent_links_cannot_form_a_cycle_together()
    {
        var project = await CreateTeamProjectAsync();
        var a = await CreateTaskAsync(Ben, project.Id, NewTask("A"));
        var b = await CreateTaskAsync(Ben, project.Id, NewTask("B"));
        var c = await CreateTaskAsync(Ben, project.Id, NewTask("C"));
        await LinkAsync(Ben, project.Id, a.Id, b.Id);

        var results = await Task.WhenAll(LinkAsync(Ben, project.Id, b.Id, c.Id), LinkAsync(Clara, project.Id, c.Id, a.Id));

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], results.Select(r => r.StatusCode).Order());
    }

    [Fact]
    public async Task Links_need_two_unrelated_tasks_of_the_project()
    {
        var project = await CreateTeamProjectAsync();
        var other = await CreateTeamProjectAsync();
        var parent = await CreateTaskAsync(Ben, project.Id, NewTask("Eltern"));
        var child = await CreateTaskAsync(Ben, project.Id, NewTask("Kind", parentTaskId: parent.Id));
        var grandchild = await CreateTaskAsync(Ben, project.Id, NewTask("Enkel", parentTaskId: child.Id));
        var foreign = await CreateTaskAsync(Ben, other.Id, NewTask("Anderes Projekt"));

        Assert.Equal(HttpStatusCode.BadRequest, (await LinkAsync(Ben, project.Id, parent.Id, parent.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await LinkAsync(Ben, project.Id, parent.Id, foreign.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await LinkAsync(Ben, project.Id, grandchild.Id, parent.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await LinkAsync(Ben, project.Id, parent.Id, child.Id, "after")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await LinkAsync(Ben, project.Id, Guid.NewGuid(), child.Id)).StatusCode);
    }

    [Fact]
    public async Task Violated_dependencies_are_flagged_but_dates_stay()
    {
        var project = await CreateTeamProjectAsync();
        var first = await CreateTaskAsync(Ben, project.Id, NewTask("Erst", startDate: Day, dueDate: Day.AddDays(4)));
        var overlapping = await CreateTaskAsync(Ben, project.Id, NewTask("Zu früh", startDate: Day.AddDays(4), dueDate: Day.AddDays(6)));
        var after = await CreateTaskAsync(Ben, project.Id, NewTask("Danach", startDate: Day.AddDays(5)));
        var undated = await CreateTaskAsync(Ben, project.Id, NewTask("Offen"));

        var violated = await LinkedAsync(Ben, project.Id, first.Id, overlapping.Id);
        var fine = await LinkedAsync(Ben, project.Id, first.Id, after.Id);
        var unknown = await LinkedAsync(Ben, project.Id, first.Id, undated.Id);

        Assert.True(violated.Violated);
        Assert.False(fine.Violated);
        Assert.False(unknown.Violated);
        var gantt = await GetAsync(Ben, project.Id);
        Assert.Equal(Day.AddDays(4), gantt.Tasks.Single(t => t.Id == overlapping.Id).StartDate);
    }

    [Fact]
    public async Task Dependencies_of_deleted_tasks_disappear()
    {
        var project = await CreateTeamProjectAsync();
        var a = await CreateTaskAsync(Ben, project.Id, NewTask("A"));
        var b = await CreateTaskAsync(Ben, project.Id, NewTask("B"));
        var link = await LinkedAsync(Ben, project.Id, a.Id, b.Id);

        await As(Ben).DeleteAsync($"/api/v1/tasks/{b.Id}");

        Assert.Empty((await GetAsync(Ben, project.Id)).Dependencies);
        Assert.Equal(HttpStatusCode.NotFound, (await As(Ben).DeleteAsync($"/api/v1/task-dependencies/{link.Id}")).StatusCode);
    }

    [Fact]
    public async Task Editors_manage_milestones()
    {
        var project = await CreateTeamProjectAsync();

        var denied = await As(David).PostAsJsonAsync($"/api/v1/projects/{project.Id}/gantt/milestones", new CreateMilestoneRequest("Abnahme", Day));
        var created = await As(Clara).PostAsJsonAsync($"/api/v1/projects/{project.Id}/gantt/milestones", new CreateMilestoneRequest(" Abnahme ", Day));

        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var milestone = (await created.Content.ReadFromJsonAsync<GanttMilestoneResponse>())!;
        Assert.Equal(("Abnahme", Day, 1L), (milestone.Name, milestone.Date, milestone.Version));

        var renamed = await As(Clara).PatchAsJsonAsync($"/api/v1/gantt-milestones/{milestone.Id}",
            new { version = milestone.Version, name = "Go-live", date = Day.AddDays(7) });
        var stale = await As(Ben).PatchAsJsonAsync($"/api/v1/gantt-milestones/{milestone.Id}", new { version = milestone.Version, name = "Alt" });
        var forbidden = await As(David).PatchAsJsonAsync($"/api/v1/gantt-milestones/{milestone.Id}", new { version = 2, name = "Mitglied" });

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        var shown = Assert.Single((await GetAsync(Eva, project.Id)).Milestones);
        Assert.Equal(("Go-live", Day.AddDays(7), 2L), (shown.Name, shown.Date, shown.Version));

        Assert.Equal(HttpStatusCode.NotFound, (await As(Felix).DeleteAsync($"/api/v1/gantt-milestones/{milestone.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await As(Clara).DeleteAsync($"/api/v1/gantt-milestones/{milestone.Id}")).StatusCode);
        Assert.Empty((await GetAsync(Eva, project.Id)).Milestones);
        Assert.Equal(1L, await ScalarAsync("select count(*) from audit_log where action = 'MilestoneDeleted' and resource_id = $1", milestone.Id));
    }

    [Fact]
    public async Task Milestones_need_a_name_and_a_date()
    {
        var project = await CreateTeamProjectAsync();
        var url = $"/api/v1/projects/{project.Id}/gantt/milestones";

        Assert.Equal(HttpStatusCode.BadRequest, (await As(Ben).PostAsJsonAsync(url, new CreateMilestoneRequest(" ", Day))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await As(Ben).PostAsJsonAsync(url, new CreateMilestoneRequest(new string('x', 201), Day))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await As(Ben).PostAsJsonAsync(url, new CreateMilestoneRequest("Ohne Datum", null))).StatusCode);

        var milestone = (await (await As(Ben).PostAsJsonAsync(url, new CreateMilestoneRequest("Termin", Day))).Content.ReadFromJsonAsync<GanttMilestoneResponse>())!;
        var cleared = await As(Ben).PatchAsJsonAsync($"/api/v1/gantt-milestones/{milestone.Id}", new { version = milestone.Version, date = (DateOnly?)null });
        Assert.Equal(HttpStatusCode.BadRequest, cleared.StatusCode);
    }

    [Fact]
    public async Task Seed_project_has_a_schedule()
    {
        var gantt = await GetAsync(Ben, IntranetProject.Id);

        Assert.Contains(gantt.Tasks, t => t.Id == DesignTask.Id && t.StartDate is not null && t.DueDate is not null);
        Assert.Contains(gantt.Tasks, t => t.Id == ContentTask.Id && t.StartDate is null);
        Assert.Contains(gantt.Dependencies, d => d.SourceTaskId == ConceptTask.Id && d.TargetTaskId == DesignTask.Id);
        Assert.Contains(gantt.Milestones, m => m.Name == "Go-live Intranet");
    }

    private async Task<GanttResponse> GetAsync(SeedUser user, Guid projectId) =>
        (await As(user).GetFromJsonAsync<GanttResponse>($"/api/v1/projects/{projectId}/gantt"))!;

    private Task<HttpResponseMessage> LinkAsync(SeedUser user, Guid projectId, Guid source, Guid target, string? type = null) =>
        As(user).PostAsJsonAsync($"/api/v1/projects/{projectId}/gantt/dependencies", new CreateDependencyRequest(source, target, type));

    private async Task<GanttDependency> LinkedAsync(SeedUser user, Guid projectId, Guid source, Guid target)
    {
        var response = await LinkAsync(user, projectId, source, target);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<GanttDependency>())!;
    }

    [Fact]
    public async Task Chart_downloads_as_pdf_for_viewers_and_stays_hidden_from_outsiders()
    {
        var project = await CreateTeamProjectAsync();
        await CreateTaskAsync(Ben, project.Id, NewTask("Konzept", startDate: Day, dueDate: Day.AddDays(4)));

        var response = await As(Gina).GetAsync($"/api/v1/projects/{project.Id}/gantt/export.pdf");
        var bytes = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.EndsWith(".pdf", response.Content.Headers.ContentDisposition?.FileNameStar, StringComparison.Ordinal);
        Assert.Equal("%PDF-"u8.ToArray(), bytes[..5]);
        Assert.Equal(HttpStatusCode.NotFound, (await As(Felix).GetAsync($"/api/v1/projects/{project.Id}/gantt/export.pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await As(Fritz).GetAsync($"/api/v1/projects/{project.Id}/gantt/export.pdf")).StatusCode);
    }
}
