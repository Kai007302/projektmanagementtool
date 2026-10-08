using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Modules.Projects;

namespace ProjectHub.Api.Modules.Kanban;

/// <summary>The one board of a project. Holds only layout; cards are the project's tasks.</summary>
public sealed class KanbanBoard
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public Guid ProjectId { get; init; }
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
}

/// <summary>A board column. Every column stands for one task status (migration 005).</summary>
public sealed class KanbanColumn : IVersioned
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public Guid BoardId { get; init; }
    public required string Name { get; set; }
    public required string TaskStatus { get; set; }
    public decimal Position { get; set; }
    public int? WipLimit { get; set; }

    /// <summary>One of <see cref="KanbanColumnColors.All"/>, or null for the default look (migration 018).</summary>
    public string? Color { get; set; }

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
}

public static class KanbanColumnColors
{
    public static readonly IReadOnlyList<string> All = ["gray", "blue", "green", "yellow", "orange", "red", "purple", "pink"];
}

internal sealed class KanbanBoardConfiguration : IEntityTypeConfiguration<KanbanBoard>
{
    public void Configure(EntityTypeBuilder<KanbanBoard> builder)
    {
        builder.ToTable("kanban_board");
        builder.HasOne<Project>().WithMany().HasForeignKey(b => b.ProjectId);
    }
}

internal sealed class KanbanColumnConfiguration : IEntityTypeConfiguration<KanbanColumn>
{
    public void Configure(EntityTypeBuilder<KanbanColumn> builder)
    {
        builder.ToTable("kanban_column");
        builder.Property(c => c.Version).IsConcurrencyToken();

        // Lets EF insert a new board before its default columns.
        builder.HasOne<KanbanBoard>().WithMany().HasForeignKey(c => c.BoardId);
    }
}
