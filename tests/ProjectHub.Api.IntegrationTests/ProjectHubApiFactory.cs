using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using ProjectHub.Api.Modules.Identity.Development;

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
        builder.UseSetting(ProjectHubSettings.SeedDevelopmentDataKey, applyMigrations.ToString());
    }

    /// <summary>A client signed in as the given synthetic user.</summary>
    public HttpClient CreateClientFor(DevelopmentSeedData.SeedUser user)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(DevelopmentIdentityOptions.UserHeader, user.ObjectId);
        return client;
    }
}
