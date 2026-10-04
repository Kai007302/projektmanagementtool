using Microsoft.Extensions.DependencyInjection.Extensions;
using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Infrastructure.Observability;
using ProjectHub.Api.Modules.Identity;
using ProjectHub.Api.Modules.Notifications;

namespace ProjectHub.Api.Modules.Integrations.Webex;

public sealed record FakeWebexMessageResponse(Guid Id, string? To, string? RoomId, string Markdown, DateTimeOffset SentAt);

public sealed record FakeWebexSpaceResponse(string RoomId, string Title, IReadOnlyList<string> Members, IReadOnlyList<string> Messages);

public sealed record FakeWebexResponse(IReadOnlyList<FakeWebexMessageResponse> DirectMessages, IReadOnlyList<FakeWebexSpaceResponse> Spaces);

public static class WebexEndpoints
{
    public static IServiceCollection AddWebexModule(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var options = WebexOptions.FromConfiguration(configuration, environment);
        services.AddSingleton(options);
        services.Replace(ServiceDescriptor.Singleton(new NotificationChannelOptions(options.Available)));
        services.AddSingleton<FakeWebexClient>();
        switch (options.Transport)
        {
            case WebexOptions.Bot:
                services.AddHttpClient(WebexBotClient.HttpClientName, client =>
                {
                    client.BaseAddress = WebexBotClient.BaseAddress;
                    client.Timeout = TimeSpan.FromSeconds(30);
                });
                services.AddSingleton<IWebexClient, WebexBotClient>();
                break;
            case WebexOptions.Fake:
                services.AddSingleton<IWebexClient>(sp => sp.GetRequiredService<FakeWebexClient>());
                break;
            default:
                services.AddSingleton<IWebexClient, DisabledWebexClient>();
                break;
        }

        services.AddSingleton<IWebexMessageSender, WebexNotificationSender>();
        services.AddScoped<WebexLinkService>();
        services.AddScoped<WebexWebhookService>();
        services.AddScoped<WebexAdminService>();
        services.AddHostedService<WebhookEventCleanupService>();
        return services;
    }

    public static RouteGroupBuilder MapWebexEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/projects/{projectId:guid}/webex", async (Guid projectId, UserContext user, WebexLinkService service, CancellationToken ct) =>
            ApiResults.Ok(await service.ListAsync(user, projectId, ct)));

        api.MapPost("/projects/{projectId:guid}/webex/links", async (
                Guid projectId, CreateWebexLinkRequest request, UserContext user, WebexLinkService service, CancellationToken ct) =>
            ApiResults.From(await service.AddAsync(user, projectId, request, ct), link => Results.Created($"/api/v1/webex-links/{link.Id}", link)));

        api.MapPost("/projects/{projectId:guid}/webex/space", async (Guid projectId, UserContext user, WebexLinkService service, CancellationToken ct) =>
            ApiResults.From(await service.CreateSpaceAsync(user, projectId, ct), link => Results.Created($"/api/v1/webex-links/{link.Id}", link)));

        api.MapDelete("/webex-links/{id:guid}", async (Guid id, UserContext user, WebexLinkService service, CancellationToken ct) =>
            ApiResults.NoContent(await service.RemoveAsync(user, id, ct)));

        api.MapGet("/admin/webex", async (UserContext user, WebexAdminService service, CancellationToken ct) =>
            ApiResults.Ok(await service.StatusAsync(user, ct)));

        api.MapPost("/admin/webex/webhook", async (UserContext user, WebexAdminService service, CancellationToken ct) =>
            ApiResults.From(await service.RegisterWebhookAsync(user, ct), webhook => Results.Created($"/api/v1/admin/webex", webhook)));

        return api;
    }

    /// <summary>
    /// Called by Webex, not by people: no user, but every request must carry a valid signature (ADR 0011).
    /// Answers 200 for handled and repeated deliveries, so Webex stops retrying.
    /// </summary>
    public static IEndpointRouteBuilder MapWebexWebhook(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/" + WebexAdminService.WebhookPath, async (HttpRequest request, WebexWebhookService service, CancellationToken ct) =>
            {
                if (request.ContentLength > WebexWebhookService.MaxBodyBytes)
                {
                    return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                }

                using var buffer = new MemoryStream();
                await request.Body.CopyToAsync(buffer, ct);
                if (buffer.Length > WebexWebhookService.MaxBodyBytes)
                {
                    return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                }

                var outcome = await service.HandleAsync(buffer.ToArray(), request.Headers[WebexWebhookService.SignatureHeader], ct);
                ProjectHubMetrics.WebhookReceived("webex", outcome.ToString().ToLowerInvariant());
                return outcome switch
                {
                    WebhookOutcome.Unauthorized => Results.Unauthorized(),
                    WebhookOutcome.Invalid => Results.BadRequest(),
                    _ => Results.Ok(),
                };
            })
            .AllowAnonymous()
            .RequireRateLimiting(HttpHardening.WebhookPolicy);
        return endpoints;
    }

    /// <summary>Development only: what the fake bot sent to the signed-in person and the spaces they are in.</summary>
    public static RouteGroupBuilder MapWebexDevelopmentEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/dev/webex", (UserContext user, FakeWebexClient fake) =>
        {
            var messages = fake.Messages;
            var direct = messages
                .Where(m => string.Equals(m.ToPersonEmail, user.Email, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(m => m.SentAt)
                .Select(m => new FakeWebexMessageResponse(m.Id, m.ToPersonEmail, m.RoomId, m.Markdown, m.SentAt))
                .ToList();
            var spaces = fake.Spaces
                .Where(s => s.Members.Contains(user.Email, StringComparer.OrdinalIgnoreCase))
                .Select(s => new FakeWebexSpaceResponse(
                    s.RoomId, s.Title, s.Members, messages.Where(m => m.RoomId == s.RoomId).OrderBy(m => m.SentAt).Select(m => m.Markdown).ToList()))
                .ToList();
            return Results.Ok(new FakeWebexResponse(direct, spaces));
        });
        return api;
    }
}
