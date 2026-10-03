using System.Security.Claims;

namespace ProjectHub.Api.Modules.Identity;

internal sealed class HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public string? EntraObjectId => Principal?.FindFirstValue(IdentityClaimTypes.ObjectId);

    public string? DisplayName => Principal?.FindFirstValue(IdentityClaimTypes.Name);

    public string? Email => Principal?.FindFirstValue(IdentityClaimTypes.Email);
}
