using ProjectHub.Api.Modules.Notifications;

namespace ProjectHub.Api.Modules.Integrations.Webex;

/// <summary>How ProjectHub reaches Webex (ADR 0011). Tokens and secrets only come from the secret store.</summary>
public sealed record WebexOptions(string Transport, string? BotToken, string? WebhookSecret, Uri? PublicApiUrl)
{
    public const string TransportKey = "PROJECTHUB_WEBEX_TRANSPORT";
    public const string BotTokenKey = "WEBEX_BOT_TOKEN";
    public const string WebhookSecretKey = "WEBEX_WEBHOOK_SECRET";

    /// <summary>Address Webex calls webhooks on, e.g. https://projecthub.firma.de (the API part of it).</summary>
    public const string PublicApiUrlKey = "PROJECTHUB_PUBLIC_API_URL";

    public const string Off = "off";
    public const string Fake = "fake";
    public const string Bot = "bot";

    public bool Available => Transport != Off;

    public static WebexOptions FromConfiguration(IConfiguration configuration, IHostEnvironment environment)
    {
        static string? Secret(string? value) =>
            string.IsNullOrWhiteSpace(value) || value.StartsWith("__", StringComparison.Ordinal) ? null : value.Trim();

        var transport = (configuration[TransportKey] ?? (environment.IsDevelopment() ? Fake : Off)).Trim().ToLowerInvariant();
        if (transport is not (Off or Fake or Bot))
        {
            throw new InvalidOperationException($"{TransportKey} must be '{Off}', '{Fake}' or '{Bot}'.");
        }

        var token = Secret(configuration[BotTokenKey]);
        if (transport == Bot && token is null)
        {
            throw new InvalidOperationException($"{BotTokenKey} is required when {TransportKey} is '{Bot}'.");
        }

        Uri? publicUrl = null;
        if (configuration[PublicApiUrlKey] is { Length: > 0 } url && !Uri.TryCreate(url, UriKind.Absolute, out publicUrl))
        {
            throw new InvalidOperationException($"{PublicApiUrlKey} must be an absolute URL.");
        }

        return new WebexOptions(transport, token, Secret(configuration[WebhookSecretKey]), publicUrl);
    }
}

/// <summary>Delivers Webex notifications from the outbox as direct messages of the bot.</summary>
internal sealed class WebexNotificationSender(IWebexClient client) : IWebexMessageSender
{
    public Task SendAsync(EmailMessage message, CancellationToken ct) => client.SendDirectMessageAsync(message.To, message.Body, ct);
}
