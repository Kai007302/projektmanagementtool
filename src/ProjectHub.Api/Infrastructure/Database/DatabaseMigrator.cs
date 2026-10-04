using DbUp;
using Npgsql;

namespace ProjectHub.Api.Infrastructure.Database;

/// <summary>
/// Applies the versioned SQL scripts from database/migrations that have not run yet.
/// Production migrations need human approval (ai/AI_WORKFLOW.md) and are not run automatically.
/// </summary>
public static class DatabaseMigrator
{
    private const string ScriptPrefix = "ProjectHub.Migrations.";

    /// <summary>Command line argument that only migrates and exits (a failure ends the process with an error).</summary>
    public const string MigrateOnlyArgument = "--migrate";

    /// <summary>Migrates the database from <see cref="ProjectHubSettings.DatabaseConnectionKey"/> without starting the API.</summary>
    public static void MigrateOnly()
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var connectionString = configuration[ProjectHubSettings.DatabaseConnectionKey] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Configuration value '{ProjectHubSettings.DatabaseConnectionKey}' is missing.");
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddJsonConsole(json => json.UseUtcTimestamp = true));
        Migrate(connectionString, loggerFactory.CreateLogger("ProjectHub.Migrations"));
    }

    /// <summary>
    /// Several instances may start at once. A session-level advisory lock on its own connection lets one of them
    /// migrate while the others wait and then find nothing left to do.
    /// </summary>
    public static void Migrate(string connectionString, ILogger logger)
    {
        using var lockConnection = new NpgsqlConnection(connectionString);
        lockConnection.Open();
        Execute(lockConnection, "select pg_advisory_lock(hashtextextended('projecthub:migrations', 0))");
        try
        {
            Upgrade(connectionString, logger);
        }
        finally
        {
            Execute(lockConnection, "select pg_advisory_unlock(hashtextextended('projecthub:migrations', 0))");
        }
    }

    private static void Execute(NpgsqlConnection connection, string sql)
    {
        using var command = new NpgsqlCommand(sql, connection);
        command.ExecuteNonQuery();
    }

    private static void Upgrade(string connectionString, ILogger logger)
    {
        var upgrader = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(typeof(DatabaseMigrator).Assembly, name => name.StartsWith(ScriptPrefix, StringComparison.Ordinal))
            .WithTransactionPerScript()
            .LogToNowhere()
            .Build();

        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
        {
            throw new InvalidOperationException($"Database migration failed in script '{result.ErrorScript?.Name}'.", result.Error);
        }

        foreach (var script in result.Scripts)
        {
            logger.LogInformation("Applied migration {Script}", script.Name);
        }
    }
}
