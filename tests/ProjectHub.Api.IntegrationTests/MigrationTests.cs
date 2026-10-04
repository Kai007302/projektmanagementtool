using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using ProjectHub.Api.Infrastructure.Database;

namespace ProjectHub.Api.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class MigrationTests(InfrastructureFixture infrastructure)
{
    [Fact]
    public async Task Instances_starting_at_once_apply_every_migration_exactly_once()
    {
        var database = $"migrations_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(infrastructure.Postgres.GetConnectionString()))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"create database {database}", admin);
            await create.ExecuteNonQueryAsync();
        }

        var connectionString = new NpgsqlConnectionStringBuilder(infrastructure.Postgres.GetConnectionString()) { Database = database, Pooling = false }.ConnectionString;
        var start = new Barrier(4);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            start.SignalAndWait();
            DatabaseMigrator.Migrate(connectionString, NullLogger.Instance);
        })));

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var count = new NpgsqlCommand("select count(*), count(distinct scriptname) from schemaversions", connection);
        await using var reader = await count.ExecuteReaderAsync();
        await reader.ReadAsync();
        var scripts = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "database", "migrations"), "*.sql").Length;
        Assert.Equal((long)scripts, reader.GetInt64(0));
        Assert.Equal((long)scripts, reader.GetInt64(1));
    }
}
