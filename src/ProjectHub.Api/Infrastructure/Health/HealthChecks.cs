using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace ProjectHub.Api.Infrastructure.Health;

public static class HealthChecks
{
    private const string ReadyTag = "ready";

    public static IServiceCollection AddProjectHubHealthChecks(this IServiceCollection services, ProjectHubSettings settings)
    {
        services.AddHealthChecks()
            .AddNpgSql(settings.DatabaseConnection, name: "postgresql", tags: [ReadyTag], timeout: TimeSpan.FromSeconds(3))
            .AddRedis(settings.RedisConnection, name: "redis", tags: [ReadyTag], timeout: TimeSpan.FromSeconds(3));

        return services;
    }

    /// <summary>
    /// /health/live: the process is running (no dependency checks).
    /// /health/ready: PostgreSQL and Redis are reachable.
    /// </summary>
    public static IEndpointRouteBuilder MapProjectHubHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
            .AllowAnonymous();
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(ReadyTag) })
            .AllowAnonymous();

        return endpoints;
    }
}
