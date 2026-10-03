using System.Net;
using System.Net.Http.Json;
using Npgsql;
using ProjectHub.Api.Modules.Identity.Development;
using ProjectHub.Api.Modules.Organizations;
using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Modules.Users;
using static ProjectHub.Api.Modules.Identity.Development.DevelopmentSeedData;

namespace ProjectHub.Api.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class IdentityEndpointTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private ProjectHubApiFactory factory = null!;

    public Task InitializeAsync()
    {
        factory = new ProjectHubApiFactory(infrastructure.Postgres.GetConnectionString(), infrastructure.Redis.GetConnectionString());
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await factory.DisposeAsync();

    [Fact]
    public async Task Me_returns_the_signed_in_user()
    {
        var me = await factory.CreateClientFor(Eva).GetFromJsonAsync<MeResponse>("/api/v1/me");

        Assert.Equal(Eva.Id, me!.Id);
        Assert.Equal(Contoso.Id, me.OrganizationId);
        Assert.Equal("member", me.OrganizationRole);
    }

    [Fact]
    public async Task Default_development_user_is_the_organization_admin()
    {
        var me = await factory.CreateClient().GetFromJsonAsync<MeResponse>("/api/v1/me");

        Assert.Equal(Ada.Id, me!.Id);
        Assert.Equal("admin", me.OrganizationRole);
    }

    [Fact]
    public async Task Unknown_development_user_is_unauthorized()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevelopmentIdentityOptions.UserHeader, "dev-nobody");

        var response = await client.GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Inactive_user_is_forbidden()
    {
        await SetStatusAsync(Gina, "inactive");
        try
        {
            var response = await factory.CreateClientFor(Gina).GetAsync("/api/v1/me");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            await SetStatusAsync(Gina, "active");
        }
    }

    [Fact]
    public async Task Organization_is_the_callers_own()
    {
        var contoso = await factory.CreateClientFor(Ada).GetFromJsonAsync<OrganizationResponse>("/api/v1/organization");
        var fabrikam = await factory.CreateClientFor(Fritz).GetFromJsonAsync<OrganizationResponse>("/api/v1/organization");

        Assert.Equal(Contoso.Id, contoso!.Id);
        Assert.Equal(Fabrikam.Id, fabrikam!.Id);
    }

    [Fact]
    public async Task Users_lists_only_the_callers_organization()
    {
        var page = await factory.CreateClientFor(Eva).GetFromJsonAsync<PagedResponse<UserResponse>>("/api/v1/users?limit=100");

        Assert.Contains(page!.Items, u => u.Id == Ada.Id);
        Assert.DoesNotContain(page.Items, u => u.Id == Fritz.Id);
        Assert.All(page.Items, u => Assert.EndsWith("@contoso-dev.example.invalid", u.Email));
    }

    [Fact]
    public async Task Users_are_paged()
    {
        var client = factory.CreateClientFor(Ada);

        var first = await client.GetFromJsonAsync<PagedResponse<UserResponse>>("/api/v1/users?limit=2");
        var second = await client.GetFromJsonAsync<PagedResponse<UserResponse>>($"/api/v1/users?limit=2&offset={first!.NextOffset}");

        Assert.Equal(2, first.Items.Count);
        Assert.Equal(2, first.NextOffset);
        Assert.Empty(first.Items.Select(u => u.Id).Intersect(second!.Items.Select(u => u.Id)));
    }

    [Fact]
    public async Task Users_search_matches_name()
    {
        var page = await factory.CreateClientFor(Ada).GetFromJsonAsync<PagedResponse<UserResponse>>("/api/v1/users?search=clara");

        Assert.Equal(Clara.Id, Assert.Single(page!.Items).Id);
    }

    [Theory]
    [InlineData("limit=0")]
    [InlineData("limit=101")]
    [InlineData("offset=-1")]
    public async Task Users_rejects_invalid_paging(string query)
    {
        var response = await factory.CreateClientFor(Ada).GetAsync($"/api/v1/users?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Development_users_are_listed_for_sign_in()
    {
        var users = await factory.CreateClient().GetFromJsonAsync<DevelopmentUserResponse[]>("/api/dev/users");

        Assert.Equal(DevelopmentSeedData.Users.Count, users!.Length);
    }

    private async Task SetStatusAsync(SeedUser user, string status)
    {
        await using var dataSource = NpgsqlDataSource.Create(infrastructure.Postgres.GetConnectionString());
        await using var command = dataSource.CreateCommand("update app_user set status = $1 where id = $2");
        command.Parameters.Add(new NpgsqlParameter { Value = status });
        command.Parameters.Add(new NpgsqlParameter { Value = user.Id });
        await command.ExecuteNonQueryAsync();
    }
}
