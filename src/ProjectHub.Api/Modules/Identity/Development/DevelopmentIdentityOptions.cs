namespace ProjectHub.Api.Modules.Identity.Development;

/// <summary>Synthetic user signed in automatically in Development. Never real people.</summary>
public sealed class DevelopmentIdentityOptions
{
    public const string SectionName = "DevelopmentIdentity";

    public string ObjectId { get; set; } = "00000000-0000-0000-0000-000000000001";

    public string DisplayName { get; set; } = "Dev User";

    public string Email { get; set; } = "dev.user@example.invalid";
}
