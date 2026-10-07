using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ProjectHub.Api.Modules.Notifications;

/// <summary>A message for one person. Only that person can read it.</summary>
public sealed class Notification
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public Guid UserId { get; init; }
    public required string Type { get; init; }
    public required string Title { get; init; }
    public string? Body { get; init; }
    public string? ResourceType { get; init; }
    public Guid? ResourceId { get; init; }
    public DateTimeOffset? ReadAt { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Channels a person wants to be notified on. Without a row both in-app and mail are on.</summary>
public sealed class NotificationPreference
{
    public Guid OrganizationId { get; init; }
    public Guid UserId { get; init; }
    public bool InAppEnabled { get; set; } = true;
    public bool EmailEnabled { get; set; } = true;
    public bool WebexEnabled { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class NotificationTypes
{
    public const string TaskAssigned = "task_assigned";
    public const string TaskCommentMention = "task_comment_mention";
    public const string KnowledgeCommentMention = "knowledge_comment_mention";
    public const string ProjectMemberAdded = "project_member_added";

    /// <summary>Someone signed in for the first time and belongs to no department yet (ADR 0021).</summary>
    public const string PersonWithoutDepartment = "person_without_department";
}

public static class NotificationResources
{
    public const string Task = "task";
    public const string KnowledgeArticle = "knowledge_article";
    public const string Project = "project";
    public const string User = "user";
}

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder) => builder.ToTable("notification");
}

internal sealed class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.ToTable("notification_preference");
        builder.HasKey(p => new { p.OrganizationId, p.UserId });
    }
}
