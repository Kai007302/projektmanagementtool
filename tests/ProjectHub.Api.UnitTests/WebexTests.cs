using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using ProjectHub.Api.Modules.Integrations.Webex;
using ProjectHub.Api.Modules.Notifications;

namespace ProjectHub.Api.UnitTests;

public sealed class WebexTests
{
    private static readonly WebexOptions Options = new(WebexOptions.Bot, "bot-token", "secret", null);

    [Fact]
    public async Task Bot_sends_direct_messages_with_its_token()
    {
        var handler = new Handler(_ => Json(HttpStatusCode.OK, """{"id":"m1"}"""));

        await Client(handler).SendDirectMessageAsync("clara@contoso.example", "**Titel**", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://webexapis.com/v1/messages", request.Uri);
        Assert.Equal("Bearer bot-token", request.Authorization);
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal("clara@contoso.example", body.RootElement.GetProperty("toPersonEmail").GetString());
        Assert.Equal("**Titel**", body.RootElement.GetProperty("markdown").GetString());
    }

    [Fact]
    public async Task Bot_creates_spaces_invites_and_ignores_existing_members()
    {
        var handler = new Handler(request => request.Uri switch
        {
            "https://webexapis.com/v1/rooms" => Json(HttpStatusCode.OK, """{"id":"room-1","title":"Intranet"}"""),
            _ when request.Body.Contains("ben@", StringComparison.Ordinal) => Json(HttpStatusCode.Conflict, """{"message":"ben@contoso.example is already a member"}"""),
            _ => Json(HttpStatusCode.OK, """{"id":"membership"}"""),
        });
        var client = Client(handler);

        var space = await client.CreateSpaceAsync("Intranet", CancellationToken.None);
        await client.AddMemberAsync(space.RoomId, "clara@contoso.example", CancellationToken.None);
        await client.AddMemberAsync(space.RoomId, "ben@contoso.example", CancellationToken.None);

        Assert.Equal("room-1", space.RoomId);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Contains("\"roomId\":\"room-1\"", handler.Requests[1].Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.BadGateway, true)]
    [InlineData(HttpStatusCode.Unauthorized, true)]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    public async Task Failures_keep_only_the_status(HttpStatusCode status, bool transient)
    {
        var handler = new Handler(_ => Json(status, """{"message":"clara@contoso.example not found","trackingId":"x"}"""));

        var error = await Assert.ThrowsAsync<MessageDeliveryException>(() =>
            Client(handler).SendDirectMessageAsync("clara@contoso.example", "x", CancellationToken.None));

        Assert.Equal(((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture), error.Code);
        Assert.Equal(transient, error.Transient);
    }

    [Fact]
    public async Task Bot_person_id_is_asked_once()
    {
        var handler = new Handler(_ => Json(HttpStatusCode.OK, """{"id":"bot-1"}"""));
        var client = Client(handler);

        Assert.Equal("bot-1", await client.BotPersonIdAsync(CancellationToken.None));
        Assert.Equal("bot-1", await client.BotPersonIdAsync(CancellationToken.None));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public void Signatures_are_checked_over_the_raw_body()
    {
        var body = Encoding.UTF8.GetBytes("""{"resource":"memberships"}""");
        var signature = Convert.ToHexStringLower(HMACSHA1.HashData(Encoding.UTF8.GetBytes("secret"), body));

        Assert.True(WebexWebhookService.SignatureValid(body, signature, "secret"));
        Assert.True(WebexWebhookService.SignatureValid(body, signature.ToUpperInvariant(), "secret"));
        Assert.False(WebexWebhookService.SignatureValid(body, signature, "other"));
        Assert.False(WebexWebhookService.SignatureValid([.. body, (byte)' '], signature, "secret"));
        Assert.False(WebexWebhookService.SignatureValid(body, null, "secret"));
        Assert.False(WebexWebhookService.SignatureValid(body, "abc", "secret"));
    }

    [Fact]
    public void Payloads_get_a_key_from_resource_event_and_object()
    {
        var payload = WebexWebhookService.Parse(Encoding.UTF8.GetBytes(
            """{"id":"wh-1","resource":"memberships","event":"deleted","data":{"id":"mem-1","roomId":"room-1","personId":"bot-1"}}"""));

        Assert.Equal(new WebexWebhookService.Payload("memberships:deleted:mem-1", "memberships", "deleted", "wh-1", "room-1", "bot-1"), payload);
        Assert.Null(WebexWebhookService.Parse(Encoding.UTF8.GetBytes("""{"resource":"memberships","event":"deleted"}""")));
        Assert.Null(WebexWebhookService.Parse(Encoding.UTF8.GetBytes("not json")));
        Assert.Null(WebexWebhookService.Parse(Encoding.UTF8.GetBytes("""{"resource":1,"event":"x","data":{"id":"y"}}""")));
    }

    [Fact]
    public void Room_ids_become_app_links()
    {
        var space = Guid.NewGuid();
        var roomId = Convert.ToBase64String(Encoding.UTF8.GetBytes($"ciscospark://us/ROOM/{space}")).TrimEnd('=');

        Assert.Equal($"webexteams://im?space={space}", WebexRooms.AppLink(roomId));
        Assert.NotNull(WebexRooms.AppLink(WebexRooms.NewRoomId()));
        Assert.Null(WebexRooms.AppLink("room-1"));
        Assert.Null(WebexRooms.AppLink(Convert.ToBase64String(Encoding.UTF8.GetBytes("ciscospark://us/PEOPLE/x"))));
    }

    [Theory]
    [InlineData("https://contoso.webex.com/meet/ben", true)]
    [InlineData("https://webex.com/x", true)]
    [InlineData("http://contoso.webex.com/meet/ben", false)]
    [InlineData("https://webex.com.evil.example/x", false)]
    [InlineData("https://evilwebex.com/x", false)]
    [InlineData("https://user@contoso.webex.com/x", false)]
    [InlineData("https://contoso.webex.com:8443/x", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("", false)]
    public void Only_https_webex_links_are_accepted(string url, bool accepted) =>
        Assert.Equal(accepted, WebexLinkService.IsWebexUrl(url));

    [Fact]
    public void Markdown_in_titles_is_escaped()
    {
        Assert.Equal("\\[Klick\\]\\(https://evil\\) \\*fett\\*", NotificationDispatcher.WebexMarkdown("[Klick](https://evil) *fett*"));
        Assert.Equal("a b", NotificationDispatcher.WebexMarkdown("a\nb"));
    }

    [Theory]
    [InlineData("Development", null, "fake")]
    [InlineData("Production", null, "off")]
    [InlineData("Production", "fake", "fake")]
    public void Transport_defaults_to_fake_only_in_development(string environment, string? transport, string expected)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [WebexOptions.TransportKey] = transport }).Build();

        Assert.Equal(expected, WebexOptions.FromConfiguration(configuration, new Environment(environment)).Transport);
    }

    [Theory]
    [InlineData("bot", "__SET_VIA_SECRET_STORE__")]
    [InlineData("teams", "token")]
    public void Bot_needs_a_token(string transport, string token)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [WebexOptions.TransportKey] = transport,
            [WebexOptions.BotTokenKey] = token,
        }).Build();

        Assert.Throws<InvalidOperationException>(() => WebexOptions.FromConfiguration(configuration, new Environment("Production")));
    }

    private static WebexBotClient Client(Handler handler) => new(new Clients(handler), Options);

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed record Recorded(string Uri, string? Authorization, string Body);

    private sealed class Handler(Func<Recorded, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Recorded> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var recorded = new Recorded(
                request.RequestUri!.AbsoluteUri,
                request.Headers.Authorization?.ToString(),
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            Requests.Add(recorded);
            return respond(recorded);
        }
    }

    private sealed class Clients(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = WebexBotClient.BaseAddress };
    }

    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "ProjectHub";
        public string ContentRootPath { get; set; } = "/";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
