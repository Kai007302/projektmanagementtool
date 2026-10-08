using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Infrastructure.Events;

namespace ProjectHub.Api.Modules.Tasks;

/// <summary>
/// The central domain object. Kanban and Gantt are views on it and never keep their own copy.
/// </summary>
public sealed class ProjectTask : IVersioned
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public Guid ProjectId { get; init; }
    public Guid? ParentTaskId { get; set; }
    public Guid? KanbanColumnId { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public string Status { get; set; } = TaskStatus.Todo;
    public string Priority { get; set; } = TaskPriority.Normal;
    public Guid CreatorId { get; init; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? DueDate { get; set; }

    /// <summary>Calculated from status and subtasks (<see cref="TaskProgress"/>), never set by hand.</summary>
    public short Progress { get; set; }
    public decimal? EstimatedHours { get; set; }
    public decimal? BoardPosition { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
}

/// <summary>Someone a task is assigned to; a task can have several (ADR 0022).</summary>
public sealed class TaskAssignee
{
    public Guid OrganizationId { get; init; }
    public Guid TaskId { get; init; }
    public Guid UserId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Raised after a task was assigned to someone (on creation or when the person was added to the assignees).</summary>
public sealed record TaskAssigned(Guid OrganizationId, Guid ProjectId, Guid TaskId, Guid AssigneeId, Guid ActorId) : IDomainEvent;

public static class TaskStatus
{
    public const string Todo = "todo";
    public const string InProgress = "in_progress";
    public const string Done = "done";

    public static readonly IReadOnlyList<string> All = [Todo, InProgress, Done];
}

public static class TaskPriority
{
    public const string Low = "low";
    public const string Normal = "normal";
    public const string High = "high";
    public const string Urgent = "urgent";

    public static readonly IReadOnlyList<string> All = [Low, Normal, High, Urgent];
}

internal sealed class ProjectTaskConfiguration : IEntityTypeConfiguration<ProjectTask>
{
    public void Configure(EntityTypeBuilder<ProjectTask> builder)
    {
        builder.ToTable("task");
        builder.Property(t => t.Version).IsConcurrencyToken();
    }
}

internal sealed class TaskAssigneeConfiguration : IEntityTypeConfiguration<TaskAssignee>
{
    public void Configure(EntityTypeBuilder<TaskAssignee> builder)
    {
        builder.ToTable("task_assignee");
        builder.HasKey(a => new { a.TaskId, a.UserId });
    }
}
