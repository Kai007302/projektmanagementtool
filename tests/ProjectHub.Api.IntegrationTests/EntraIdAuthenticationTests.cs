using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Modules.Identity;
using ProjectHub.Api.Modules.Identity.Development;
using ProjectHub.Api.Modules.Integrations.Webex;

namespace ProjectHub.Api.IntegrationTests;

/// <summary>Outside Development the API only accepts Entra ID tokens; the development identity is gone.</summary>
[Collection(InfrastructureCollection.Name)]
public sealed class EntraIdAuthenticationTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private WebApplicationFactory<Program> factory = null!;

    private const string WebhookSecret = "production-webhook-secret";

    public Task InitializeAsync()
    {
        // Outside Development the API never migrates by itself.
        DatabaseMigrator.Migrate(infrastructure.Postgres.GetConnectionString(), NullLogger.Instance);
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting(ProjectHubSettings.DatabaseConnectionKey, infrastructure.Postgres.GetConnectionString());
            builder.UseSetting(ProjectHubSettings.RedisConnectionKey, infrastructure.Redis.GetConnectionString());
            builder.UseSetting(EntraIdRegistration.TenantIdKey, "00000000-0000-0000-0000-000000000000");
            builder.UseSetting(EntraIdRegistration.ClientIdKey, "00000000-0000-0000-0000-000000000000");
            builder.UseSetting(WebexOptions.WebhookSecretKey, WebhookSecret);
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

    [Fact]
    public async Task Webex_webhooks_need_a_signature_but_no_token()
    {
        var body = $$$"""{"id":"wh-prod","resource":"messages","event":"created","data":{"id":"{{{Guid.NewGuid()}}}"}}""";
        var signature = Convert.ToHexStringLower(HMACSHA1.HashData(Encoding.UTF8.GetBytes(WebhookSecret), Encoding.UTF8.GetBytes(body)));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/integrations/webex/webhook")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(WebexWebhookService.SignatureHeader, signature);

        var signed = await factory.CreateClient().SendAsync(request);
        var unsigned = await factory.CreateClient().PostAsync("/api/v1/integrations/webex/webhook", new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, signed.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unsigned.StatusCode);
        Assert.DoesNotContain(unsigned.Headers.WwwAuthenticate, h => h.Scheme == "Bearer");
    }
}
