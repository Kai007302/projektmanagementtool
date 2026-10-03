using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProjectHub.Api.Infrastructure.Database;

namespace ProjectHub.Api.Modules.Whiteboard;

/// <summary>A collaborative Yjs document of a project (ADR 0004, ADR 0009). Its content lives in snapshots and updates.</summary>
public sealed class ProjectWhiteboard : IVersioned
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public Guid ProjectId { get; init; }
    public required string Name { get; set; }

    /// <summary>Stable id of the Yjs document; independent of the row id so documents could move to another sync service.</summary>
    public required string CollaborationDocumentId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
}

/// <summary>The merged document up to and including update <see cref="SequenceNumber"/>.</summary>
public sealed class WhiteboardSnapshot
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public Guid WhiteboardId { get; init; }
    public required string StorageKey { get; init; }
    public long SequenceNumber { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>One accepted Yjs update since the latest snapshot.</summary>
public sealed class WhiteboardUpdate
{
    public Guid WhiteboardId { get; init; }
    public long SequenceNumber { get; init; }
    public Guid OrganizationId { get; init; }
    public required byte[] Payload { get; init; }
    public Guid CreatedBy { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>A task card on a whiteboard. Derived from the document during compaction; holds no task data.</summary>
public sealed class WhiteboardReference
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public Guid WhiteboardId { get; init; }
    public Guid TaskId { get; init; }
    public required string ObjectId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

internal sealed class WhiteboardConfiguration : IEntityTypeConfiguration<ProjectWhiteboard>
{
    public void Configure(EntityTypeBuilder<ProjectWhiteboard> builder)
    {
        builder.ToTable("whiteboard");
        builder.Property(w => w.Version).IsConcurrencyToken();
    }
}

internal sealed class WhiteboardSnapshotConfiguration : IEntityTypeConfiguration<WhiteboardSnapshot>
{
    public void Configure(EntityTypeBuilder<WhiteboardSnapshot> builder) => builder.ToTable("whiteboard_snapshot");
}

internal sealed class WhiteboardUpdateConfiguration : IEntityTypeConfiguration<WhiteboardUpdate>
{
    public void Configure(EntityTypeBuilder<WhiteboardUpdate> builder)
    {
        builder.ToTable("whiteboard_update");
        builder.HasKey(u => new { u.WhiteboardId, u.SequenceNumber });
    }
}

internal sealed class WhiteboardReferenceConfiguration : IEntityTypeConfiguration<WhiteboardReference>
{
    public void Configure(EntityTypeBuilder<WhiteboardReference> builder) => builder.ToTable("whiteboard_reference");
}
