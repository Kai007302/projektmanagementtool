using Microsoft.EntityFrameworkCore;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Modules.Identity;

namespace ProjectHub.Api.Modules.Users;

public sealed record UserResponse(Guid Id, string DisplayName, string Email, string? Department, string Status);

public sealed record MeResponse(Guid Id, string DisplayName, string Email, Guid OrganizationId, string OrganizationRole);

public static class UserEndpoints
{
    public static RouteGroupBuilder MapUserEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/me", (UserContext user) =>
            Results.Ok(new MeResponse(user.UserId, user.DisplayName, user.Email, user.OrganizationId, user.OrganizationRole)));

        api.MapGet("/users", async (UserContext user, ProjectHubDbContext db, string? search, int? limit, int? offset, CancellationToken ct) =>
        {
            if (!Paging.TryCreate(limit, offset, out var paging, out var error))
            {
                return error!;
            }

            // Anonymized people (ADR 0015) are no longer anyone to find, assign or mention.
            var query = db.Set<AppUser>().AsNoTracking().Where(u => u.OrganizationId == user.OrganizationId && u.AnonymizedAt == null);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var pattern = $"%{EscapeLike(search.Trim())}%";
                query = query.Where(u => EF.Functions.ILike(u.DisplayName, pattern, "\\") || EF.Functions.ILike(u.Email, pattern, "\\"));
            }

            var rows = await query
                .OrderBy(u => u.DisplayName).ThenBy(u => u.Id)
                .Skip(paging.Skip).Take(paging.Take + 1)
                .Select(u => new UserResponse(u.Id, u.DisplayName, u.Email, u.Department, u.Status))
                .ToListAsync(ct);

            return Results.Ok(paging.ToPage(rows));
        });

        return api;
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
