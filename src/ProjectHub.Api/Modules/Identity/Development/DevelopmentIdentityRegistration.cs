using Microsoft.AspNetCore.Authentication;

namespace ProjectHub.Api.Modules.Identity.Development;

public static class DevelopmentIdentityRegistration
{
    public const string Scheme = "Development";

    public static IServiceCollection AddDevelopmentIdentity(this IServiceCollection services, IHostEnvironment environment, IConfiguration configuration)
    {
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException("The development identity provider must never run outside the Development environment.");
        }

        services.Configure<DevelopmentIdentityOptions>(configuration.GetSection(DevelopmentIdentityOptions.SectionName));
        services.AddAuthentication(Scheme)
            .AddScheme<AuthenticationSchemeOptions, DevelopmentAuthenticationHandler>(Scheme, null);

        return services;
    }
}
