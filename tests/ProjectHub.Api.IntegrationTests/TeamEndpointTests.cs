using System.Net;
using System.Net.Http.Json;
using Npgsql;
using ProjectHub.Api.Modules.Teams;
using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Modules.Users;
using static ProjectHub.Api.Modules.Identity.Development.DevelopmentSeedData;

namespace ProjectHub.Api.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class TeamEndpointTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private ProjectHubApiFactory factory = null!;

    public Task InitializeAsync()
    {
        factory = new ProjectHubApiFactory(infrastructure.Postgres.GetConnectionString(), infrastructure.Redis.GetConnectionString());
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await factory.DisposeAsync();

    [Fact]
    public async Task Teams_lists_only_the_callers_organization()
    {
        var page = await factory.CreateClientFor(Eva).GetFromJsonAsync<PagedResponse<TeamSummary>>("/api/v1/teams?limit=100");

        Assert.Contains(page!.Items, t => t.Id == PlatformTeam.Id);
        Assert.DoesNotContain(page.Items, t => t.Id == FabrikamTeam.Id);
    }

    [Fact]
    public async Task Team_details_include_members()
    {
        var team = await factory.CreateClientFor(Eva).GetFromJsonAsync<TeamDetails>($"/api/v1/teams/{PlatformTeam.Id}");

        Assert.Contains(team!.Members, m => m.UserId == Ben.Id && m.Role == "owner");
        Assert.False(team.CanManage);
    }

    [Fact]
    public async Task Team_of_another_organization_is_not_found()
    {
        var response = await factory.CreateClientFor(Fritz).GetAsync($"/api/v1/teams/{PlatformTeam.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Organization_admin_creates_team_and_it_is_audited()
    {
        var name = UniqueName();

        var response = await factory.CreateClientFor(Ada).PostAsJsonAsync("/api/v1/teams", new CreateTeamRequest(name, "Beschreibung"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var team = await response.Content.ReadFromJsonAsync<TeamSummary>();
        Assert.Equal(name, team!.Name);
        Assert.Equal(1L, await CountAuditAsync("TeamCreated", team.Id));
    }

    [Theory]
    [InlineData("dev-ben")]
    [InlineData("dev-eva")]
    [InlineData("dev-gina")]
    public async Task Non_admins_cannot_create_teams(string objectId)
    {
        var user = Users.Single(u => u.ObjectId == objectId);

        var response = await factory.CreateClientFor(user).PostAsJsonAsync("/api/v1/teams", new CreateTeamRequest(UniqueName(), null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Duplicate_team_name_conflicts_case_insensitively()
    {
        var client = factory.CreateClientFor(Ada);
        var name = UniqueName();
        await client.PostAsJsonAsync("/api/v1/teams", new CreateTeamRequest(name, null));

        var response = await client.PostAsJsonAsync("/api/v1/teams", new CreateTeamRequest(name.ToUpperInvariant(), null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Team_name_is_required(string name)
    {
        var response = await factory.CreateClientFor(Ada).PostAsJsonAsync("/api/v1/teams", new CreateTeamRequest(name, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Same_team_name_is_allowed_in_another_organization()
    {
        var name = UniqueName();
        await factory.CreateClientFor(Ada).PostAsJsonAsync("/api/v1/teams", new CreateTeamRequest(name, null));

        var response = await factory.CreateClientFor(Fritz).PostAsJsonAsync("/api/v1/teams", new CreateTeamRequest(name, null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Team_owner_manages_members_and_changes_are_audited()
    {
        var team = await CreateTeamAsync();
        var ada = factory.CreateClientFor(Ada);
        await ada.PostAsJsonAsync($"/api/v1/teams/{team.Id}/members", new AddTeamMemberRequest(Ben.Id, "owner"));
        var ben = factory.CreateClientFor(Ben);

        var added = await ben.PostAsJsonAsync($"/api/v1/teams/{team.Id}/members", new AddTeamMemberRequest(Felix.Id, null));
        var removed = await ben.DeleteAsync($"/api/v1/teams/{team.Id}/members/{Felix.Id}");

        Assert.Equal(HttpStatusCode.NoContent, added.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(2L, await CountAuditAsync("TeamMemberAdded", team.Id));
        Assert.Equal(1L, await CountAuditAsync("TeamMemberRemoved", team.Id));
    }

    [Fact]
    public async Task Plain_team_member_cannot_manage_members()
    {
        var response = await factory.CreateClientFor(Clara)
            .PostAsJsonAsync($"/api/v1/teams/{PlatformTeam.Id}/members", new AddTeamMemberRequest(Eva.Id, null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Viewer_cannot_remove_members()
    {
        var response = await factory.CreateClientFor(Eva).DeleteAsync($"/api/v1/teams/{PlatformTeam.Id}/members/{Clara.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task User_of_another_organization_cannot_be_added()
    {
        var team = await CreateTeamAsync();

        var response = await factory.CreateClientFor(Ada)
            .PostAsJsonAsync($"/api/v1/teams/{team.Id}/members", new AddTeamMemberRequest(Fritz.Id, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Admin_of_another_organization_cannot_manage_members()
    {
        var response = await factory.CreateClientFor(Fritz)
            .PostAsJsonAsync($"/api/v1/teams/{PlatformTeam.Id}/members", new AddTeamMemberRequest(Fritz.Id, null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Adding_an_existing_member_conflicts()
    {
        var response = await factory.CreateClientFor(Ada)
            .PostAsJsonAsync($"/api/v1/teams/{PlatformTeam.Id}/members", new AddTeamMemberRequest(Ben.Id, "owner"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_member_role_is_rejected()
    {
        var team = await CreateTeamAsync();

        var response = await factory.CreateClientFor(Ada)
            .PostAsJsonAsync($"/api/v1/teams/{team.Id}/members", new AddTeamMemberRequest(Eva.Id, "admin"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<TeamSummary> CreateTeamAsync()
    {
        var response = await factory.CreateClientFor(Ada).PostAsJsonAsync("/api/v1/teams", new CreateTeamRequest(UniqueName(), null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TeamSummary>())!;
    }

    private static string UniqueName() => $"Team {Guid.NewGuid():N}";

    private async Task<long> CountAuditAsync(string action, Guid resourceId)
    {
        await using var dataSource = NpgsqlDataSource.Create(infrastructure.Postgres.GetConnectionString());
        await using var command = dataSource.CreateCommand("select count(*) from audit_log where action = $1 and resource_id = $2");
        command.Parameters.Add(new NpgsqlParameter { Value = action });
        command.Parameters.Add(new NpgsqlParameter { Value = resourceId });
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
