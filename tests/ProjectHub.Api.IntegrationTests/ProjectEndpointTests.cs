using System.Net;
using System.Net.Http.Json;
using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Modules.Projects;
using static ProjectHub.Api.Modules.Identity.Development.DevelopmentSeedData;

namespace ProjectHub.Api.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class ProjectEndpointTests(InfrastructureFixture infrastructure) : ApiTestBase(infrastructure)
{
    [Fact]
    public async Task Creator_becomes_project_admin_and_creation_is_recorded()
    {
        var project = await CreateProjectAsync(Felix);

        var details = await As(Felix).GetFromJsonAsync<ProjectDetails>($"/api/v1/projects/{project.Id}");

        Assert.Equal(Felix.Id, details!.OwnerId);
        Assert.Contains(details.Members, m => m.UserId == Felix.Id && m.Role == "admin");
        Assert.True(details.Capabilities.CanManage);
        Assert.Equal(1L, await ScalarAsync("select count(*) from audit_log where action = 'ProjectCreated' and resource_id = $1", project.Id));
        Assert.Equal(1L, await ScalarAsync("select count(*) from activity_log where action = 'ProjectCreated' and project_id = $1", project.Id));
    }

    [Theory]
    [InlineData("", null, null, null)]
    [InlineData("Projekt", "unknown", null, null)]
    [InlineData("Projekt", null, "2026-10-10", "2026-10-01")]
    public async Task Invalid_project_is_rejected(string name, string? status, string? start, string? end)
    {
        var request = new CreateProjectRequest(
            name, null, status, start is null ? null : DateOnly.Parse(start), end is null ? null : DateOnly.Parse(end));

        var response = await As(Ben).PostAsJsonAsync("/api/v1/projects", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_contains_only_projects_the_user_belongs_to()
    {
        var felixProject = await CreateProjectAsync(Felix);

        var forEva = await As(Eva).GetFromJsonAsync<PagedResponse<ProjectSummary>>("/api/v1/projects?limit=100");
        var forFritz = await As(Fritz).GetFromJsonAsync<PagedResponse<ProjectSummary>>("/api/v1/projects?limit=100");

        Assert.Contains(forEva!.Items, p => p.Id == IntranetProject.Id && p.MyRole == "viewer");
        Assert.DoesNotContain(forEva.Items, p => p.Id == felixProject.Id);
        Assert.DoesNotContain(forFritz!.Items, p => p.Id == IntranetProject.Id);
    }

    [Fact]
    public async Task Organization_admin_sees_all_projects_of_the_organization()
    {
        var felixProject = await CreateProjectAsync(Felix);

        var page = await As(Ada).GetFromJsonAsync<PagedResponse<ProjectSummary>>("/api/v1/projects?limit=100");

        Assert.Contains(page!.Items, p => p.Id == felixProject.Id && p.MyRole == null);
        Assert.DoesNotContain(page.Items, p => p.Id == FabrikamProject.Id);
    }

    [Theory]
    [InlineData("dev-felix")]
    [InlineData("dev-fritz")]
    public async Task Project_is_not_found_without_membership_or_from_another_organization(string objectId)
    {
        var response = await As(Users.Single(u => u.ObjectId == objectId)).GetAsync($"/api/v1/projects/{IntranetProject.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Editor_updates_project_with_current_version()
    {
        var project = await CreateTeamProjectAsync();

        var response = await As(Clara).PatchAsJsonAsync($"/api/v1/projects/{project.Id}", new { version = project.Version, name = "Neu", status = "on_hold" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<ProjectSummary>();
        Assert.Equal("Neu", updated!.Name);
        Assert.Equal("on_hold", updated.Status);
        Assert.Equal(project.Version + 1, updated.Version);
    }

    [Theory]
    [InlineData("dev-david")]
    [InlineData("dev-eva")]
    [InlineData("dev-gina")]
    public async Task Members_viewers_and_guests_cannot_update_the_project(string objectId)
    {
        var project = await CreateTeamProjectAsync();

        var response = await As(Users.Single(u => u.ObjectId == objectId))
            .PatchAsJsonAsync($"/api/v1/projects/{project.Id}", new { version = project.Version, name = "Neu" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Stale_project_update_conflicts_and_does_not_overwrite()
    {
        var project = await CreateTeamProjectAsync();
        await As(Ben).PatchAsJsonAsync($"/api/v1/projects/{project.Id}", new { version = project.Version, name = "Erste Änderung" });

        var stale = await As(Clara).PatchAsJsonAsync($"/api/v1/projects/{project.Id}", new { version = project.Version, name = "Veraltet" });

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var current = await As(Ben).GetFromJsonAsync<ProjectDetails>($"/api/v1/projects/{project.Id}");
        Assert.Equal("Erste Änderung", current!.Name);
    }

    [Fact]
    public async Task Update_without_version_is_rejected()
    {
        var project = await CreateTeamProjectAsync();

        var response = await As(Ben).PatchAsJsonAsync($"/api/v1/projects/{project.Id}", new { name = "Neu" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Only_project_admins_delete_and_deleted_projects_disappear()
    {
        var project = await CreateTeamProjectAsync();

        var byEditor = await As(Clara).DeleteAsync($"/api/v1/projects/{project.Id}");
        var byAdmin = await As(Ben).DeleteAsync($"/api/v1/projects/{project.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, byEditor.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, byAdmin.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await As(Ben).GetAsync($"/api/v1/projects/{project.Id}")).StatusCode);
        var list = await As(Ben).GetFromJsonAsync<PagedResponse<ProjectSummary>>("/api/v1/projects?limit=100");
        Assert.DoesNotContain(list!.Items, p => p.Id == project.Id);
        Assert.Equal(1L, await ScalarAsync("select count(*) from audit_log where action = 'ProjectDeleted' and resource_id = $1", project.Id));
    }

    [Fact]
    public async Task Project_admin_manages_members()
    {
        var project = await CreateProjectAsync(Ben);
        var client = As(Ben);

        var added = await client.PostAsJsonAsync($"/api/v1/projects/{project.Id}/members", new AddProjectMemberRequest(Felix.Id, "viewer"));
        var changed = await client.PatchAsJsonAsync($"/api/v1/projects/{project.Id}/members/{Felix.Id}", new ChangeProjectMemberRequest("editor"));
        var details = await client.GetFromJsonAsync<ProjectDetails>($"/api/v1/projects/{project.Id}");
        var removed = await client.DeleteAsync($"/api/v1/projects/{project.Id}/members/{Felix.Id}");

        Assert.Equal(HttpStatusCode.NoContent, added.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        Assert.Contains(details!.Members, m => m.UserId == Felix.Id && m.Role == "editor");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await As(Felix).GetAsync($"/api/v1/projects/{project.Id}")).StatusCode);
        Assert.Equal(3L, await ScalarAsync("select count(*) from audit_log where resource_id = $1 and action like 'ProjectMember%'", project.Id));
    }

    [Fact]
    public async Task Editor_cannot_manage_members()
    {
        var project = await CreateTeamProjectAsync();

        var response = await As(Clara).PostAsJsonAsync($"/api/v1/projects/{project.Id}/members", new AddProjectMemberRequest(Felix.Id, "viewer"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Member_checks_reject_duplicates_other_organizations_and_bad_roles()
    {
        var project = await CreateTeamProjectAsync();
        var client = As(Ben);

        var duplicate = await client.PostAsJsonAsync($"/api/v1/projects/{project.Id}/members", new AddProjectMemberRequest(Clara.Id, "viewer"));
        var foreign = await client.PostAsJsonAsync($"/api/v1/projects/{project.Id}/members", new AddProjectMemberRequest(Fritz.Id, "viewer"));
        var badRole = await client.PostAsJsonAsync($"/api/v1/projects/{project.Id}/members", new AddProjectMemberRequest(Felix.Id, "owner"));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badRole.StatusCode);
    }

    [Fact]
    public async Task Last_admin_cannot_be_removed_or_demoted()
    {
        var project = await CreateProjectAsync(Ben);
        var client = As(Ben);

        var demote = await client.PatchAsJsonAsync($"/api/v1/projects/{project.Id}/members/{Ben.Id}", new ChangeProjectMemberRequest("viewer"));
        var remove = await client.DeleteAsync($"/api/v1/projects/{project.Id}/members/{Ben.Id}");

        Assert.Equal(HttpStatusCode.Conflict, demote.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, remove.StatusCode);
    }

    [Fact]
    public async Task Activity_feed_lists_changes_newest_first_for_members_only()
    {
        var project = await CreateTeamProjectAsync();
        await As(Ben).PatchAsJsonAsync($"/api/v1/projects/{project.Id}", new { version = project.Version, description = "Beschreibung" });

        var feed = await As(Eva).GetFromJsonAsync<PagedResponse<ActivityResponse>>($"/api/v1/projects/{project.Id}/activity");
        var forFelix = await As(Felix).GetAsync($"/api/v1/projects/{project.Id}/activity");

        Assert.Equal("ProjectUpdated", feed!.Items[0].Action);
        Assert.Equal("Ben Projektleiter", feed.Items[0].ActorName);
        Assert.Contains(feed.Items, a => a.Action == "ProjectCreated");
        Assert.Equal(HttpStatusCode.NotFound, forFelix.StatusCode);
    }
}
