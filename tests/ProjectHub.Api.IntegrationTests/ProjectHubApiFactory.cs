using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using ProjectHub.Api.Modules.Attachments;
using ProjectHub.Api.Modules.Identity.Development;
using ProjectHub.Api.Modules.Whiteboard;

namespace ProjectHub.Api.IntegrationTests;

public sealed class ProjectHubApiFactory(
    string databaseConnection,
    string redisConnection,
    bool applyMigrations = true,
    Action<IWebHostBuilder>? configure = null)
    : WebApplicationFactory<Program>
{
    public string AttachmentDirectory { get; } = Path.Combine(Path.GetTempPath(), "projecthub-tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting(ProjectHubSettings.DatabaseConnectionKey, databaseConnection);
        builder.UseSetting(ProjectHubSettings.RedisConnectionKey, redisConnection);
        builder.UseSetting(ProjectHubSettings.ApplyMigrationsOnStartupKey, applyMigrations.ToString());
        builder.UseSetting(ProjectHubSettings.SeedDevelopmentDataKey, applyMigrations.ToString());
        builder.UseSetting(AttachmentOptions.DirectoryKey, AttachmentDirectory);
        builder.UseSetting(WhiteboardOptions.DirectoryKey, Path.Combine(AttachmentDirectory, "whiteboards"));

        // Tests compact explicitly; the background service would make them depend on timing.
        builder.UseSetting(WhiteboardOptions.CompactionIntervalKey, "0");
        configure?.Invoke(builder);
    }

    /// <summary>A client signed in as the given synthetic user.</summary>
    public HttpClient CreateClientFor(DevelopmentSeedData.SeedUser user)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(DevelopmentIdentityOptions.UserHeader, user.ObjectId);
        return client;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (Directory.Exists(AttachmentDirectory))
        {
            Directory.Delete(AttachmentDirectory, recursive: true);
        }
    }
}
