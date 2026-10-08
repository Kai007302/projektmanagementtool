using System.Security.Claims;

namespace ProjectHub.Api.Modules.Identity;

internal sealed class HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public string? EntraObjectId => Principal?.FindFirstValue(IdentityClaimTypes.ObjectId);

    public string? TenantId => Principal?.FindFirstValue(IdentityClaimTypes.TenantId);

    public string? DisplayName => Principal?.FindFirstValue(IdentityClaimTypes.Name);

    public string? Email => Principal?.FindFirstValue(IdentityClaimTypes.Email) ?? Principal?.FindFirstValue(IdentityClaimTypes.UserPrincipalName);

    public IReadOnlyList<string>? EntraGroupIds =>
        Principal is not { } principal
            ? []
            : principal.HasClaim(c => c.Type == IdentityClaimTypes.HasGroups)
              || principal.FindAll(IdentityClaimTypes.ClaimNames).Any(c => c.Value.Contains("\"groups\"", StringComparison.Ordinal))
                ? null
                : principal.FindAll(IdentityClaimTypes.Groups).Select(c => c.Value).ToList();
}
