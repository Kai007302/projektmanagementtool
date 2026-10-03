using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace ProjectHub.Api.IntegrationTests;

/// <summary>Starts PostgreSQL 18 and Redis 8 once for all integration tests.</summary>
public sealed class InfrastructureFixture : IAsyncLifetime
{
    public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder("postgres:18").Build();

    public RedisContainer Redis { get; } = new RedisBuilder("redis:8-alpine").Build();

    public Task InitializeAsync() => Task.WhenAll(Postgres.StartAsync(), Redis.StartAsync());

    public async Task DisposeAsync()
    {
        await Postgres.DisposeAsync();
        await Redis.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class InfrastructureCollection : ICollectionFixture<InfrastructureFixture>
{
    public const string Name = "Infrastructure";
}
