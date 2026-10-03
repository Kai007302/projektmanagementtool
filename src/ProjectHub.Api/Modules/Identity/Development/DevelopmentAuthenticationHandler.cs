using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ProjectHub.Api.Modules.Identity.Development;

/// <summary>
/// Signs every request in as the configured synthetic user. Only registered in Development.
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
        var user = identity.Value;
        var claims = new[]
        {
            new Claim(IdentityClaimTypes.ObjectId, user.ObjectId),
            new Claim(IdentityClaimTypes.Name, user.DisplayName),
            new Claim(IdentityClaimTypes.Email, user.Email),
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name, IdentityClaimTypes.Name, null));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}
