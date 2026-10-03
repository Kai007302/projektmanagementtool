using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Infrastructure.Outcomes;
using ProjectHub.Api.Modules.Audit;
using ProjectHub.Api.Modules.Identity;
using ProjectHub.Api.Modules.Identity.Authorization;
using ProjectHub.Api.Modules.Users;

namespace ProjectHub.Api.Modules.Projects;

public sealed record ActivityResponse(
    Guid Id, string Action, string ResourceType, Guid? ResourceId, Guid? ActorId, string? ActorName, JsonElement Metadata, DateTimeOffset CreatedAt);

/// <summary>The project's activity feed, newest first.</summary>
public sealed class ProjectActivityService(ProjectHubDbContext db, ProjectAccess access)
{
    public async Task<ServiceResult<IReadOnlyList<ActivityResponse>>> ListAsync(UserContext user, Guid projectId, Paging paging, CancellationToken ct)
    {
        if (await access.RequireAsync(user, projectId, ProjectPermission.View, ct) is { } failure)
        {
            return failure;
        }

        var rows = await (
                from entry in db.Set<ActivityLogEntry>().AsNoTracking()
                join actor in db.Set<AppUser>().AsNoTracking() on entry.ActorId equals actor.Id into actors
                from actor in actors.DefaultIfEmpty()
                where entry.ProjectId == projectId && entry.OrganizationId == user.OrganizationId
                orderby entry.CreatedAt descending, entry.Id descending
                select new { Entry = entry, ActorName = actor == null ? null : actor.DisplayName })
            .Skip(paging.Skip).Take(paging.Take + 1)
            .ToListAsync(ct);

        return rows
            .Select(r => new ActivityResponse(
                r.Entry.Id, r.Entry.Action, r.Entry.ResourceType, r.Entry.ResourceId, r.Entry.ActorId, r.ActorName,
                JsonDocument.Parse(r.Entry.Metadata).RootElement.Clone(), r.Entry.CreatedAt))
            .ToList();
    }
}
