using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Infrastructure.Events;

namespace ProjectHub.Api.Modules.Comments;

public sealed class TaskComment : IVersioned
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public Guid TaskId { get; init; }
    public Guid AuthorId { get; init; }
    public required string Content { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public long Version { get; set; }
}

internal sealed class TaskCommentConfiguration : IEntityTypeConfiguration<TaskComment>
{
    public void Configure(EntityTypeBuilder<TaskComment> builder)
    {
        builder.ToTable("task_comment");
        builder.Property(c => c.Version).IsConcurrencyToken();
    }
}

/// <summary>
/// Raised after a comment mentioning people was saved. The notifications module (phase 6)
/// turns it into in-app and mail notifications.
/// </summary>
public sealed record UsersMentionedInComment(
    Guid OrganizationId, Guid ProjectId, Guid TaskId, Guid CommentId, Guid AuthorId, IReadOnlyList<Guid> MentionedUserIds) : IDomainEvent;
