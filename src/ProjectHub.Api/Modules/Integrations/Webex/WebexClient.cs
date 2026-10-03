using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ProjectHub.Api.Modules.Notifications;

namespace ProjectHub.Api.Modules.Integrations.Webex;

public sealed record WebexSpace(string RoomId, string Title);

/// <summary>
/// What ProjectHub does in Webex, always as its bot (ADR 0011). Failures throw <see cref="MessageDeliveryException"/>
/// with a code that never contains addresses or content.
/// </summary>
public interface IWebexClient
{
    Task SendDirectMessageAsync(string personEmail, string markdown, CancellationToken ct);

    Task<WebexSpace> CreateSpaceAsync(string title, CancellationToken ct);

    /// <summary>Invites a person by mail address. Someone already in the space is not an error.</summary>
    Task AddMemberAsync(string roomId, string personEmail, CancellationToken ct);

    Task PostMessageAsync(string roomId, string markdown, CancellationToken ct);

    /// <summary>The bot's own person id, to recognize webhooks about the bot itself.</summary>
    Task<string> BotPersonIdAsync(CancellationToken ct);

    /// <summary>Registers a webhook and returns its Webex id.</summary>
    Task<string> CreateWebhookAsync(string name, Uri targetUrl, string resource, string @event, string secret, CancellationToken ct);
}

public static class WebexRooms
{
    /// <summary>
    /// Webex room ids are base64 of <c>ciscospark://{region}/ROOM/{uuid}</c>; the Webex app opens
    /// <c>webexteams://im?space={uuid}</c>. Null when the id has another shape.
    /// </summary>
    public static string? AppLink(string roomId)
    {
        try
        {
            var padded = roomId.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            var marker = decoded.IndexOf("/ROOM/", StringComparison.Ordinal);
            return decoded.StartsWith("ciscospark://", StringComparison.Ordinal) && marker > 0
                && Guid.TryParse(decoded[(marker + "/ROOM/".Length)..], out var space)
                ? $"webexteams://im?space={space}"
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>A room id in the Webex format, for the fake.</summary>
    public static string NewRoomId() =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"ciscospark://us/ROOM/{Guid.NewGuid()}")).TrimEnd('=');
}

/// <summary>Webex REST API as the ProjectHub bot (<c>https://webexapis.com/v1/</c>).</summary>
public sealed class WebexBotClient(IHttpClientFactory httpClients, WebexOptions options) : IWebexClient
{
    public const string HttpClientName = "webex";
    public static readonly Uri BaseAddress = new("https://webexapis.com/v1/");
    private string? botPersonId;

    public Task SendDirectMessageAsync(string personEmail, string markdown, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, "messages", new { toPersonEmail = personEmail, markdown }, ct);

    public async Task<WebexSpace> CreateSpaceAsync(string title, CancellationToken ct)
    {
        using var json = await SendAsync(HttpMethod.Post, "rooms", new { title }, ct);
        return new WebexSpace(json.RootElement.GetProperty("id").GetString()!, title);
    }

    public async Task AddMemberAsync(string roomId, string personEmail, CancellationToken ct)
    {
        try
        {
            (await SendAsync(HttpMethod.Post, "memberships", new { roomId, personEmail }, ct)).Dispose();
        }
        catch (MessageDeliveryException ex) when (ex.Code == "409")
        {
            // Already a member.
        }
    }

    public Task PostMessageAsync(string roomId, string markdown, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, "messages", new { roomId, markdown }, ct);

    public async Task<string> BotPersonIdAsync(CancellationToken ct)
    {
        if (botPersonId is { } known)
        {
            return known;
        }

        using var json = await SendAsync(HttpMethod.Get, "people/me", null, ct);
        return botPersonId = json.RootElement.GetProperty("id").GetString()!;
    }

    public async Task<string> CreateWebhookAsync(string name, Uri targetUrl, string resource, string @event, string secret, CancellationToken ct)
    {
        using var json = await SendAsync(HttpMethod.Post, "webhooks", new { name, targetUrl = targetUrl.AbsoluteUri, resource, @event, secret }, ct);
        return json.RootElement.GetProperty("id").GetString()!;
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.BotToken);

