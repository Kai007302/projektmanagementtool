namespace ProjectHub.Api;

/// <summary>
/// Infrastructure settings read from environment variables (see .env.example).
/// </summary>
public sealed record ProjectHubSettings(
    string DatabaseConnection,
    string RedisConnection,
    bool ApplyMigrationsOnStartup)
{
    public const string DatabaseConnectionKey = "PROJECTHUB_DB_CONNECTION";
    public const string RedisConnectionKey = "REDIS_CONNECTION";
    public const string ApplyMigrationsOnStartupKey = "PROJECTHUB_APPLY_MIGRATIONS";

    public static ProjectHubSettings FromConfiguration(IConfiguration configuration) =>
        new(
            Required(configuration, DatabaseConnectionKey),
            Required(configuration, RedisConnectionKey),
            configuration.GetValue(ApplyMigrationsOnStartupKey, false));

    private static string Required(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Configuration value '{key}' is missing. See .env.example.");
        }

        return value;
    }
}
