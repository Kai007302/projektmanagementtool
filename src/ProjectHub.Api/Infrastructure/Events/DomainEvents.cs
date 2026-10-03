namespace ProjectHub.Api.Infrastructure.Events;

/// <summary>Something that happened in a module and that other modules may react to.</summary>
public interface IDomainEvent;

public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken ct);
}

public interface IDomainEventPublisher
{
    /// <summary>
    /// Dispatches to in-process handlers after the originating transaction committed.
    /// Durable delivery (Azure Service Bus, ADR 0003) replaces this once integrations need it.
    /// </summary>
    Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken ct)
        where TEvent : IDomainEvent;
}

internal sealed class InProcessDomainEventPublisher(IServiceProvider services, ILogger<InProcessDomainEventPublisher> logger)
    : IDomainEventPublisher
{
    public async Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken ct)
        where TEvent : IDomainEvent
    {
        var handlers = services.GetServices<IDomainEventHandler<TEvent>>().ToList();
        logger.LogDebug("Publishing {Event} to {HandlerCount} handlers", typeof(TEvent).Name, handlers.Count);
        foreach (var handler in handlers)
        {
            await handler.HandleAsync(domainEvent, ct);
        }
    }
}

public static class DomainEventRegistration
{
    public static IServiceCollection AddDomainEvents(this IServiceCollection services) =>
        services.AddScoped<IDomainEventPublisher, InProcessDomainEventPublisher>();
}
