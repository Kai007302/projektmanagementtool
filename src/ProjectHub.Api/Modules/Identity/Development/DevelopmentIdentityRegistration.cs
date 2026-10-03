using Microsoft.AspNetCore.Authentication;

namespace ProjectHub.Api.Modules.Identity.Development;

public sealed record DevelopmentUserResponse(string ObjectId, string DisplayName, string Organization, string OrganizationRole);

public static class DevelopmentIdentityRegistration
{
    public const string Scheme = "Development";

    public static IServiceCollection AddDevelopmentIdentity(this IServiceCollection services, IHostEnvironment environment, IConfiguration configuration)
    {
        EnsureDevelopment(environment);

        services.Configure<DevelopmentIdentityOptions>(configuration.GetSection(DevelopmentIdentityOptions.SectionName));
        services.AddAuthentication(Scheme)
            .AddScheme<AuthenticationSchemeOptions, DevelopmentAuthenticationHandler>(Scheme, null);

        return services;
    }

    /// <summary>Lists the synthetic users for the frontend's development sign-in.</summary>
    public static IEndpointRouteBuilder MapDevelopmentIdentityEndpoints(this IEndpointRouteBuilder endpoints, IHostEnvironment environment)
    {
        EnsureDevelopment(environment);

        endpoints.MapGet("/api/dev/users", () => DevelopmentSeedData.Users
                .Select(u => new DevelopmentUserResponse(u.ObjectId, u.DisplayName, DevelopmentSeedData.OrganizationOf(u).Name, u.OrganizationRole)))
            .AllowAnonymous();

        return endpoints;
    }

    private static void EnsureDevelopment(IHostEnvironment environment)
    {
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException("The development identity provider must never run outside the Development environment.");
        }
    }
}
