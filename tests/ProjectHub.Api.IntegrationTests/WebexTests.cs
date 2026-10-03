using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using ProjectHub.Api.Modules.Integrations.Webex;
using ProjectHub.Api.Modules.Notifications;
using static ProjectHub.Api.Modules.Identity.Development.DevelopmentSeedData;

namespace ProjectHub.Api.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class WebexTests(InfrastructureFixture infrastructure) : ApiTestBase(infrastructure)
{
    private const string Secret = "test-webhook-secret";

    protected override ProjectHubApiFactory CreateFactory() =>
        new(Infrastructure.Postgres.GetConnectionString(), Infrastructure.Redis.GetConnectionString(), configure: builder =>
        {
            builder.UseSetting(WebexOptions.WebhookSecretKey, Secret);
            builder.UseSetting(WebexOptions.PublicApiUrlKey, "https://projecthub.example/");
        });

    private FakeWebexClient Fake => Factory.Services.GetRequiredService<FakeWebexClient>();

    [Fact]
    public async Task Editors_link_meetings_and_viewers_see_them()
    {
        var project = await CreateTeamProjectAsync();

        var created = await As(Clara).PostAsJsonAsync($"/api/v1/projects/{project.Id}/webex/links",
            new CreateWebexLinkRequest("meeting", " Jour fixe ", "https://contoso.webex.com/meet/ben"));
        var denied = await As(David).PostAsJsonAsync($"/api/v1/projects/{project.Id}/webex/links",
            new CreateWebexLinkRequest("meeting", "Jour fixe", "https://contoso.webex.com/meet/ben"));
        var invalid = await As(Clara).PostAsJsonAsync($"/api/v1/projects/{project.Id}/webex/links",
            new CreateWebexLinkRequest("meeting", "Phishing", "https://webex.com.evil.example/meet"));
        var wrongKind = await As(Clara).PostAsJsonAsync($"/api/v1/projects/{project.Id}/webex/links",
            new CreateWebexLinkRequest("call", "x", "https://contoso.webex.com/meet/ben"));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, wrongKind.StatusCode);
        var shown = (await As(Eva).GetFromJsonAsync<ProjectWebexResponse>($"/api/v1/projects/{project.Id}/webex"))!;
        Assert.True(shown.Available);
        var link = Assert.Single(shown.Links);
        Assert.Equal(("meeting", "Jour fixe", "https://contoso.webex.com/meet/ben", "active", false), (link.Kind, link.Title, link.Url, link.Status, link.CreatedByBot));
        Assert.Equal(HttpStatusCode.NotFound, (await As(Felix).GetAsync($"/api/v1/projects/{project.Id}/webex")).StatusCode);
        Assert.Equal(1, await ScalarAsync("select count(*) from activity_log where project_id = $1 and action = 'WebexLinkAdded'", project.Id));

