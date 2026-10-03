using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ProjectHub.Api.Modules.Identity.Development;

/// <summary>
/// Signs requests in as one of the synthetic users from <see cref="DevelopmentSeedData"/>,
/// chosen by the X-Dev-User header or the configured default. Only registered in Development.
/// Issues the same claims as an Entra ID token, so everything downstream is provider-agnostic.
/// </summary>
internal sealed class DevelopmentAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<DevelopmentIdentityOptions> identity)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string objectId = Request.Headers[DevelopmentIdentityOptions.UserHeader].FirstOrDefault() ?? identity.Value.DefaultObjectId;
        var user = DevelopmentSeedData.Users.SingleOrDefault(u => u.ObjectId == objectId);
        if (user is null)
        {
            return Task.FromResult(AuthenticateResult.Fail("Unknown development user."));
        }

        var claims = new[]
        {
            new Claim(IdentityClaimTypes.ObjectId, user.ObjectId),
            new Claim(IdentityClaimTypes.TenantId, DevelopmentSeedData.OrganizationOf(user).TenantId),
            new Claim(IdentityClaimTypes.Name, user.DisplayName),
            new Claim(IdentityClaimTypes.Email, user.Email),
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name, IdentityClaimTypes.Name, null));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}
