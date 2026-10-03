using Microsoft.EntityFrameworkCore;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Modules.Identity;

namespace ProjectHub.Api.Modules.Users;

public sealed record UserResponse(Guid Id, string DisplayName, string Email, string? Department, string Status);

public sealed record MeResponse(Guid Id, string DisplayName, string Email, Guid OrganizationId, string OrganizationRole);

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int? NextOffset);

public static class UserEndpoints
{
    public const int MaxPageSize = 100;

    public static RouteGroupBuilder MapUserEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/me", (UserContext user) =>
            Results.Ok(new MeResponse(user.UserId, user.DisplayName, user.Email, user.OrganizationId, user.OrganizationRole)));

        api.MapGet("/users", async (UserContext user, ProjectHubDbContext db, string? search, int? limit, int? offset, CancellationToken ct) =>
        {
            var take = limit ?? 50;
            var skip = offset ?? 0;
            if (take is < 1 or > MaxPageSize)
            {
                return ApiResults.Validation("limit", $"Must be between 1 and {MaxPageSize}.");
            }

            if (skip < 0)
            {
                return ApiResults.Validation("offset", "Must not be negative.");
            }

            var query = db.Set<AppUser>().AsNoTracking().Where(u => u.OrganizationId == user.OrganizationId);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var pattern = $"%{EscapeLike(search.Trim())}%";
                query = query.Where(u => EF.Functions.ILike(u.DisplayName, pattern, "\\") || EF.Functions.ILike(u.Email, pattern, "\\"));
            }

            var page = await query
                .OrderBy(u => u.DisplayName).ThenBy(u => u.Id)
                .Skip(skip).Take(take + 1)
                .Select(u => new UserResponse(u.Id, u.DisplayName, u.Email, u.Department, u.Status))
                .ToListAsync(ct);

            var hasMore = page.Count > take;
            return Results.Ok(new PagedResponse<UserResponse>(page.Take(take).ToList(), hasMore ? skip + take : null));
        });

        return api;
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
