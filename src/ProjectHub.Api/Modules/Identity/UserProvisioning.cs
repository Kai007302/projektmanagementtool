using Microsoft.EntityFrameworkCore;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Modules.Identity.Authorization;

namespace ProjectHub.Api.Modules.Identity;

/// <summary>
/// Whether people from the configured Entra tenant get a ProjectHub user at their first sign-in (DEC-013, ADR 0014).
/// Off by default: then unknown people get 403 until someone creates their user.
/// </summary>
public sealed record UserProvisioningOptions(bool OnFirstSignIn, string? AllowedTenantId, string OrganizationName)
{
    public const string ModeKey = "PROJECTHUB_USER_PROVISIONING";
    public const string FirstSignIn = "first-sign-in";
    public const string OrganizationNameKey = "PROJECTHUB_ORGANIZATION_NAME";

    public static UserProvisioningOptions FromConfiguration(IConfiguration configuration) =>
        new(
            string.Equals(configuration[ModeKey], FirstSignIn, StringComparison.OrdinalIgnoreCase),
            configuration[EntraIdRegistration.TenantIdKey],
            configuration[OrganizationNameKey] is { Length: > 0 } name ? name.Trim() : "ProjectHub");
}

/// <summary>
/// Creates the organization of the configured tenant and the signed-in person's user. The first person of an
/// organization becomes its admin, everyone after that a member. Existing users are never changed: an inactive
/// user stays inactive, so deactivating someone keeps them out.
/// </summary>
internal sealed class UserProvisioning(
    UserProvisioningOptions options,
    ProjectHubDbContext db,
    ILogger<UserProvisioning> logger)
{
    public async Task TryProvisionAsync(ICurrentUser caller, CancellationToken ct)
    {
        if (!options.OnFirstSignIn
            || caller.TenantId is not { Length: > 0 } tenantId
            || caller.EntraObjectId is not { Length: > 0 } objectId
            || !string.Equals(tenantId, options.AllowedTenantId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (caller.Email is not { Length: > 0 } email)
        {
            logger.LogWarning("A first sign-in without an e-mail claim was not provisioned.");
            return;
        }

        var displayName = caller.DisplayName is { Length: > 0 } name ? name : email;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // One provisioning at a time per tenant, so two first sign-ins can not both become admin.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"select pg_advisory_xact_lock(hashtextextended({"projecthub:provisioning:" + tenantId}, 0))", ct);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             insert into organization (name, slug, entra_tenant_id)
             values ({options.OrganizationName}, {"tenant-" + tenantId.ToLowerInvariant()}, {tenantId})
             on conflict do nothing
             """, ct);

        // Conflicts (same person, or the e-mail already taken by another account) leave the existing user untouched.
        var created = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             insert into app_user (organization_id, entra_object_id, email, display_name, organization_role)
             select o.id, {objectId}, {email}, {displayName},
                    case when exists (select 1 from app_user u where u.organization_id = o.id)
                         then {OrganizationRole.Member} else {OrganizationRole.Admin} end
             from organization o
             where o.entra_tenant_id = {tenantId}
             on conflict do nothing
             """, ct);

        await transaction.CommitAsync(ct);

        if (created == 1)
        {
            logger.LogInformation("Provisioned a ProjectHub user at first sign-in.");
        }
        else
        {
            logger.LogWarning("First sign-in was not provisioned: the account or its e-mail address already has a user.");
        }
    }
}
