using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Modules.Identity;
using ProjectHub.Api.Modules.Identity.Authorization;
using ProjectHub.Api.Modules.Users;

namespace ProjectHub.Api.Modules.Teams;

public sealed record CreateTeamRequest(string? Name, string? Description);

public sealed record AddTeamMemberRequest(Guid? UserId, string? Role);

public static class TeamEndpoints
{
    public static IServiceCollection AddTeamsModule(this IServiceCollection services) =>
        services.AddScoped<TeamService>();

    public static RouteGroupBuilder MapTeamEndpoints(this RouteGroupBuilder api)
    {
        var teams = api.MapGroup("/teams");

        teams.MapGet("/", async (UserContext user, TeamService service, int? limit, int? offset, CancellationToken ct) =>
        {
            var take = limit ?? 50;
            var skip = offset ?? 0;
            if (take is < 1 or > UserEndpoints.MaxPageSize)
            {
                return ApiResults.Validation("limit", $"Must be between 1 and {UserEndpoints.MaxPageSize}.");
            }

            if (skip < 0)
            {
                return ApiResults.Validation("offset", "Must not be negative.");
            }

            var page = await service.ListAsync(user, skip, take + 1, ct);
            var hasMore = page.Count > take;
            return Results.Ok(new PagedResponse<TeamSummary>(page.Take(take).ToList(), hasMore ? skip + take : null));
        });

        teams.MapGet("/{id:guid}", async (Guid id, UserContext user, TeamService service, CancellationToken ct) =>
            await service.GetAsync(user, id, ct) is { } team ? Results.Ok(team) : ApiResults.NotFound("Team"));

        teams.MapPost("/", async (CreateTeamRequest request, UserContext user, TeamService service, CancellationToken ct) =>
        {
            var name = request.Name?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > TeamService.MaxNameLength)
            {
                return ApiResults.Validation("name", $"Required, at most {TeamService.MaxNameLength} characters.");
            }

            var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
            if (description?.Length > TeamService.MaxDescriptionLength)
            {
                return ApiResults.Validation("description", $"At most {TeamService.MaxDescriptionLength} characters.");
            }

            var (outcome, team) = await service.CreateAsync(user, name, description, ct);
            return outcome switch
            {
                TeamOutcome.Success => Results.Created($"/api/v1/teams/{team!.Id}", new TeamSummary(team.Id, team.Name, team.Description, 0)),
                TeamOutcome.Forbidden => ApiResults.Forbidden("Only organization admins can create teams."),
                TeamOutcome.Duplicate => ApiResults.Conflict("A team with this name already exists."),
                _ => throw new InvalidOperationException($"Unexpected outcome {outcome}."),
            };
        });

        teams.MapPost("/{id:guid}/members", async (Guid id, AddTeamMemberRequest request, UserContext user, TeamService service, CancellationToken ct) =>
        {
            if (request.UserId is not { } userId)
            {
                return ApiResults.Validation("userId", "Required.");
            }

            var role = request.Role ?? TeamRole.Member;
            if (!TeamRole.All.Contains(role))
            {
                return ApiResults.Validation("role", $"Must be one of: {string.Join(", ", TeamRole.All)}.");
            }

            return await service.AddMemberAsync(user, id, userId, role, ct) switch
            {
                TeamOutcome.Success => Results.NoContent(),
                TeamOutcome.NotFound => ApiResults.NotFound("Team"),
                TeamOutcome.Forbidden => ApiResults.Forbidden("Only organization admins and team owners can manage members."),
                TeamOutcome.UserNotFound => ApiResults.Validation("userId", "User not found."),
                TeamOutcome.Duplicate => ApiResults.Conflict("The user is already a member of this team."),
                var outcome => throw new InvalidOperationException($"Unexpected outcome {outcome}."),
            };
        });

        teams.MapDelete("/{id:guid}/members/{userId:guid}", async (Guid id, Guid userId, UserContext user, TeamService service, CancellationToken ct) =>
            await service.RemoveMemberAsync(user, id, userId, ct) switch
            {
                TeamOutcome.Success => Results.NoContent(),
                TeamOutcome.NotFound => ApiResults.NotFound("Team"),
                TeamOutcome.Forbidden => ApiResults.Forbidden("Only organization admins and team owners can manage members."),
                TeamOutcome.UserNotFound => ApiResults.NotFound("Team member"),
                var outcome => throw new InvalidOperationException($"Unexpected outcome {outcome}."),
            });

        return api;
    }
}
