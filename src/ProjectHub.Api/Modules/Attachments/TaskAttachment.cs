using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ProjectHub.Api.Modules.Attachments;

/// <summary>Attachment metadata. The file itself lives in <see cref="IAttachmentStorage"/> under <see cref="StorageKey"/>.</summary>
public sealed class TaskAttachment
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public Guid TaskId { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public long SizeBytes { get; init; }
    public required string StorageKey { get; init; }
    public Guid UploadedBy { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

internal sealed class TaskAttachmentConfiguration : IEntityTypeConfiguration<TaskAttachment>
{
    public void Configure(EntityTypeBuilder<TaskAttachment> builder) => builder.ToTable("task_attachment");
}
