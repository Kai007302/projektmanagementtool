namespace ProjectHub.Api.Modules.Identity;

/// <summary>
/// The authenticated caller. Backed by Entra ID in production and by the
/// development identity provider locally; consumers never see the difference.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>Entra ID object id (claim "oid").</summary>
    string? EntraObjectId { get; }

    string? DisplayName { get; }

    string? Email { get; }
}