        HttpResponseMessage response;
        try
        {
            response = await httpClients.CreateClient(HttpClientName).SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new MessageDeliveryException("network", transient: true, inner: ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new MessageDeliveryException("timeout", transient: true, inner: ex);
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                // Webex error messages can name people; only the status is kept.
                var retryAfter = response.Headers.RetryAfter?.Delta;
                throw new MessageDeliveryException($"{status}", transient: status is 401 or 408 or 429 or >= 500, retryAfter);
            }

            var content = await response.Content.ReadAsStringAsync(ct);
            return JsonDocument.Parse(content.Length == 0 ? "{}" : content);
        }
    }
}

public sealed record FakeWebexMessage(Guid Id, string? ToPersonEmail, string? RoomId, string Markdown, DateTimeOffset SentAt);

public sealed record FakeWebexSpace(string RoomId, string Title, IReadOnlyList<string> Members);

/// <summary>Sends nothing: keeps messages and spaces in memory for development and tests (<c>GET /api/v1/dev/webex</c>).</summary>
public sealed class FakeWebexClient(TimeProvider clock) : IWebexClient
{
    public const string BotId = "fake-projecthub-bot";
    public const int Capacity = 500;

    private readonly ConcurrentQueue<FakeWebexMessage> messages = new();
    private readonly ConcurrentDictionary<string, (string Title, ConcurrentDictionary<string, byte> Members)> spaces = new();
    private readonly ConcurrentQueue<(string Id, Uri Target, string Resource, string Event)> webhooks = new();

    public IReadOnlyList<FakeWebexMessage> Messages => messages.ToArray();

    public IReadOnlyList<FakeWebexSpace> Spaces =>
        spaces.Select(s => new FakeWebexSpace(s.Key, s.Value.Title, s.Value.Members.Keys.Order().ToList())).ToList();

    public IReadOnlyList<(string Id, Uri Target, string Resource, string Event)> Webhooks => webhooks.ToArray();

    public Task SendDirectMessageAsync(string personEmail, string markdown, CancellationToken ct)
    {
        Add(new FakeWebexMessage(Guid.CreateVersion7(), personEmail, null, markdown, clock.GetUtcNow()));
        return Task.CompletedTask;
    }

    public Task<WebexSpace> CreateSpaceAsync(string title, CancellationToken ct)
    {
        var roomId = WebexRooms.NewRoomId();
        spaces[roomId] = (title, new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase));
        return Task.FromResult(new WebexSpace(roomId, title));
    }

    public Task AddMemberAsync(string roomId, string personEmail, CancellationToken ct)
    {
        if (!spaces.TryGetValue(roomId, out var space))
        {
            throw new MessageDeliveryException("404", transient: false);
        }

        space.Members.TryAdd(personEmail, 0);
        return Task.CompletedTask;
    }

    public Task PostMessageAsync(string roomId, string markdown, CancellationToken ct)
    {
        Add(new FakeWebexMessage(Guid.CreateVersion7(), null, roomId, markdown, clock.GetUtcNow()));
        return Task.CompletedTask;
    }

    public Task<string> BotPersonIdAsync(CancellationToken ct) => Task.FromResult(BotId);

    public Task<string> CreateWebhookAsync(string name, Uri targetUrl, string resource, string @event, string secret, CancellationToken ct)
    {
        var id = $"fake-webhook-{Guid.NewGuid():N}";
        webhooks.Enqueue((id, targetUrl, resource, @event));
        return Task.FromResult(id);
    }

    private void Add(FakeWebexMessage message)
    {
        messages.Enqueue(message);
        while (messages.Count > Capacity && messages.TryDequeue(out _))
        {
        }
    }
}

/// <summary>Webex switched off: the API reports it as unavailable and every call fails permanently.</summary>
public sealed class DisabledWebexClient : IWebexClient
{
    private static MessageDeliveryException Off() => new("webex off", transient: false);

    public Task SendDirectMessageAsync(string personEmail, string markdown, CancellationToken ct) => throw Off();

    public Task<WebexSpace> CreateSpaceAsync(string title, CancellationToken ct) => throw Off();

    public Task AddMemberAsync(string roomId, string personEmail, CancellationToken ct) => throw Off();

    public Task PostMessageAsync(string roomId, string markdown, CancellationToken ct) => throw Off();

    public Task<string> BotPersonIdAsync(CancellationToken ct) => throw Off();

    public Task<string> CreateWebhookAsync(string name, Uri targetUrl, string resource, string @event, string secret, CancellationToken ct) => throw Off();
}
