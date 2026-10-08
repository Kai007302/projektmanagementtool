using System.Diagnostics;
using System.Text.Json;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Modules.Identity;

namespace ProjectHub.Api.Modules.Audit;

public static class AuditActions
{
    public const string DepartmentCreated = "DepartmentCreated";
    public const string DepartmentUpdated = "DepartmentUpdated";
    public const string DepartmentDeleted = "DepartmentDeleted";
    public const string DepartmentMemberAdded = "DepartmentMemberAdded";
    public const string DepartmentMemberRoleChanged = "DepartmentMemberRoleChanged";
    public const string DepartmentMemberRemoved = "DepartmentMemberRemoved";

    /// <summary>An organization admin opened a project or article only their admin role lets them see (ADR 0021).</summary>
    public const string OrganizationAdminAccess = "OrganizationAdminAccess";
    public const string ProjectCreated = "ProjectCreated";
    public const string ProjectDeleted = "ProjectDeleted";
    public const string ProjectMemberAdded = "ProjectMemberAdded";
    public const string ProjectMemberRoleChanged = "ProjectMemberRoleChanged";
    public const string ProjectMemberRemoved = "ProjectMemberRemoved";
    public const string ProjectMoved = "ProjectMoved";
    public const string ProjectVisibilityChanged = "ProjectVisibilityChanged";
    public const string TaskDeleted = "TaskDeleted";
    public const string BoardColumnDeleted = "BoardColumnDeleted";
    public const string MilestoneDeleted = "MilestoneDeleted";
    public const string WhiteboardDeleted = "WhiteboardDeleted";
    public const string MailRetried = "MailRetried";
    public const string WebexWebhookRegistered = "WebexWebhookRegistered";
    public const string KnowledgeSpaceCreated = "KnowledgeSpaceCreated";
    public const string KnowledgeArticleCreated = "KnowledgeArticleCreated";
    public const string KnowledgeStatusChanged = "KnowledgeStatusChanged";
    public const string KnowledgeVisibilityChanged = "KnowledgeVisibilityChanged";
    public const string KnowledgePermissionsChanged = "KnowledgePermissionsChanged";
    public const string KnowledgeArticleDeleted = "KnowledgeArticleDeleted";
    public const string PersonalDataExported = "PersonalDataExported";
    public const string UserAnonymized = "UserAnonymized";
    public const string CalendarFeedCreated = "CalendarFeedCreated";
    public const string CalendarFeedRevoked = "CalendarFeedRevoked";
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
