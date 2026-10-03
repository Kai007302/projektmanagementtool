using Microsoft.AspNetCore.SignalR;
using ProjectHub.Api.Infrastructure.Events;
using ProjectHub.Api.Modules.Projects;

namespace ProjectHub.Api.Modules.Realtime;

public static class RealtimeModule
{
    /// <summary>SignalR with Redis as ephemeral cross-instance fan-out (ADR 0003).</summary>
    public static IServiceCollection AddRealtimeModule(this IServiceCollection services, ProjectHubSettings settings)
    {
        services.AddSignalR().AddStackExchangeRedis(settings.RedisConnection, options =>
            options.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("projecthub"));
        services.AddScoped<IDomainEventHandler<ProjectContentChanged>, ProjectContentChangedBroadcaster>();
        return services;
    }

    public static IEndpointRouteBuilder MapRealtimeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHub<ProjectEventsHub>(ProjectEventsHub.Path);
        return endpoints;
    }
}

/// <summary>
/// Forwards committed changes to the project's realtime group. Best effort: the change is already
/// saved, so a failing backplane is logged and never fails the request.
/// </summary>
internal sealed class ProjectContentChangedBroadcaster(
    IHubContext<ProjectEventsHub, IProjectEventsClient> hub,
    ILogger<ProjectContentChangedBroadcaster> logger)
    : IDomainEventHandler<ProjectContentChanged>
{
    public async Task HandleAsync(ProjectContentChanged domainEvent, CancellationToken ct)
    {
        try
        {
            await hub.Clients.Group(ProjectEventsHub.GroupOf(domainEvent.ProjectId))
                .ProjectChanged(new ProjectChangedMessage(domainEvent.ProjectId, domainEvent.Area));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Realtime notification for project {ProjectId} failed", domainEvent.ProjectId);
        }
    }
}
