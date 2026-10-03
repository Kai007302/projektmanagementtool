using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProjectHub.Api.Infrastructure.Database;

namespace ProjectHub.Api.Modules.Projects;

public sealed class Project : IVersioned
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string Status { get; set; } = ProjectStatus.Active;
    public Guid OwnerId { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public long Version { get; set; }
}

public static class ProjectStatus
{
    public const string Planned = "planned";
    public const string Active = "active";
    public const string OnHold = "on_hold";
    public const string Completed = "completed";
    public const string Archived = "archived";

    public static readonly IReadOnlyList<string> All = [Planned, Active, OnHold, Completed, Archived];
}

public sealed class ProjectMember
{
    public Guid OrganizationId { get; init; }
    public Guid ProjectId { get; init; }
    public Guid UserId { get; init; }
    public required string Role { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
}

internal sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("project");
        builder.Property(p => p.Version).IsConcurrencyToken();
    }
}

internal sealed class ProjectMemberConfiguration : IEntityTypeConfiguration<ProjectMember>
{
    public void Configure(EntityTypeBuilder<ProjectMember> builder)
    {
        builder.ToTable("project_member");
        builder.HasKey(m => new { m.ProjectId, m.UserId });

        // Lets EF insert a new project before its first members.
        builder.HasOne<Project>().WithMany().HasForeignKey(m => m.ProjectId);
    }
}
