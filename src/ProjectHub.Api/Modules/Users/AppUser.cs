using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ProjectHub.Api.Modules.Users;

public sealed class AppUser
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public required string EntraObjectId { get; set; }
    public required string Email { get; set; }
    public required string DisplayName { get; set; }
    public string? Department { get; set; }
    public string Status { get; set; } = UserStatus.Active;
    public string OrganizationRole { get; set; } = Identity.Authorization.OrganizationRole.Member;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>Set when the person was anonymized (Art. 17 GDPR, ADR 0017); the row then identifies nobody.</summary>
    public DateTimeOffset? AnonymizedAt { get; set; }
    public long Version { get; set; }
}

public static class UserStatus
{
    public const string Active = "active";
    public const string Inactive = "inactive";
    public const string Invited = "invited";
}

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.ToTable("app_user");
        builder.Property(u => u.Version).IsConcurrencyToken();
    }
}
