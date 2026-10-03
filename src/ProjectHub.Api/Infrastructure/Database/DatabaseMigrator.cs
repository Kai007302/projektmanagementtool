using DbUp;

namespace ProjectHub.Api.Infrastructure.Database;

/// <summary>
/// Applies the versioned SQL scripts from database/migrations that have not run yet.
/// Production migrations need human approval (ai/AI_WORKFLOW.md) and are not run automatically.
/// </summary>
public static class DatabaseMigrator
{
    private const string ScriptPrefix = "ProjectHub.Migrations.";

    public static void Migrate(string connectionString, ILogger logger)
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
