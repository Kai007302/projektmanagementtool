using Microsoft.Extensions.DependencyInjection.Extensions;
using ProjectHub.Api.Infrastructure.Events;
using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Modules.Comments;
using ProjectHub.Api.Modules.Identity;
using ProjectHub.Api.Modules.Knowledge;
using ProjectHub.Api.Modules.Projects;
using ProjectHub.Api.Modules.Tasks;

namespace ProjectHub.Api.Modules.Notifications;

public sealed record OutboxMail(Guid Id, string To, string Subject, string Body, DateTimeOffset SentAt);

public static class NotificationEndpoints
{
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(new NotificationOptions(configuration[NotificationOptions.AppUrlKey] ?? "http://localhost:5173"));
        // IEmailSender (fake or Microsoft Graph) is chosen by AddMailTransport.
        services.AddSingleton(MailOutboxOptions.FromConfiguration(configuration));
        services.AddSingleton<MailOutboxSignal>();
        services.AddSingleton<MailOutbox>();
        services.AddHostedService<MailOutboxWorker>();
        services.AddScoped<MailOutboxAdminService>();
        services.TryAddSingleton(new NotificationChannelOptions(WebexAvailable: false));
        services.AddSingleton<NotificationDispatcher>();
        services.AddScoped<NotificationService>();
        services.AddScoped<NotificationHandlers>();
        services.AddScoped<IDomainEventHandler<TaskAssigned>>(sp => sp.GetRequiredService<NotificationHandlers>());
        services.AddScoped<IDomainEventHandler<UsersMentionedInComment>>(sp => sp.GetRequiredService<NotificationHandlers>());
        services.AddScoped<IDomainEventHandler<UsersMentionedInKnowledgeComment>>(sp => sp.GetRequiredService<NotificationHandlers>());
        services.AddScoped<IDomainEventHandler<ProjectMemberAdded>>(sp => sp.GetRequiredService<NotificationHandlers>());
        return services;
    }

    public static RouteGroupBuilder MapNotificationEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/me/notifications", async (UserContext user, NotificationService service, bool? unreadOnly, int? limit, int? offset, CancellationToken ct) =>
        {
            if (!Paging.TryCreate(limit, offset, out var paging, out var error))
            {
                return error!;
            }

            return Results.Ok(paging.ToPage(await service.ListAsync(user, unreadOnly ?? false, paging, ct)));
        });

        api.MapGet("/me/notifications/unread-count", async (UserContext user, NotificationService service, CancellationToken ct) =>
            Results.Ok(await service.UnreadCountAsync(user, ct)));

        api.MapPost("/notifications/{id:guid}/read", async (Guid id, UserContext user, NotificationService service, CancellationToken ct) =>
            ApiResults.NoContent(await service.MarkReadAsync(user, id, ct)));

        api.MapPost("/me/notifications/read-all", async (UserContext user, NotificationService service, CancellationToken ct) =>
        {
            await service.MarkAllReadAsync(user, ct);
            return Results.NoContent();
        });

        api.MapGet("/admin/mail-outbox", async (UserContext user, MailOutboxAdminService service, CancellationToken ct) =>
            ApiResults.Ok(await service.StatusAsync(user, ct)));

        api.MapPost("/admin/mail-outbox/{id:guid}/retry", async (Guid id, UserContext user, MailOutboxAdminService service, CancellationToken ct) =>
            ApiResults.NoContent(await service.RetryAsync(user, id, ct)));

        api.MapGet("/me/notification-preferences", async (UserContext user, NotificationService service, CancellationToken ct) =>
            Results.Ok(await service.PreferencesAsync(user, ct)));

        api.MapPut("/me/notification-preferences", async (UpdatePreferencesRequest request, UserContext user, NotificationService service, CancellationToken ct) =>
            ApiResults.Ok(await service.UpdatePreferencesAsync(user, request, ct)));

        return api;
    }

    /// <summary>Development only: the fake mails sent to the signed-in person, newest first.</summary>
    public static RouteGroupBuilder MapNotificationDevelopmentEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/dev/outbox", (UserContext user, FakeEmailSender sender) =>
            Results.Ok(sender.Outbox
                .Where(m => m.Message.OrganizationId == user.OrganizationId && m.Message.RecipientId == user.UserId)
                .OrderByDescending(m => m.SentAt)
                .Select(m => new OutboxMail(m.Id, m.Message.To, m.Message.Subject, m.Message.Body, m.SentAt))));

        return api;
    }

    public static IEndpointRouteBuilder MapNotificationHub(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHub<NotificationsHub>(NotificationsHub.Path);
        return endpoints;
    }
}
