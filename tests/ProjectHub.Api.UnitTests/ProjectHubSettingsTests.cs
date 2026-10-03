using Microsoft.Extensions.Configuration;

namespace ProjectHub.Api.UnitTests;

public class ProjectHubSettingsTests
{
    [Fact]
    public void Reads_values_from_configuration()
    {
        var settings = ProjectHubSettings.FromConfiguration(Configuration(new()
        {
            [ProjectHubSettings.DatabaseConnectionKey] = "Host=db",
            [ProjectHubSettings.RedisConnectionKey] = "redis:6379",
            [ProjectHubSettings.ApplyMigrationsOnStartupKey] = "true",
        }));

        Assert.Equal("Host=db", settings.DatabaseConnection);
        Assert.Equal("redis:6379", settings.RedisConnection);
        Assert.True(settings.ApplyMigrationsOnStartup);
    }

    [Fact]
    public void Migrations_are_not_applied_on_startup_by_default()
    {
        var settings = ProjectHubSettings.FromConfiguration(Configuration(new()
        {
            [ProjectHubSettings.DatabaseConnectionKey] = "Host=db",
            [ProjectHubSettings.RedisConnectionKey] = "redis:6379",
        }));

        Assert.False(settings.ApplyMigrationsOnStartup);
    }

    [Theory]
    [InlineData(ProjectHubSettings.DatabaseConnectionKey)]
    [InlineData(ProjectHubSettings.RedisConnectionKey)]
    public void Missing_connection_fails_fast(string missingKey)
    {
        var values = new Dictionary<string, string?>
        {
            [ProjectHubSettings.DatabaseConnectionKey] = "Host=db",
            [ProjectHubSettings.RedisConnectionKey] = "redis:6379",
        };
        values.Remove(missingKey);

        var error = Assert.Throws<InvalidOperationException>(() => ProjectHubSettings.FromConfiguration(Configuration(values)));
        Assert.Contains(missingKey, error.Message);
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
