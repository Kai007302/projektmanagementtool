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

    /// <summary>A short emoji chosen for the project; without one the UI picks one from the id.</summary>
    public string? Icon { get; set; }

    /// <summary>Changes with every uploaded logo; null while the project has none (ADR 0020).</summary>
    public long? LogoVersion { get; set; }

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

/// <summary>The project's logo: a small PNG, JPEG or WebP image, apart from the project row (ADR 0020).</summary>
public sealed class ProjectLogo
{
    public Guid ProjectId { get; init; }
    public Guid OrganizationId { get; init; }
    public required byte[] Content { get; set; }
    public required string ContentType { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed class ProjectLogoConfiguration : IEntityTypeConfiguration<ProjectLogo>
{
    public void Configure(EntityTypeBuilder<ProjectLogo> builder)
    {
        builder.ToTable("project_logo");
        builder.HasKey(l => l.ProjectId);
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
