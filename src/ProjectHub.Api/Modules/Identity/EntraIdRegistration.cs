using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace ProjectHub.Api.Modules.Identity;

/// <summary>
/// Validates Entra ID access tokens issued for the ProjectHub app registration.
/// Tenant and client id come from ENTRA_TENANT_ID / ENTRA_CLIENT_ID; the app registration
/// itself is a human review gate (ai/AI_WORKFLOW.md).
/// </summary>
public static class EntraIdRegistration
{
    public const string TenantIdKey = "ENTRA_TENANT_ID";
    public const string ClientIdKey = "ENTRA_CLIENT_ID";

    /// <summary>Scope the web app requests for the API; default api://{client id}/access_as_user (docs/SELF_HOSTING.md).</summary>
    public const string ApiScopeKey = "ENTRA_API_SCOPE";

    public static string ApiScope(IConfiguration configuration) =>
        configuration[ApiScopeKey] is { Length: > 0 } scope ? scope : $"api://{configuration[ClientIdKey]}/access_as_user";

    public static IServiceCollection AddEntraIdAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var tenantId = Required(configuration, TenantIdKey);
        var clientId = Required(configuration, ClientIdKey);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
                options.TokenValidationParameters.ValidAudiences = [clientId, $"api://{clientId}"];

                // Entra issues v1 access tokens unless the app registration asks for v2; both come from this tenant only.
                options.TokenValidationParameters.ValidIssuers =
                [
                    $"https://login.microsoftonline.com/{tenantId}/v2.0",
                    $"https://sts.windows.net/{tenantId}/",
                ];
                options.TokenValidationParameters.NameClaimType = IdentityClaimTypes.Name;

                // Keep Entra's short claim names (oid, tid, ...) instead of mapping them to WS-* URIs.
                options.MapInboundClaims = false;

                // Browsers send the token as query parameter on WebSocket requests to the realtime hubs.
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        if (context.Request.Path.StartsWithSegments(IdentityModule.ApiV1Prefix + "/hubs")
                            && context.Request.Query["access_token"].FirstOrDefault() is { Length: > 0 } token)
                        {
                            context.Token = token;
                        }

                        return Task.CompletedTask;
                    },
                };
            });

        return services;
    }

    private static string Required(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Configuration value '{key}' is missing. See .env.example.");
        }

        return value;
    }
}
