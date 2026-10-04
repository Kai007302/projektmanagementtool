using Microsoft.AspNetCore.Authorization;
using ProjectHub.Api.Modules.Identity.Authorization;
using ProjectHub.Api.Modules.Identity.Development;

namespace ProjectHub.Api.Modules.Identity;

public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IHostEnvironment environment, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
        services.AddScoped(provider => UserContextMiddleware.FromHttpContext(provider.GetRequiredService<IHttpContextAccessor>()));
        services.AddScoped<IProjectHubAuthorization, ProjectHubAuthorization>();
        services.AddSingleton(UserProvisioningOptions.FromConfiguration(configuration));
        services.AddScoped<UserProvisioning>();

        if (environment.IsDevelopment())
        {
            services.AddDevelopmentIdentity(environment, configuration);
        }
        else
        {
            services.AddEntraIdAuthentication(configuration);
        }

        // Secure by default: every endpoint requires an authenticated user unless it opts out explicitly.
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        return services;
    }

    public const string ApiV1Prefix = "/api/v1";

    /// <summary>
    /// Resolves the <see cref="UserContext"/> for /api/v1. Must run after UseAuthorization. Anonymous endpoints
    /// (webhooks, which authenticate by signature) have no user.
    /// </summary>
    public static IApplicationBuilder UseUserContext(this IApplicationBuilder app) =>
        app.UseWhen(
            context => context.Request.Path.StartsWithSegments(ApiV1Prefix)
                       && context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is null,
            branch => branch.UseMiddleware<UserContextMiddleware>());

    public static RouteGroupBuilder MapApiV1(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGroup(ApiV1Prefix);
}
