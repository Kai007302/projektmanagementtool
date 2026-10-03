using System.Net;
using System.Net.Http.Json;
using ProjectHub.Api.Modules.Gantt;
using static ProjectHub.Api.Modules.Identity.Development.DevelopmentSeedData;

namespace ProjectHub.Api.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class CalendarTests(InfrastructureFixture infrastructure) : ApiTestBase(infrastructure)
{
    [Fact]
    public async Task Task_with_dates_downloads_as_all_day_entry()
    {
        var project = await CreateTeamProjectAsync();
        var task = await CreateTaskAsync(Ben, project.Id, NewTask("Texte, final", startDate: new DateOnly(2026, 11, 2), dueDate: new DateOnly(2026, 11, 4)));

        var response = await As(Eva).GetAsync($"/api/v1/tasks/{task.Id}/calendar.ics");
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/calendar", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Texte, final.ics", response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName);
        Assert.Contains($"UID:task-{task.Id}@projecthub\r\n", text, StringComparison.Ordinal);
        Assert.Contains("DTSTART;VALUE=DATE:20261102\r\n", text, StringComparison.Ordinal);
        Assert.Contains("DTEND;VALUE=DATE:20261105\r\n", text, StringComparison.Ordinal);
        Assert.Contains("SUMMARY:Texte\\, final\r\n", text, StringComparison.Ordinal);
        Assert.Contains($"SEQUENCE:{task.Version}\r\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_a_due_date_makes_a_single_day_and_no_date_is_rejected()
    {
        var project = await CreateTeamProjectAsync();
        var due = await CreateTaskAsync(Ben, project.Id, NewTask("Abgabe", dueDate: new DateOnly(2026, 12, 1)));
        var undated = await CreateTaskAsync(Ben, project.Id, NewTask("Irgendwann"));

        var text = await As(Ben).GetStringAsync($"/api/v1/tasks/{due.Id}/calendar.ics");
        var rejected = await As(Ben).GetAsync($"/api/v1/tasks/{undated.Id}/calendar.ics");

        Assert.Contains("DTSTART;VALUE=DATE:20261201\r\nDTEND;VALUE=DATE:20261202\r\n", text, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }

    [Theory]
    [InlineData("dev-felix")]
    [InlineData("dev-fritz")]
    public async Task People_who_cannot_see_the_project_get_not_found(string objectId)
    {
        var project = await CreateTeamProjectAsync();
        var task = await CreateTaskAsync(Ben, project.Id, NewTask("Intern", dueDate: new DateOnly(2026, 12, 1)));
        var created = await As(Clara).PostAsJsonAsync($"/api/v1/projects/{project.Id}/gantt/milestones", new CreateMilestoneRequest("Go-live", new DateOnly(2026, 12, 15)));
        var milestone = (await created.Content.ReadFromJsonAsync<GanttMilestoneResponse>())!;
        var other = Users.Single(u => u.ObjectId == objectId);

        Assert.Equal(HttpStatusCode.NotFound, (await As(other).GetAsync($"/api/v1/tasks/{task.Id}/calendar.ics")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await As(other).GetAsync($"/api/v1/gantt-milestones/{milestone.Id}/calendar.ics")).StatusCode);
    }

    [Fact]
    public async Task Milestones_download_for_viewers()
    {
        var project = await CreateTeamProjectAsync();
        var created = await As(Clara).PostAsJsonAsync($"/api/v1/projects/{project.Id}/gantt/milestones", new CreateMilestoneRequest("Go-live", new DateOnly(2026, 12, 15)));
        var milestone = (await created.Content.ReadFromJsonAsync<GanttMilestoneResponse>())!;

        var text = await As(Gina).GetStringAsync($"/api/v1/gantt-milestones/{milestone.Id}/calendar.ics");

        Assert.Contains($"UID:milestone-{milestone.Id}@projecthub\r\n", text, StringComparison.Ordinal);
        Assert.Contains("DTSTART;VALUE=DATE:20261215\r\nDTEND;VALUE=DATE:20261216\r\n", text, StringComparison.Ordinal);
        Assert.Contains("SUMMARY:Go-live\r\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deleted_tasks_are_not_found()
    {
        var project = await CreateTeamProjectAsync();
        var task = await CreateTaskAsync(Ben, project.Id, NewTask("Weg", dueDate: new DateOnly(2026, 12, 1)));
        Assert.Equal(HttpStatusCode.NoContent, (await As(Ben).DeleteAsync($"/api/v1/tasks/{task.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await As(Ben).GetAsync($"/api/v1/tasks/{task.Id}/calendar.ics")).StatusCode);
    }
}
