using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ProjectHub.Api.Modules.Projects;

/// <summary>Project as needed for authorization. CRUD follows in phase 2.</summary>
public sealed class Project
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string Status { get; set; } = "active";
    public Guid OwnerId { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
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
    }
}
