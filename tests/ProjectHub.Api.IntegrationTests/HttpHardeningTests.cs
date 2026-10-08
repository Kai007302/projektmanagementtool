using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Modules.Identity;
using ProjectHub.Api.Modules.Integrations.Webex;
using ProjectHub.Api.Modules.Tasks;
using static ProjectHub.Api.Modules.Identity.Development.DevelopmentSeedData;

namespace ProjectHub.Api.IntegrationTests;

/// <summary>Security headers, rate limits, request size and proxy trust (ADR 0012).</summary>
[Collection(InfrastructureCollection.Name)]
public sealed class HttpHardeningTests(InfrastructureFixture infrastructure) : ApiTestBase(infrastructure)
{
    protected override ProjectHubApiFactory CreateFactory() =>
        new(Infrastructure.Postgres.GetConnectionString(), Infrastructure.Redis.GetConnectionString(), configure: builder =>
        {
            builder.UseSetting(HttpHardeningOptions.ApiRequestsPerMinuteKey, "5");
            builder.UseSetting(HttpHardeningOptions.UploadsPerMinuteKey, "1");
            builder.UseSetting(HttpHardeningOptions.WebhooksPerMinuteKey, "1");
            builder.UseSetting(WebexOptions.WebhookSecretKey, "secret");
        });

    [Fact]
    public async Task Api_responses_can_not_be_rendered_framed_or_cached()
    {
        var response = await As(Ada).GetAsync("/api/v1/me");
        var problem = await As(Ada).GetAsync($"/api/v1/projects/{Guid.NewGuid()}");

        foreach (var r in new[] { response, problem })
        {
            Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
            Assert.Equal("DENY", r.Headers.GetValues("X-Frame-Options").Single());
            Assert.Equal("default-src 'none'; frame-ancestors 'none'", r.Headers.GetValues("Content-Security-Policy").Single());
            Assert.Equal("no-referrer", r.Headers.GetValues("Referrer-Policy").Single());
            Assert.True(r.Headers.CacheControl?.NoStore);
        }

        // HSTS only outside Development.
        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task Too_many_requests_get_429_per_person_and_health_is_never_limited()
    {
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await As(Clara).GetAsync("/api/v1/me")).StatusCode);
        }

        var rejected = await As(Clara).GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(int.Parse(rejected.Headers.GetValues("Retry-After").Single()) is > 0 and <= 60);
        Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType?.MediaType);

        // Another person has an own budget; probes always pass.
        Assert.Equal(HttpStatusCode.OK, (await As(David).GetAsync("/api/v1/me")).StatusCode);
        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await Factory.CreateClient().GetAsync("/health/live")).StatusCode);
        }
    }

    [Fact]
    public async Task Uploads_and_webhooks_have_their_own_tighter_limits()
    {
        async Task<HttpStatusCode> Upload() =>
            (await As(Eva).PostAsync($"/api/v1/tasks/{Guid.NewGuid()}/attachments",
                new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "file", "a.txt" } })).StatusCode;

        Assert.Equal(HttpStatusCode.NotFound, await Upload());
        Assert.Equal(HttpStatusCode.TooManyRequests, await Upload());

        var webhook = Factory.CreateClient();
        HttpContent Body() => new StringContent("{}", Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.Unauthorized, (await webhook.PostAsync("/api/v1/integrations/webex/webhook", Body())).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await webhook.PostAsync("/api/v1/integrations/webex/webhook", Body())).StatusCode);
    }
}

/// <summary>Behaviour that only exists outside Development or on a real Kestrel server.</summary>
[Collection(InfrastructureCollection.Name)]
public sealed class ProductionHttpHardeningTests(InfrastructureFixture infrastructure)
{
    private WebApplicationFactory<Program> Production(params (string Key, string Value)[] settings) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting(ProjectHubSettings.DatabaseConnectionKey, infrastructure.Postgres.GetConnectionString());
            builder.UseSetting(ProjectHubSettings.RedisConnectionKey, infrastructure.Redis.GetConnectionString());
            builder.UseSetting(EntraIdRegistration.TenantIdKey, "00000000-0000-0000-0000-000000000000");
            builder.UseSetting(EntraIdRegistration.ClientIdKey, "00000000-0000-0000-0000-000000000000");
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        });

    [Fact]
    public async Task Hsts_is_sent_on_https_only()
    {
        await using var factory = Production();
        var https = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://projecthub.example") });
        var http = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://projecthub.example") });

        Assert.Equal("max-age=31536000", (await https.GetAsync("/health/live")).Headers.GetValues("Strict-Transport-Security").Single());
        Assert.False((await http.GetAsync("/health/live")).Headers.Contains("Strict-Transport-Security"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_scheme_from_the_proxy_counts_only_when_trusted(bool trusted)
    {
        await using var factory = Production((HttpHardeningOptions.TrustForwardedHeadersKey, trusted.ToString()));
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://projecthub.example") });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Forwarded-Proto", "https");

        var response = await client.SendAsync(request);

        Assert.Equal(trusted, response.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task Requests_above_the_limit_are_rejected_but_uploads_have_their_own()
    {
        await using var factory = new ProjectHubApiFactory(
            infrastructure.Postgres.GetConnectionString(), infrastructure.Redis.GetConnectionString(),
            configure: builder => builder.UseSetting(HttpHardeningOptions.MaxRequestBytesKey, "1024"));
        factory.UseKestrel(0);
        factory.StartServer();
        var client = factory.CreateClientFor(Ada);

        var project = (await (await client.PostAsJsonAsync("/api/v1/projects",
            new Modules.Projects.CreateProjectRequest("Limits", null, null, null, null))).Content.ReadFromJsonAsync<Modules.Projects.ProjectSummary>())!;
        var tooLarge = await client.PostAsJsonAsync($"/api/v1/projects/{project.Id}/tasks",
            new CreateTaskRequest("Aufgabe", new string('x', 4000), null, null, null, null, null, null, null));
        var task = await client.PostAsJsonAsync($"/api/v1/projects/{project.Id}/tasks",
            new CreateTaskRequest("Aufgabe", null, null, null, null, null, null, null, null));
        var taskId = (await task.Content.ReadFromJsonAsync<TaskResponse>())!.Id;
        var upload = await client.PostAsync($"/api/v1/tasks/{taskId}/attachments",
            new MultipartFormDataContent { { new ByteArrayContent(new byte[8192]), "file", "gross.txt" } });

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);
        Assert.Equal(HttpStatusCode.Created, task.StatusCode);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
    }
}
