using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using ProjectHub.Api.Modules.Identity;
using ProjectHub.Api.Modules.Identity.Development;

namespace ProjectHub.Api.IntegrationTests;

/// <summary>Outside Development the API only accepts Entra ID tokens; the development identity is gone.</summary>
[Collection(InfrastructureCollection.Name)]
public sealed class EntraIdAuthenticationTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private WebApplicationFactory<Program> factory = null!;

    public Task InitializeAsync()
    {
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting(ProjectHubSettings.DatabaseConnectionKey, infrastructure.Postgres.GetConnectionString());
            builder.UseSetting(ProjectHubSettings.RedisConnectionKey, infrastructure.Redis.GetConnectionString());
            builder.UseSetting(EntraIdRegistration.TenantIdKey, "00000000-0000-0000-0000-000000000000");
            builder.UseSetting(EntraIdRegistration.ClientIdKey, "00000000-0000-0000-0000-000000000000");
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await factory.DisposeAsync();

    [Fact]
    public async Task Api_requires_a_bearer_token()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, h => h.Scheme == "Bearer");
    }

    [Fact]
    public async Task Development_user_header_is_ignored()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevelopmentIdentityOptions.UserHeader, DevelopmentSeedData.Ada.ObjectId);

        var response = await client.GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Development_endpoints_do_not_exist()
    {
        var response = await factory.CreateClient().GetAsync("/api/dev/users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_endpoints_stay_anonymous()
    {
        var response = await factory.CreateClient().GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
