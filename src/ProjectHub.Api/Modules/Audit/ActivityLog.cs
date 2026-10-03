using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Modules.Identity;
using ProjectHub.Api.Modules.Projects;

namespace ProjectHub.Api.Modules.Audit;

/// <summary>Project activity feed visible to project members. Append-only.</summary>
public sealed class ActivityLogEntry
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public Guid? ProjectId { get; init; }
    public Guid? ActorId { get; init; }
    public required string ResourceType { get; init; }
    public Guid? ResourceId { get; init; }
    public required string Action { get; init; }
    public string Metadata { get; init; } = "{}";
    public DateTimeOffset CreatedAt { get; init; }
}

internal sealed class ActivityLogEntryConfiguration : IEntityTypeConfiguration<ActivityLogEntry>
{
    public void Configure(EntityTypeBuilder<ActivityLogEntry> builder)
    {
        builder.ToTable("activity_log");
        builder.Property(a => a.Metadata).HasColumnType("jsonb");

        // Lets EF insert a new project before the activity entry that records its creation.
        builder.HasOne<Project>().WithMany().HasForeignKey(a => a.ProjectId);
    }
}

public static class ActivityActions
{
    public const string ProjectCreated = "ProjectCreated";
    public const string ProjectUpdated = "ProjectUpdated";
    public const string ProjectDeleted = "ProjectDeleted";
    public const string MemberAdded = "MemberAdded";
    public const string MemberRoleChanged = "MemberRoleChanged";
    public const string MemberRemoved = "MemberRemoved";
    public const string TaskCreated = "TaskCreated";
    public const string TaskUpdated = "TaskUpdated";
    public const string TaskDeleted = "TaskDeleted";
    public const string TaskMoved = "TaskMoved";
    public const string BoardColumnCreated = "BoardColumnCreated";
    public const string BoardColumnUpdated = "BoardColumnUpdated";
    public const string BoardColumnDeleted = "BoardColumnDeleted";
    public const string DependencyAdded = "DependencyAdded";
    public const string DependencyRemoved = "DependencyRemoved";
    public const string MilestoneCreated = "MilestoneCreated";
    public const string MilestoneUpdated = "MilestoneUpdated";
    public const string MilestoneDeleted = "MilestoneDeleted";
    public const string WhiteboardCreated = "WhiteboardCreated";
    public const string WhiteboardRenamed = "WhiteboardRenamed";
    public const string WhiteboardDeleted = "WhiteboardDeleted";
    public const string CommentAdded = "CommentAdded";
    public const string AttachmentAdded = "AttachmentAdded";
    public const string AttachmentDeleted = "AttachmentDeleted";
}

public interface IActivityLog
{
    /// <summary>Adds an entry to the current unit of work. Metadata holds ids and field names, not content.</summary>
    void Record(UserContext actor, Guid projectId, string action, string resourceType, Guid resourceId, object? metadata = null);
}

internal sealed class ActivityLog(ProjectHubDbContext db, TimeProvider clock) : IActivityLog
{
    public void Record(UserContext actor, Guid projectId, string action, string resourceType, Guid resourceId, object? metadata = null) =>
        db.Set<ActivityLogEntry>().Add(new ActivityLogEntry
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = actor.OrganizationId,
            ProjectId = projectId,
            ActorId = actor.UserId,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Action = action,
            Metadata = metadata is null ? "{}" : JsonSerializer.Serialize(metadata),
            CreatedAt = clock.GetUtcNow(),
        });
}
