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

    /// <summary>
    /// Object ids of the Entra ID security groups in the token (claim "groups"), empty when it has none. Null when
    /// the token only says there are too many groups to include ("group overage"), so the groups are unknown.
    /// </summary>
    IReadOnlyList<string>? EntraGroupIds { get; }
}
