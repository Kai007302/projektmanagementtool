namespace ProjectHub.Api.Modules.Identity;

/// <summary>
/// The authenticated caller as stated by the identity provider (Entra ID in production,
/// the development identity provider locally). Says nothing about permissions;
/// see <see cref="UserContext"/> for the resolved ProjectHub user.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>Entra ID object id (claim "oid").</summary>
    string? EntraObjectId { get; }

    /// <summary>Entra ID tenant id (claim "tid").</summary>
    string? TenantId { get; }

    string? DisplayName { get; }

    string? Email { get; }
}
