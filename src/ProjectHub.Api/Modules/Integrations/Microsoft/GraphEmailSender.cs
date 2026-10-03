using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Core;
using Azure.Identity;
using ProjectHub.Api.Modules.Notifications;

namespace ProjectHub.Api.Modules.Integrations.Microsoft;

/// <summary>Sender mailbox and app registration for mails through Microsoft Graph (ADR 0010).</summary>
public sealed record GraphMailOptions(string TenantId, string ClientId, string? ClientSecret, string SenderMailbox)
{
    public const string TenantIdKey = "MICROSOFT_GRAPH_TENANT_ID";
    public const string ClientIdKey = "MICROSOFT_GRAPH_CLIENT_ID";

    /// <summary>Only from the secret store. Without it the API uses its managed identity.</summary>
    public const string ClientSecretKey = "MICROSOFT_GRAPH_CLIENT_SECRET";

    public const string SenderMailboxKey = "MICROSOFT_GRAPH_SENDER_MAILBOX";
}

/// <summary>
/// Sends mails app-only from the functional mailbox: <c>POST /users/{mailbox}/sendMail</c>. The app may only send
/// as that mailbox (Exchange Online RBAC for Applications); no copy is kept in Sent Items.
/// </summary>
public sealed partial class GraphEmailSender(IHttpClientFactory httpClients, TokenCredential credential, GraphMailOptions options) : IEmailSender
{
    public const string HttpClientName = "microsoft-graph";
    public static readonly Uri BaseAddress = new("https://graph.microsoft.com/v1.0/");
    private static readonly string[] Scopes = ["https://graph.microsoft.com/.default"];

    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        AccessToken token;
        try
        {
            token = await credential.GetTokenAsync(new TokenRequestContext(Scopes), ct);
        }
        catch (AuthenticationFailedException ex)
        {
            throw new MessageDeliveryException("token", transient: true, inner: ex);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"users/{Uri.EscapeDataString(options.SenderMailbox)}/sendMail")
        {
            Content = JsonContent.Create(new
            {
                message = new
                {
                    subject = message.Subject,
                    body = new { contentType = "Text", content = message.Body },
                    toRecipients = new[] { new { emailAddress = new { address = message.To } } },
                },
                saveToSentItems = false,
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

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
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            var status = (int)response.StatusCode;
            var code = await ErrorCodeAsync(response, ct);

            // 401 usually means an expired or rotated credential, which an admin fixes without losing mails.
            var transient = status is 401 or 408 or 429 or >= 500;
            throw new MessageDeliveryException(code is null ? $"{status}" : $"{status} {code}", transient, RetryAfter(response));
        }
    }

    /// <summary>The Graph error code (for example "ErrorAccessDenied"), never the message, which can name mailboxes.</summary>
    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (json.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("code", out var code)
                && code.GetString() is { } value
                && SafeCode().IsMatch(value))
            {
                return value;
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response) =>
        response.Headers.RetryAfter switch
        {
            { Delta: { } delta } => delta,
            { Date: { } date } => date - DateTimeOffset.UtcNow,
            _ => null,
        };

    [GeneratedRegex("^[A-Za-z0-9_.-]{1,64}$")]
    private static partial Regex SafeCode();
}

public static class MailTransportRegistration
{
    public const string TransportKey = "PROJECTHUB_MAIL_TRANSPORT";
    public const string Fake = "fake";
    public const string Graph = "graph";

    /// <summary>
    /// Chooses how outbox mails leave ProjectHub: <c>fake</c> (default, nothing leaves) or <c>graph</c>.
    /// With <c>graph</c>, missing settings stop the API at startup instead of failing every mail later.
    /// </summary>
    public static IServiceCollection AddMailTransport(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<FakeEmailSender>();
        var transport = (configuration[TransportKey] ?? Fake).Trim().ToLowerInvariant();
        switch (transport)
        {
            case Fake:
                services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<FakeEmailSender>());
                return services;
            case Graph:
                var options = GraphOptionsFrom(configuration);
                TokenCredential credential = options.ClientSecret is { } secret
                    ? new ClientSecretCredential(options.TenantId, options.ClientId, secret)
                    : new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(options.ClientId));
                services.AddHttpClient(GraphEmailSender.HttpClientName, client =>
                {
                    client.BaseAddress = GraphEmailSender.BaseAddress;
                    client.Timeout = TimeSpan.FromSeconds(30);
                });
                services.AddSingleton(options);
                services.AddSingleton<IEmailSender>(sp => new GraphEmailSender(sp.GetRequiredService<IHttpClientFactory>(), credential, options));
                return services;
            default:
                throw new InvalidOperationException($"{TransportKey} must be '{Fake}' or '{Graph}'.");
        }
    }

    private static GraphMailOptions GraphOptionsFrom(IConfiguration configuration)
    {
        string Required(string key) =>
            configuration[key] is { Length: > 0 } value && !value.StartsWith("__", StringComparison.Ordinal)
                ? value.Trim()
                : throw new InvalidOperationException($"{key} is required when {TransportKey} is '{Graph}'.");

        var mailbox = Required(GraphMailOptions.SenderMailboxKey);
        if (!mailbox.Contains('@', StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{GraphMailOptions.SenderMailboxKey} must be a mailbox address.");
        }

        var secret = configuration[GraphMailOptions.ClientSecretKey];
        return new GraphMailOptions(
            Required(GraphMailOptions.TenantIdKey),
            Required(GraphMailOptions.ClientIdKey),
            string.IsNullOrWhiteSpace(secret) || secret.StartsWith("__", StringComparison.Ordinal) ? null : secret,
            mailbox);
    }
}
