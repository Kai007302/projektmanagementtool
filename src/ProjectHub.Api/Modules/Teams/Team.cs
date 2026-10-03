using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ProjectHub.Api.Modules.Teams;

public sealed class Team
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
}

public sealed class TeamMember
{
    public Guid OrganizationId { get; init; }
    public Guid TeamId { get; init; }
    public Guid UserId { get; init; }
    public required string Role { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
}

internal sealed class TeamConfiguration : IEntityTypeConfiguration<Team>
{
    public void Configure(EntityTypeBuilder<Team> builder)
    {
        builder.ToTable("team");
        builder.Property(t => t.Version).IsConcurrencyToken();
    }
}

internal sealed class TeamMemberConfiguration : IEntityTypeConfiguration<TeamMember>
{
    public void Configure(EntityTypeBuilder<TeamMember> builder)
    {
        builder.ToTable("team_member");
        builder.HasKey(m => new { m.TeamId, m.UserId });
    }
}
