using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Infrastructure.Events;
using ProjectHub.Api.Infrastructure.Outcomes;
using ProjectHub.Api.Modules.Audit;
using ProjectHub.Api.Modules.Identity;
using ProjectHub.Api.Modules.Notifications;
using ProjectHub.Api.Modules.Projects;

namespace ProjectHub.Api.Modules.Integrations.Webex;

public enum WebhookOutcome
{
    Processed,
    Duplicate,
    Unauthorized,
    Invalid,
}

/// <summary>
/// Receives Webex webhooks (ADR 0011): validated by signature, deduplicated by an event key and processed
/// idempotently in the same transaction that records the event.
/// </summary>
public sealed class WebexWebhookService(
    ProjectHubDbContext db,
    IWebexClient webex,
    WebexOptions options,
    IDomainEventPublisher events,
    TimeProvider clock,
    ILogger<WebexWebhookService> logger)
{
    public const int MaxBodyBytes = 64 * 1024;
    public const string SignatureHeader = "X-Spark-Signature";

    public async Task<WebhookOutcome> HandleAsync(byte[] body, string? signature, CancellationToken ct)
    {
        var correlationId = Activity.Current?.TraceId.ToString();
        if (options.WebhookSecret is not { } secret || !SignatureValid(body, signature, secret))
        {
            logger.LogWarning("Webex webhook with missing or invalid signature rejected (correlation {CorrelationId})", correlationId);
            return WebhookOutcome.Unauthorized;
        }

        if (Parse(body) is not { } payload)
        {
            return WebhookOutcome.Invalid;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = clock.GetUtcNow();
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             insert into webhook_event (provider, event_key, resource, event, correlation_id, received_at)
             values ('webex', {payload.Key}, {payload.Resource}, {payload.Event}, {correlationId}, {now})
             on conflict (provider, event_key) do nothing
             """,
            ct);
        if (inserted == 0)
        {
            logger.LogInformation("Webex webhook {EventKey} already handled (correlation {CorrelationId})", payload.Key, correlationId);
            return WebhookOutcome.Duplicate;
        }

        if (payload.WebhookId is { } webhookId)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"update webhook_subscription set last_event_at = {now}, updated_at = {now} where external_subscription_id = {webhookId}", ct);
        }

        var affected = new List<Guid>();
        if (payload is { Resource: "memberships", Event: "deleted", RoomId: { } roomId, PersonId: { } personId }
            && personId == await webex.BotPersonIdAsync(ct))
        {
            // Setting a state, never counting: a second delivery would change nothing anyway.
            affected = await db.Set<ProjectWebexLink>()
                .Where(l => l.RoomId == roomId && l.Status == WebexLinkStatus.Active)
                .Select(l => l.ProjectId)
                .ToListAsync(ct);
            await db.Set<ProjectWebexLink>()
                .Where(l => l.RoomId == roomId && l.Status == WebexLinkStatus.Active)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.Status, WebexLinkStatus.Disconnected), ct);
        }

        await transaction.CommitAsync(ct);
        logger.LogInformation(
            "Webex webhook {Resource}/{Event} handled, {Links} project links changed (correlation {CorrelationId})",
            payload.Resource, payload.Event, affected.Count, correlationId);
        foreach (var projectId in affected.Distinct())
        {
            await events.PublishAsync(new ProjectContentChanged(projectId, ProjectContentChanged.Webex), ct);
        }

        return WebhookOutcome.Processed;
    }

    /// <summary>HMAC-SHA1 of the raw body with the webhook secret, hex encoded, compared in constant time.</summary>
    public static bool SignatureValid(byte[] body, string? signature, string secret)
    {
        if (string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        var expected = Convert.ToHexStringLower(HMACSHA1.HashData(Encoding.UTF8.GetBytes(secret), body));
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(signature.Trim().ToLowerInvariant()));
    }

    public sealed record Payload(string Key, string Resource, string Event, string? WebhookId, string? RoomId, string? PersonId);

    /// <summary>Webex sends no event id; resource, event and the id of the changed object identify a delivery.</summary>
    public static Payload? Parse(byte[] body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            string? Text(JsonElement element, string name) =>
                element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;

            var data = root.TryGetProperty("data", out var d) ? d : default;
            if (Text(root, "resource") is not { Length: > 0 and <= 50 } resource
                || Text(root, "event") is not { Length: > 0 and <= 50 } @event
                || Text(data, "id") is not { Length: > 0 and <= 200 } dataId)
            {
                return null;
            }

            return new Payload($"{resource}:{@event}:{dataId}", resource, @event, Text(root, "id"), Text(data, "roomId"), Text(data, "personId"));
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed record WebexWebhookResponse(Guid Id, string ExternalId, string Resource, bool Active, DateTimeOffset? LastEventAt);

public sealed record WebexStatusResponse(string Transport, bool WebhookSecretConfigured, Uri? WebhookTarget, IReadOnlyList<WebexWebhookResponse> Webhooks);

/// <summary>Webex setup for organization admins: state and webhook registration.</summary>
public sealed class WebexAdminService(ProjectHubDbContext db, IWebexClient webex, WebexOptions options, IAuditLog audit, TimeProvider clock)
{
    public const string WebhookPath = "api/v1/integrations/webex/webhook";

    public Uri? WebhookTarget => options.PublicApiUrl is { } baseUrl ? new Uri(new Uri(baseUrl.AbsoluteUri.TrimEnd('/') + "/"), WebhookPath) : null;

    public async Task<ServiceResult<WebexStatusResponse>> StatusAsync(UserContext user, CancellationToken ct)
    {
        if (!user.IsOrganizationAdmin)
        {
            return ServiceFailure.Forbidden("Only organization admins can see the Webex setup.");
        }

        var webhooks = await db.Database.SqlQuery<WebexWebhookResponse>(
            $"""
             select s.id, s.external_subscription_id as external_id, s.resource, s.active, s.last_event_at
             from webhook_subscription s join integration i on i.id = s.integration_id
             where i.organization_id = {user.OrganizationId} and i.provider = 'webex'
             order by s.created_at
             """).ToListAsync(ct);
        return new WebexStatusResponse(options.Transport, options.WebhookSecret is not null, WebhookTarget, webhooks);
    }

    /// <summary>
    /// Registers the webhook that tells ProjectHub when the bot was removed from a space. Needs the webhook secret
    /// and the public address of the API.
    /// </summary>
    public async Task<ServiceResult<WebexWebhookResponse>> RegisterWebhookAsync(UserContext user, CancellationToken ct)
    {
        if (!user.IsOrganizationAdmin)
        {
            return ServiceFailure.Forbidden("Only organization admins can register Webex webhooks.");
        }

        if (!options.Available || options.WebhookSecret is not { } secret || WebhookTarget is not { } target)
        {
            return ServiceFailure.Conflict($"Webex, {WebexOptions.WebhookSecretKey} and {WebexOptions.PublicApiUrlKey} must be set up first.");
        }

        string externalId;
        try
        {
            externalId = await webex.CreateWebhookAsync("ProjectHub bot memberships", target, "memberships", "deleted", secret, ct);
        }
        catch (MessageDeliveryException ex)
        {
            return ServiceFailure.Unavailable($"Webex did not register the webhook ({ex.Code}).");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = clock.GetUtcNow();
        var integrationId = (await db.Database.SqlQuery<Guid>(
            $"""
             insert into integration (organization_id, provider, status, created_at, updated_at)
             values ({user.OrganizationId}, 'webex', 'active', {now}, {now})
             on conflict (organization_id, provider) do update set updated_at = excluded.updated_at
             returning id as "Value"
             """).ToListAsync(ct)).Single();
        var id = Guid.CreateVersion7();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             insert into webhook_subscription (id, organization_id, integration_id, external_subscription_id, resource, active, created_at, updated_at)
             values ({id}, {user.OrganizationId}, {integrationId}, {externalId}, 'memberships', true, {now}, {now})
             """,
            ct);
        audit.Record(user, AuditActions.WebexWebhookRegistered, "webhook_subscription", id, new { Resource = "memberships", Event = "deleted" });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new WebexWebhookResponse(id, externalId, "memberships", true, null);
    }
}

/// <summary>Forgets handled webhook deliveries after 30 days; Webex retries far sooner than that.</summary>
internal sealed class WebhookEventCleanupService(IServiceScopeFactory scopes, TimeProvider clock, ILogger<WebhookEventCleanupService> logger) : BackgroundService
{
    public static readonly TimeSpan Keep = TimeSpan.FromDays(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ProjectHubDbContext>();
                var before = clock.GetUtcNow() - Keep;
                await db.Database.ExecuteSqlInterpolatedAsync($"delete from webhook_event where received_at < {before}", stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Cleaning up webhook events failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
