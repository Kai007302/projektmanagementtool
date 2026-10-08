namespace ProjectHub.Api.Modules.Identity;

/// <summary>Claim names as issued by Entra ID tokens.</summary>
public static class IdentityClaimTypes
{
    public const string ObjectId = "oid";
    public const string TenantId = "tid";
    public const string Name = "name";
    public const string Email = "preferred_username";

    /// <summary>Sign-in name in v1 access tokens, which carry no preferred_username.</summary>
    public const string UserPrincipalName = "upn";

    /// <summary>Object ids of the person's security groups, when the app registration emits them (ADR 0021).</summary>
    public const string Groups = "groups";

    /// <summary>Set instead of <see cref="Groups"/> in implicit-flow tokens when there are too many groups.</summary>
    public const string HasGroups = "hasgroups";

    /// <summary>Points to Microsoft Graph instead of listing the groups when there are too many ("group overage").</summary>
    public const string ClaimNames = "_claim_names";
}