        Assert.Equal(HttpStatusCode.Forbidden, (await As(Eva).DeleteAsync($"/api/v1/webex-links/{link.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await As(Fritz).DeleteAsync($"/api/v1/webex-links/{link.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await As(Clara).DeleteAsync($"/api/v1/webex-links/{link.Id}")).StatusCode);
        Assert.Empty((await As(Eva).GetFromJsonAsync<ProjectWebexResponse>($"/api/v1/projects/{project.Id}/webex"))!.Links);
    }

    [Fact]
    public async Task The_bot_creates_one_project_space_with_the_members()
    {
        var project = await CreateTeamProjectAsync();

        var response = await As(Ben).PostAsync($"/api/v1/projects/{project.Id}/webex/space", null);
        var second = await As(Clara).PostAsync($"/api/v1/projects/{project.Id}/webex/space", null);
        var viewer = await As(Eva).PostAsync($"/api/v1/projects/{project.Id}/webex/space", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, viewer.StatusCode);
        var link = (await response.Content.ReadFromJsonAsync<WebexLinkResponse>())!;
        Assert.Equal(("space", project.Name, true), (link.Kind, link.Title, link.CreatedByBot));
        Assert.StartsWith("webexteams://im?space=", link.Url, StringComparison.Ordinal);
        var space = Assert.Single(Fake.Spaces, s => s.Title == project.Name);
        Assert.Equal(new[] { Ben, Clara, David, Eva, Gina }.Select(u => u.Email).Order(StringComparer.OrdinalIgnoreCase), space.Members);
        Assert.Contains(Fake.Messages, m => m.RoomId == space.RoomId && m.Markdown.Contains("In ProjectHub öffnen", StringComparison.Ordinal));
        Assert.Equal(1, await ScalarAsync("select count(*) from project_webex_link where project_id = $1", project.Id));
    }

    [Fact]
    public async Task Notifications_arrive_as_direct_messages_when_switched_on()
    {
        var project = await CreateTeamProjectAsync();
        var title = $"Webex {Guid.NewGuid():N}";
        try
        {
            var preferences = await As(David).PutAsJsonAsync("/api/v1/me/notification-preferences", new UpdatePreferencesRequest(true, true, true));
            Assert.Equal(new NotificationPreferencesResponse(true, true, true, true), await preferences.Content.ReadFromJsonAsync<NotificationPreferencesResponse>());
            var kept = await As(David).PutAsJsonAsync("/api/v1/me/notification-preferences", new UpdatePreferencesRequest(true, true));
            Assert.True((await kept.Content.ReadFromJsonAsync<NotificationPreferencesResponse>())!.WebexEnabled);

            await CreateTaskAsync(Ben, project.Id, NewTask(title, assigneeId: David.Id));
            Assert.Equal(1, await ScalarAsync("select count(*) from mail_outbox where subject like $1 and channel = 'webex'", $"%{title}%"));
            Assert.Equal(1, await ScalarAsync("select count(*) from mail_outbox where subject like $1 and channel = 'email'", $"%{title}%"));
            await DeliverMailsAsync();

            var message = Assert.Single(Fake.Messages, m => m.Markdown.Contains(title, StringComparison.Ordinal));
            Assert.Equal(David.Email, message.ToPersonEmail);
            Assert.Contains("[In ProjectHub öffnen](", message.Markdown, StringComparison.Ordinal);
            Assert.Equal(1, await ScalarAsync("select count(*) from mail_outbox where subject like $1 and channel = 'webex' and status = 'sent'", $"%{title}%"));
        }
        finally
        {
            await As(David).PutAsJsonAsync("/api/v1/me/notification-preferences", new UpdatePreferencesRequest(true, true, false));
        }
    }

    [Fact]
    public async Task Removing_the_bot_disconnects_the_space_once()
    {
        var project = await CreateTeamProjectAsync();
        var link = (await (await As(Ben).PostAsync($"/api/v1/projects/{project.Id}/webex/space", null)).Content.ReadFromJsonAsync<WebexLinkResponse>())!;
        var roomId = Assert.Single(Fake.Spaces, s => s.Title == project.Name).RoomId;
        var membershipId = Guid.NewGuid().ToString();
        var someoneElse = Payload("memberships", "deleted", Guid.NewGuid().ToString(), roomId, "person-clara");
        var botRemoved = Payload("memberships", "deleted", membershipId, roomId, FakeWebexClient.BotId);

        Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync(someoneElse)).StatusCode);
        Assert.Equal("active", await StatusAsync(project.Id));

        Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync(botRemoved)).StatusCode);
        Assert.Equal("disconnected", await StatusAsync(project.Id));

        // Webex retries: the same delivery is recorded once and changes nothing.
        await ScalarAsync("update project_webex_link set status = 'active' where id = $1", link.Id);
        Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync(botRemoved)).StatusCode);
        Assert.Equal("active", await StatusAsync(project.Id));
        Assert.Equal(1, await ScalarAsync("select count(*) from webhook_event where event_key = $1 and correlation_id is not null", $"memberships:deleted:{membershipId}"));
    }

    [Fact]
    public async Task Webhooks_without_a_valid_signature_are_rejected()
    {
        var body = Payload("memberships", "deleted", Guid.NewGuid().ToString(), "room", FakeWebexClient.BotId);
        var client = Factory.CreateClient();

        var unsigned = await client.PostAsync("/api/v1/integrations/webex/webhook", new StringContent(body, Encoding.UTF8, "application/json"));
        var wrong = await PostWebhookAsync(body, signature: Sign(body, "other-secret"));
        var tampered = await PostWebhookAsync(body + " ", signature: Sign(body, Secret));
        var malformed = await PostWebhookAsync("""{"resource":"memberships"}""");

        Assert.Equal(HttpStatusCode.Unauthorized, unsigned.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, tampered.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
    }

    [Fact]
    public async Task Admins_register_the_webhook()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await As(Ben).PostAsync("/api/v1/admin/webex/webhook", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await As(Ben).GetAsync("/api/v1/admin/webex")).StatusCode);

        var response = await As(Ada).PostAsync("/api/v1/admin/webex/webhook", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var webhook = (await response.Content.ReadFromJsonAsync<WebexWebhookResponse>())!;
        var registered = Assert.Single(Fake.Webhooks, w => w.Id == webhook.ExternalId);
        Assert.Equal(new Uri("https://projecthub.example/api/v1/integrations/webex/webhook"), registered.Target);
        Assert.Equal(("memberships", "deleted"), (registered.Resource, registered.Event));
        var status = (await As(Ada).GetFromJsonAsync<WebexStatusResponse>("/api/v1/admin/webex"))!;
        Assert.Equal(("fake", true), (status.Transport, status.WebhookSecretConfigured));
        Assert.Contains(status.Webhooks, w => w.ExternalId == webhook.ExternalId);
        Assert.Equal(1, await ScalarAsync("select count(*) from audit_log where action = 'WebexWebhookRegistered' and resource_id = $1", webhook.Id));
        var fabrikam = (await As(Fritz).GetFromJsonAsync<WebexStatusResponse>("/api/v1/admin/webex"))!;
        Assert.DoesNotContain(fabrikam.Webhooks, w => w.ExternalId == webhook.ExternalId);
    }

    private Task<string?> StatusAsync(Guid projectId) =>
        ScalarTextAsync("select status from project_webex_link where project_id = $1", projectId);

    private async Task<string?> ScalarTextAsync(string sql, object value)
    {
        await using var dataSource = Npgsql.NpgsqlDataSource.Create(Infrastructure.Postgres.GetConnectionString());
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.Add(new Npgsql.NpgsqlParameter { Value = value });
        return (string?)await command.ExecuteScalarAsync();
    }

    private static string Payload(string resource, string @event, string dataId, string roomId, string personId) =>
        $$$"""{"id":"wh-1","resource":"{{{resource}}}","event":"{{{@event}}}","data":{"id":"{{{dataId}}}","roomId":"{{{roomId}}}","personId":"{{{personId}}}"}}""";

    private static string Sign(string body, string secret) =>
        Convert.ToHexStringLower(HMACSHA1.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body)));

    private Task<HttpResponseMessage> PostWebhookAsync(string body, string? signature = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/integrations/webex/webhook")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(WebexWebhookService.SignatureHeader, signature ?? Sign(body, Secret));
        return Factory.CreateClient().SendAsync(request);
    }
}
