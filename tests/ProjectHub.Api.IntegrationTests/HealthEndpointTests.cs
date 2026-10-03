using System.Net;

namespace ProjectHub.Api.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public class HealthEndpointTests(InfrastructureFixture infrastructure)
{
    // Nothing listens on port 1, so dependency checks against it fail fast.
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1";
    private const string UnreachableRedis = "127.0.0.1:1,abortConnect=false,connectTimeout=500";

    [Fact]
    public async Task Live_returns_200_when_dependencies_are_available()
    {
        await using var factory = new ProjectHubApiFactory(infrastructure.Postgres.GetConnectionString(), infrastructure.Redis.GetConnectionString());

        var response = await factory.CreateClient().GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_returns_200_when_database_and_redis_are_available()
    {
        await using var factory = new ProjectHubApiFactory(infrastructure.Postgres.GetConnectionString(), infrastructure.Redis.GetConnectionString());

        var response = await factory.CreateClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_returns_503_when_redis_is_unreachable_while_live_stays_200()
    {
        await using var factory = new ProjectHubApiFactory(infrastructure.Postgres.GetConnectionString(), UnreachableRedis);
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
    }

    [Fact]
    public async Task Ready_returns_503_when_database_is_unreachable()
    {
        await using var factory = new ProjectHubApiFactory(UnreachableDatabase, infrastructure.Redis.GetConnectionString(), applyMigrations: false);

        var response = await factory.CreateClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
