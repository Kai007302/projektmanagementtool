using Microsoft.AspNetCore.Authorization;
using ProjectHub.Api.Modules.Identity.Development;

namespace ProjectHub.Api.Modules.Identity;

public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IHostEnvironment environment, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

        if (environment.IsDevelopment())
        {
            services.AddDevelopmentIdentity(environment, configuration);
        }
        else
        {
            // Entra ID / OIDC is wired up in phase 1. Until then nothing outside Development can authenticate.
            services.AddAuthentication();
        }

        // Secure by default: every endpoint requires an authenticated user unless it opts out explicitly.
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        return services;
    }
}
