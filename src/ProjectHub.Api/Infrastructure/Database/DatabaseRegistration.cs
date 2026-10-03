using Microsoft.EntityFrameworkCore;

namespace ProjectHub.Api.Infrastructure.Database;

public static class DatabaseRegistration
{
    public static IServiceCollection AddProjectHubDatabase(this IServiceCollection services, ProjectHubSettings settings)
    {
        services.AddDbContext<ProjectHubDbContext>(options => options
            .UseNpgsql(settings.DatabaseConnection)
            .UseSnakeCaseNamingConvention());

        return services;
    }
}
