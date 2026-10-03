using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ProjectHub.Api.Modules.Audit;

/// <summary>Append-only from the application's point of view: never updated or deleted.</summary>
public sealed class AuditLogEntry
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public Guid? ActorId { get; init; }
    public required string Action { get; init; }
    public string? ResourceType { get; init; }
    public Guid? ResourceId { get; init; }
    public string Metadata { get; init; } = "{}";
    public string? CorrelationId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

internal sealed class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.ToTable("audit_log");
        builder.Property(a => a.Metadata).HasColumnType("jsonb");
    }
}
