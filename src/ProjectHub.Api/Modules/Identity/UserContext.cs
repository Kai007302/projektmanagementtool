using ProjectHub.Api.Modules.Identity.Authorization;

namespace ProjectHub.Api.Modules.Identity;

/// <summary>
/// The ProjectHub user behind the current request, resolved from the token's tenant and object id.
/// Every organization-bound query starts from <see cref="OrganizationId"/>.
/// </summary>
public sealed record UserContext(
    Guid UserId,
    Guid OrganizationId,
    string OrganizationRole,
    string DisplayName,
    string Email)
{
    public bool IsOrganizationAdmin => OrganizationRole == Authorization.OrganizationRole.Admin;
}
