using System.Net;
using Npgsql;

namespace ProjectHub.Api.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public class StartupTests(InfrastructureFixture infrastructure)
{
    [Fact]
    public async Task Startup_applies_all_migrations_once()
    {
        var connection = infrastructure.Postgres.GetConnectionString();

        // Two startups: the second must not re-run scripts that already ran.
        await using (var first = new ProjectHubApiFactory(connection, infrastructure.Redis.GetConnectionString()))
        {
            first.CreateClient();
        }

        await using (var second = new ProjectHubApiFactory(connection, infrastructure.Redis.GetConnectionString()))
        {
            second.CreateClient();
        }

        await using var dataSource = NpgsqlDataSource.Create(connection);
        await using var command = dataSource.CreateCommand(
            "select count(*) from information_schema.tables where table_schema = 'public' and table_name in ('organization', 'task', 'knowledge_article')");
        Assert.Equal(3L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task OpenApi_document_is_served_in_development()
    {
        await using var factory = new ProjectHubApiFactory(infrastructure.Postgres.GetConnectionString(), infrastructure.Redis.GetConnectionString());

        var response = await factory.CreateClient().GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
