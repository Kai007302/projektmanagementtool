using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProjectHub.Api.Infrastructure.Database;

namespace ProjectHub.Api.Modules.Gantt;

/// <summary>The target task depends on the source task. Both belong to the same project.</summary>
public sealed class TaskDependency
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public Guid SourceTaskId { get; init; }
    public Guid TargetTaskId { get; init; }
    public required string DependencyType { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class GanttMilestone : IVersioned
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public Guid ProjectId { get; init; }
    public required string Name { get; set; }
    public DateOnly MilestoneDate { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
}

public static class DependencyTypes
{
    public const string FinishToStart = "finish_to_start";
    public const string StartToStart = "start_to_start";
    public const string FinishToFinish = "finish_to_finish";
    public const string StartToFinish = "start_to_finish";

    public static readonly IReadOnlyList<string> All = [FinishToStart, StartToStart, FinishToFinish, StartToFinish];
}

internal sealed class TaskDependencyConfiguration : IEntityTypeConfiguration<TaskDependency>
{
    public void Configure(EntityTypeBuilder<TaskDependency> builder)
    {
        builder.ToTable("task_dependency");
    }
}

internal sealed class GanttMilestoneConfiguration : IEntityTypeConfiguration<GanttMilestone>
{
    public void Configure(EntityTypeBuilder<GanttMilestone> builder)
    {
        builder.ToTable("gantt_milestone");
        builder.Property(m => m.Version).IsConcurrencyToken();
    }
}
