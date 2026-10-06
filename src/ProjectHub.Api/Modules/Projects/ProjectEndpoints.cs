using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Modules.Identity;

namespace ProjectHub.Api.Modules.Projects;

public sealed record AddProjectMemberRequest(Guid? UserId, string? Role);

public sealed record ChangeProjectMemberRequest(string? Role);

public static class ProjectEndpoints
{
    public static IServiceCollection AddProjectsModule(this IServiceCollection services) =>
        services
            .AddScoped<ProjectAccess>()
            .AddScoped<ProjectService>()
            .AddScoped<ProjectLogoService>()
            .AddScoped<ProjectActivityService>();

    public static RouteGroupBuilder MapProjectEndpoints(this RouteGroupBuilder api)
    {
        var projects = api.MapGroup("/projects");

        projects.MapGet("/", async (UserContext user, ProjectService service, int? limit, int? offset, CancellationToken ct) =>
            Paging.TryCreate(limit, offset, out var paging, out var error)
                ? Results.Ok(paging.ToPage(await service.ListAsync(user, paging, ct)))
                : error!);

        projects.MapPost("/", async (CreateProjectRequest request, UserContext user, ProjectService service, CancellationToken ct) =>
            ApiResults.From(await service.CreateAsync(user, request, ct), project => Results.Created(
                $"/api/v1/projects/{project.Id}",
                new ProjectSummary(project.Id, project.Name, project.Description, project.Status, project.StartDate, project.EndDate, "admin", project.Version))));

        projects.MapGet("/{id:guid}", async (Guid id, UserContext user, ProjectService service, CancellationToken ct) =>
            ApiResults.Ok(await service.GetAsync(user, id, ct)));

        projects.MapPatch("/{id:guid}", async (Guid id, PatchDocument patch, UserContext user, ProjectService service, CancellationToken ct) =>
            ApiResults.Ok(await service.UpdateAsync(user, id, patch, ct)));

        projects.MapDelete("/{id:guid}", async (Guid id, UserContext user, ProjectService service, CancellationToken ct) =>
            ApiResults.NoContent(await service.DeleteAsync(user, id, ct)));

        projects.MapPost("/{id:guid}/members", async (Guid id, AddProjectMemberRequest request, UserContext user, ProjectService service, CancellationToken ct) =>
            ApiResults.NoContent(await service.AddMemberAsync(user, id, request.UserId, request.Role, ct)));

        projects.MapPatch("/{id:guid}/members/{userId:guid}", async (Guid id, Guid userId, ChangeProjectMemberRequest request, UserContext user, ProjectService service, CancellationToken ct) =>
            ApiResults.NoContent(await service.ChangeMemberRoleAsync(user, id, userId, request.Role, ct)));

        projects.MapDelete("/{id:guid}/members/{userId:guid}", async (Guid id, Guid userId, UserContext user, ProjectService service, CancellationToken ct) =>
            ApiResults.NoContent(await service.RemoveMemberAsync(user, id, userId, ct)));

        projects.MapGet("/{id:guid}/logo", async (Guid id, UserContext user, ProjectLogoService service, HttpContext http, CancellationToken ct) =>
            ApiResults.From(await service.GetAsync(user, id, ct), logo =>
            {
                http.Response.Headers.XContentTypeOptions = "nosniff";
                return Results.File(logo.Content, logo.ContentType);
            }));

        // Bearer-token API without cookies, so no antiforgery token is needed.
        projects.MapPut("/{id:guid}/logo", async (Guid id, IFormFile? file, UserContext user, ProjectLogoService service, CancellationToken ct) =>
                ApiResults.Ok(await service.UploadAsync(user, id, file, ct)))
            .DisableAntiforgery()
            .RequireRateLimiting(HttpHardening.UploadPolicy)
            .WithMetadata(new LogoSizeLimit());

        projects.MapDelete("/{id:guid}/logo", async (Guid id, UserContext user, ProjectLogoService service, CancellationToken ct) =>
            ApiResults.Ok(await service.DeleteAsync(user, id, ct)));

        projects.MapGet("/{id:guid}/activity", async (Guid id, UserContext user, ProjectActivityService service, int? limit, int? offset, CancellationToken ct) =>
            Paging.TryCreate(limit, offset, out var paging, out var error)
                ? ApiResults.From(await service.ListAsync(user, id, paging, ct), rows => Results.Ok(paging.ToPage(rows)))
                : error!);

        return api;
    }

    /// <summary>A little more than the largest logo, for the multipart framing around it.</summary>
    private sealed class LogoSizeLimit : Microsoft.AspNetCore.Http.Metadata.IRequestSizeLimitMetadata
    {
        public long? MaxRequestBodySize => ProjectLogoService.MaxSizeBytes + 64 * 1024;
    }
}
