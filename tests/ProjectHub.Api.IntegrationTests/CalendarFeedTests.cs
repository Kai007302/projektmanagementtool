using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using ProjectHub.Api.Modules.Calendar;
using ProjectHub.Api.Modules.Gantt;
using static ProjectHub.Api.Modules.Identity.Development.DevelopmentSeedData;

namespace ProjectHub.Api.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class CalendarFeedTests(InfrastructureFixture infrastructure) : ApiTestBase(infrastructure)
{
    private static readonly DateOnly Soon = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10);

    [Fact]
    public async Task Feed_holds_own_dated_tasks_and_milestones_of_own_projects()
    {
        var project = await CreateTeamProjectAsync();
        var mine = await CreateTaskAsync(Ben, project.Id, NewTask("Meins", assigneeId: David.Id, startDate: Soon, dueDate: Soon.AddDays(2)));
        var others = await CreateTaskAsync(Ben, project.Id, NewTask("Fremd", assigneeId: Clara.Id, dueDate: Soon));
        var undated = await CreateTaskAsync(Ben, project.Id, NewTask("Ohne Datum", assigneeId: David.Id));
        var old = await CreateTaskAsync(Ben, project.Id, NewTask("Lange her", assigneeId: David.Id, dueDate: Soon.AddDays(-200)));
        var milestone = await CreateMilestoneAsync(project.Id, "Go-live", Soon.AddDays(5));

        var feed = await CreateFeedAsync(David);
        var response = await Factory.CreateClient().GetAsync(feed);
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/calendar", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("X-WR-CALNAME:ProjectHub – Meine Termine\r\n", text, StringComparison.Ordinal);
        Assert.Contains($"UID:task-{mine.Id}@projecthub\r\n", text, StringComparison.Ordinal);
        Assert.Contains($"SUMMARY:Meins · {project.Name}", text, StringComparison.Ordinal);
        Assert.Contains($"UID:milestone-{milestone.Id}@projecthub\r\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain($"task-{others.Id}", text, StringComparison.Ordinal);
        Assert.DoesNotContain($"task-{undated.Id}", text, StringComparison.Ordinal);
        Assert.DoesNotContain($"task-{old.Id}", text, StringComparison.Ordinal);

        var status = await As(David).GetFromJsonAsync<CalendarFeedStatus>("/api/v1/me/calendar-feed");
        Assert.True(status!.Active);
        Assert.NotNull(status.LastUsedAt);
    }

    [Fact]
    public async Task Leaving_the_project_or_deleting_the_task_removes_it_from_the_feed()
    {
        var project = await CreateTeamProjectAsync();
        var gone = await CreateTaskAsync(Ben, project.Id, NewTask("Gelöscht", assigneeId: David.Id, dueDate: Soon));
        var kept = await CreateTaskAsync(Ben, project.Id, NewTask("Bleibt", assigneeId: David.Id, dueDate: Soon));
        var feed = await CreateFeedAsync(David);
        Assert.Contains($"task-{kept.Id}", await Factory.CreateClient().GetStringAsync(feed), StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.NoContent, (await As(Ben).DeleteAsync($"/api/v1/tasks/{gone.Id}")).StatusCode);
        Assert.DoesNotContain($"task-{gone.Id}", await Factory.CreateClient().GetStringAsync(feed), StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.NoContent, (await As(Ben).DeleteAsync($"/api/v1/projects/{project.Id}/members/{David.Id}")).StatusCode);
        Assert.DoesNotContain($"task-{kept.Id}", await Factory.CreateClient().GetStringAsync(feed), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_new_address_replaces_the_old_one_and_deleting_ends_the_feed()
    {
        var first = await CreateFeedAsync(Eva);
        var second = await CreateFeedAsync(Eva);

        Assert.NotEqual(first, second);
        Assert.Equal(HttpStatusCode.NotFound, (await Factory.CreateClient().GetAsync(first)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Factory.CreateClient().GetAsync(second)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await As(Eva).DeleteAsync("/api/v1/me/calendar-feed")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Factory.CreateClient().GetAsync(second)).StatusCode);
        Assert.False((await As(Eva).GetFromJsonAsync<CalendarFeedStatus>("/api/v1/me/calendar-feed"))!.Active);
        Assert.Equal(HttpStatusCode.NotFound, (await As(Eva).DeleteAsync("/api/v1/me/calendar-feed")).StatusCode);
        Assert.True(await ScalarAsync("select count(*) from audit_log where actor_id = $1 and action = 'CalendarFeedCreated'", Eva.Id) >= 2);
        Assert.True(await ScalarAsync("select count(*) from audit_log where actor_id = $1 and action = 'CalendarFeedRevoked'", Eva.Id) >= 1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?token=")]
    [InlineData("?token=kurz")]
    [InlineData("?token=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("?token=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA%2F")]
    public async Task Unknown_or_malformed_tokens_are_not_found(string query)
    {
        var response = await Factory.CreateClient().GetAsync(CalendarFeedService.FeedPath + query);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Feeds_of_people_who_are_no_longer_active_are_not_found()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        _ = Factory.Services; // starts the host, which migrates and seeds the database
        await ScalarAsync(
            """
            with person as (
                insert into app_user (organization_id, entra_object_id, email, display_name, status)
                values ($1, $2, $3, 'Ehemals Aktiv', 'inactive') returning id)
            insert into calendar_feed (organization_id, user_id, token_hash) select $1, id, $4 from person returning 1
            """,
            Contoso.Id, $"feed-{Guid.NewGuid():N}", $"feed-{Guid.NewGuid():N}@contoso-dev.example.invalid", SHA256.HashData(Encoding.ASCII.GetBytes(token)));

        var response = await Factory.CreateClient().GetAsync($"{CalendarFeedService.FeedPath}?token={token}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Creates (or replaces) the person's feed and returns its path and query, as a calendar program would fetch it.</summary>
    private async Task<string> CreateFeedAsync(SeedUser user)
    {
        var response = await As(user).PostAsync("/api/v1/me/calendar-feed", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<CalendarFeedCreated>())!;
        var url = new Uri(created.Url);
        Assert.StartsWith(CalendarFeedService.FeedPath + "?token=", url.PathAndQuery, StringComparison.Ordinal);
        return url.PathAndQuery;
    }

    private async Task<GanttMilestoneResponse> CreateMilestoneAsync(Guid projectId, string name, DateOnly date)
    {
        var response = await As(Clara).PostAsJsonAsync($"/api/v1/projects/{projectId}/gantt/milestones", new CreateMilestoneRequest(name, date));
        return (await response.Content.ReadFromJsonAsync<GanttMilestoneResponse>())!;
    }
}
