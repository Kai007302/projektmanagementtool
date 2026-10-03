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

    public static IServiceCollection AddEntraIdAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var tenantId = Required(configuration, TenantIdKey);
        var clientId = Required(configuration, ClientIdKey);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
                options.TokenValidationParameters.ValidAudiences = [clientId, $"api://{clientId}"];
                options.TokenValidationParameters.NameClaimType = IdentityClaimTypes.Name;

                // Keep Entra's short claim names (oid, tid, ...) instead of mapping them to WS-* URIs.
                options.MapInboundClaims = false;
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
