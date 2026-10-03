using System.Diagnostics;
using System.Text.Json;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Modules.Identity;

namespace ProjectHub.Api.Modules.Audit;

public static class AuditActions
{
    public const string TeamCreated = "TeamCreated";
    public const string TeamMemberAdded = "TeamMemberAdded";
    public const string TeamMemberRemoved = "TeamMemberRemoved";
    public const string ProjectCreated = "ProjectCreated";
    public const string ProjectDeleted = "ProjectDeleted";
    public const string ProjectMemberAdded = "ProjectMemberAdded";
    public const string ProjectMemberRoleChanged = "ProjectMemberRoleChanged";
    public const string ProjectMemberRemoved = "ProjectMemberRemoved";
    public const string TaskDeleted = "TaskDeleted";
}

public interface IAuditLog
{
    /// <summary>
    /// Adds an audit entry to the current unit of work, so it is committed atomically
    /// with the change it describes. Metadata must not contain secrets or tokens.
    /// </summary>
    void Record(UserContext actor, string action, string resourceType, Guid resourceId, object? metadata = null);
}

internal sealed class AuditLog(ProjectHubDbContext db, TimeProvider clock) : IAuditLog
{
    public void Record(UserContext actor, string action, string resourceType, Guid resourceId, object? metadata = null) =>
        db.Set<AuditLogEntry>().Add(new AuditLogEntry
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = actor.OrganizationId,
            ActorId = actor.UserId,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Metadata = metadata is null ? "{}" : JsonSerializer.Serialize(metadata),
            CorrelationId = Activity.Current?.TraceId.ToString(),
            CreatedAt = clock.GetUtcNow(),
        });
}

public static class AuditModule
{
    public static IServiceCollection AddAuditModule(this IServiceCollection services) =>
        services
            .AddScoped<IAuditLog, AuditLog>()
            .AddScoped<IActivityLog, ActivityLog>();
}
