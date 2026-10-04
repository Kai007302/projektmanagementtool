namespace ProjectHub.Api.Modules.Identity;

/// <summary>What the web app needs to sign people in. Public: tenant and client id are not secrets.</summary>
public sealed record SignInConfiguration(string Mode, string? TenantId, string? ClientId, string? Scope);

public static class SignInEndpoints
{
    public const string Development = "development";
    public const string EntraId = "entra-id";

    public static RouteGroupBuilder MapSignInEndpoints(this RouteGroupBuilder api, IHostEnvironment environment, IConfiguration configuration)
    {
        var signIn = environment.IsDevelopment()
            ? new SignInConfiguration(Development, null, null, null)
            : new SignInConfiguration(
                EntraId,
                configuration[EntraIdRegistration.TenantIdKey],
                configuration[EntraIdRegistration.ClientIdKey],
                EntraIdRegistration.ApiScope(configuration));

        api.MapGet("/sign-in", () => Results.Ok(signIn)).AllowAnonymous();
        return api;
    }
}
