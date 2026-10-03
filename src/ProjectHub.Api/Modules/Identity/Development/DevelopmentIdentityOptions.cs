namespace ProjectHub.Api.Modules.Identity.Development;

public sealed class DevelopmentIdentityOptions
{
    public const string SectionName = "DevelopmentIdentity";

    /// <summary>Request header that selects a synthetic user by object id.</summary>
    public const string UserHeader = "X-Dev-User";

    /// <summary>Query parameter with the same meaning, only for realtime hubs below <see cref="HubPathPrefix"/>.</summary>
    public const string UserQueryParameter = "devUser";

    public const string HubPathPrefix = IdentityModule.ApiV1Prefix + "/hubs";

    /// <summary>Object id of the synthetic user signed in when no header is sent.</summary>
    public string DefaultObjectId { get; set; } = DevelopmentSeedData.Ada.ObjectId;
}
