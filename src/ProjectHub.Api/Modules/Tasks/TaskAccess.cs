using Microsoft.EntityFrameworkCore;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Infrastructure.Outcomes;
using ProjectHub.Api.Modules.Identity;
using ProjectHub.Api.Modules.Identity.Authorization;
using ProjectHub.Api.Modules.Projects;

namespace ProjectHub.Api.Modules.Tasks;

/// <summary>Permission checks for content hanging off a task (comments, attachments).</summary>
public sealed class TaskAccess(ProjectHubDbContext db, ProjectAccess projectAccess)
{
    /// <summary>
    /// Finds the active task and checks <paramref name="permission"/> on its project.
    /// Tasks the caller cannot see are reported as "not found".
    /// </summary>
    public async Task<(Guid ProjectId, ServiceFailure? Failure)> RequireAsync(UserContext user, Guid taskId, ProjectPermission permission, CancellationToken ct)
    {
        var projectId = await db.Set<ProjectTask>()
            .Where(t => t.Id == taskId && t.OrganizationId == user.OrganizationId && t.DeletedAt == null)
            .Select(t => (Guid?)t.ProjectId)
            .SingleOrDefaultAsync(ct);
        if (projectId is null || await projectAccess.RequireAsync(user, projectId.Value, ProjectPermission.View, ct) is not null)
        {
            return (Guid.Empty, ServiceFailure.NotFound("Task"));
        }

        return (projectId.Value, await projectAccess.RequireAsync(user, projectId.Value, permission, ct));
    }
}
