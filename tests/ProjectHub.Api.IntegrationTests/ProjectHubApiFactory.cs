using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ProjectHub.Api.IntegrationTests;

public sealed class ProjectHubApiFactory(string databaseConnection, string redisConnection, bool applyMigrations = true)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting(ProjectHubSettings.DatabaseConnectionKey, databaseConnection);
        builder.UseSetting(ProjectHubSettings.RedisConnectionKey, redisConnection);
        builder.UseSetting(ProjectHubSettings.ApplyMigrationsOnStartupKey, applyMigrations.ToString());
    }
}
