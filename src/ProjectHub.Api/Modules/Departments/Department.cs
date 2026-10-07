using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProjectHub.Api.Infrastructure.Database;

namespace ProjectHub.Api.Modules.Departments;

/// <summary>
/// A department of the organization (ADR 0021). Projects, knowledge spaces and articles belong to exactly one;
/// its members see its projects and knowledge, its leads manage everything in it.
/// </summary>
public sealed class Department : IVersioned
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public required string Name { get; set; }
    public string? Description { get; set; }

    /// <summary>Object id of the Entra ID security group whose members join at sign-in, if any.</summary>
    public string? EntraGroupId { get; set; }

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
}

public sealed class DepartmentMember
{
    public Guid OrganizationId { get; init; }
    public Guid DepartmentId { get; init; }
    public Guid UserId { get; init; }
    public required string Role { get; set; }

    /// <summary><see cref="DepartmentMemberSource"/>: added by hand or taken over from an Entra group.</summary>
    public string Source { get; set; } = DepartmentMemberSource.Manual;

    public DateTimeOffset CreatedAt { get; init; }
}

public static class DepartmentMemberSource
{
    public const string Manual = "manual";
    public const string Entra = "entra";
}

internal sealed class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.ToTable("department");
        builder.Property(d => d.Version).IsConcurrencyToken();
    }
}

internal sealed class DepartmentMemberConfiguration : IEntityTypeConfiguration<DepartmentMember>
{
    public void Configure(EntityTypeBuilder<DepartmentMember> builder)
    {
        builder.ToTable("department_member");
        builder.HasKey(m => new { m.DepartmentId, m.UserId });
    }
}
