using System.Net;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProjectHub.Api.Modules.Integrations.Microsoft;
using ProjectHub.Api.Modules.Notifications;

namespace ProjectHub.Api.UnitTests;

public sealed class GraphEmailSenderTests
{
    private static readonly GraphMailOptions Options = new("tenant", "client", "secret", "projecthub@contoso.example");
    private static readonly EmailMessage Mail = new(Guid.NewGuid(), Guid.NewGuid(), "clara@contoso.example", "Ben hat dir „Texte“ zugewiesen", "Ben hat dir „Texte“ zugewiesen\n\nIn ProjectHub öffnen: https://app");

    [Fact]
    public async Task Sends_one_mail_from_the_functional_mailbox_without_a_copy_in_sent_items()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Accepted));

        await Sender(handler).SendAsync(Mail, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://graph.microsoft.com/v1.0/users/projecthub%40contoso.example/sendMail", request.Uri);
        Assert.Equal("Bearer token-1", request.Authorization);
        using var body = JsonDocument.Parse(request.Body);
        var message = body.RootElement.GetProperty("message");
        Assert.Equal(Mail.Subject, message.GetProperty("subject").GetString());
        Assert.Equal("Text", message.GetProperty("body").GetProperty("contentType").GetString());
        Assert.Equal(Mail.Body, message.GetProperty("body").GetProperty("content").GetString());
        Assert.Equal(Mail.To, message.GetProperty("toRecipients")[0].GetProperty("emailAddress").GetProperty("address").GetString());
        Assert.False(body.RootElement.GetProperty("saveToSentItems").GetBoolean());
    }

    [Fact]
    public async Task Throttling_is_transient_and_keeps_retry_after()
    {
        var handler = new RecordingHandler(_ =>
        {
            var response = Error(HttpStatusCode.TooManyRequests, "ApplicationThrottled");
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(42));
            return response;
        });

        var error = await Assert.ThrowsAsync<EmailDeliveryException>(() => Sender(handler).SendAsync(Mail, CancellationToken.None));

        Assert.True(error.Transient);
        Assert.Equal("429 ApplicationThrottled", error.Code);
        Assert.Equal(TimeSpan.FromSeconds(42), error.RetryAfter);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.Unauthorized, true)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    public async Task Classifies_failures(HttpStatusCode status, bool transient)
    {
        var handler = new RecordingHandler(_ => Error(status, "SomeCode"));

        var error = await Assert.ThrowsAsync<EmailDeliveryException>(() => Sender(handler).SendAsync(Mail, CancellationToken.None));

        Assert.Equal(transient, error.Transient);
        Assert.Equal($"{(int)status} SomeCode", error.Code);
    }

    [Fact]
    public async Task Error_codes_never_carry_the_message_or_addresses()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("""{"error":{"code":"ErrorAccessDenied","message":"Access to projecthub@contoso.example denied"}}"""),
        });
        var odd = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"error":{"code":"clara@contoso.example is invalid","message":"x"}}"""),
        });

        var denied = await Assert.ThrowsAsync<EmailDeliveryException>(() => Sender(handler).SendAsync(Mail, CancellationToken.None));
        var invalid = await Assert.ThrowsAsync<EmailDeliveryException>(() => Sender(odd).SendAsync(Mail, CancellationToken.None));

        Assert.Equal("403 ErrorAccessDenied", denied.Code);
        Assert.Equal("400", invalid.Code);
    }

    [Fact]
    public async Task Network_and_token_failures_are_transient()
    {
        var down = new RecordingHandler(_ => throw new HttpRequestException("connection refused"));

        var network = await Assert.ThrowsAsync<EmailDeliveryException>(() => Sender(down).SendAsync(Mail, CancellationToken.None));
        var token = await Assert.ThrowsAsync<EmailDeliveryException>(() =>
            new GraphEmailSender(new Clients(down), new FakeCredential(fail: true), Options).SendAsync(Mail, CancellationToken.None));

        Assert.Equal(("network", true), (network.Code, network.Transient));
        Assert.Equal(("token", true), (token.Code, token.Transient));
        Assert.DoesNotContain(down.Requests, r => r.Authorization is null);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("smtp")]
    public void Graph_transport_needs_complete_settings(string? transport)
    {
        var settings = new Dictionary<string, string?>
        {
            [MailTransportRegistration.TransportKey] = transport ?? "graph",
            [GraphMailOptions.TenantIdKey] = "tenant",
            [GraphMailOptions.ClientIdKey] = "client",
            [GraphMailOptions.SenderMailboxKey] = transport is null ? "__SET_VIA_SECRET_STORE__" : "projecthub@contoso.example",
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddMailTransport(configuration));
    }

    [Fact]
    public void Fake_is_the_default_and_graph_needs_no_secret_with_a_managed_identity()
    {
        var empty = new ServiceCollection().AddLogging().AddSingleton(TimeProvider.System).AddMailTransport(new ConfigurationBuilder().Build()).BuildServiceProvider();
        var graph = new ServiceCollection().AddLogging().AddMailTransport(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [MailTransportRegistration.TransportKey] = "graph",
            [GraphMailOptions.TenantIdKey] = "tenant",
            [GraphMailOptions.ClientIdKey] = "client",
            [GraphMailOptions.SenderMailboxKey] = "projecthub@contoso.example",
        }).Build()).BuildServiceProvider();

        Assert.IsType<FakeEmailSender>(empty.GetRequiredService<IEmailSender>());
        Assert.IsType<GraphEmailSender>(graph.GetRequiredService<IEmailSender>());
        Assert.Null(graph.GetRequiredService<GraphMailOptions>().ClientSecret);
    }

    [Theory]
    [InlineData(1, null, 30)]
    [InlineData(2, null, 60)]
    [InlineData(5, null, 480)]
    [InlineData(8, null, 3600)]
    [InlineData(1, 90, 90)]
    [InlineData(1, 7200, 3600)]
    public void Retry_delay_doubles_and_honors_retry_after(int attempts, int? retryAfterSeconds, int expectedSeconds) =>
        Assert.Equal(
            TimeSpan.FromSeconds(expectedSeconds),
            MailOutbox.RetryDelay(attempts, retryAfterSeconds is { } s ? TimeSpan.FromSeconds(s) : null));

    private static GraphEmailSender Sender(RecordingHandler handler) => new(new Clients(handler), new FakeCredential(), Options);

    private static HttpResponseMessage Error(HttpStatusCode status, string code) =>
        new(status) { Content = new StringContent($$$"""{"error":{"code":"{{{code}}}","message":"details"}}""") };

    private sealed record RecordedRequest(HttpMethod Method, string Uri, string? Authorization, string Body);

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!.AbsoluteUri, request.Headers.Authorization?.ToString(), body));
            return respond(request);
        }
    }

    private sealed class Clients(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = GraphEmailSender.BaseAddress };
    }

    private sealed class FakeCredential(bool fail = false) : TokenCredential
    {
        private int issued;

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            fail
                ? throw new AuthenticationFailedException("no token")
                : new AccessToken($"token-{Interlocked.Increment(ref issued)}", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }
}
