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
}
