using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Modules.Identity;

namespace ProjectHub.Api.Modules.Departments;

public static class DepartmentEndpoints
{
    public static IServiceCollection AddDepartmentsModule(this IServiceCollection services, IConfiguration configuration) =>
        services
            .AddScoped<DepartmentService>()
            .AddSingleton(EntraDepartmentOptions.FromConfiguration(configuration))
            .AddScoped<EntraDepartmentSync>()
            .AddScoped<DepartmentOnboarding>();

    public static RouteGroupBuilder MapDepartmentEndpoints(this RouteGroupBuilder api)
    {
        var departments = api.MapGroup("/departments");

        departments.MapGet("/", async (UserContext user, DepartmentService service, int? limit, int? offset, CancellationToken ct) =>
            Paging.TryCreate(limit, offset, out var paging, out var error)
                ? Results.Ok(paging.ToPage(await service.ListAsync(user, paging, ct)))
                : error!);

        departments.MapGet("/unassigned", async (UserContext user, DepartmentService service, CancellationToken ct) =>
            ApiResults.Ok(await service.UnassignedAsync(user, ct)));

        departments.MapGet("/{id:guid}", async (Guid id, UserContext user, DepartmentService service, CancellationToken ct) =>
            ApiResults.Ok(await service.GetAsync(user, id, ct)));

        departments.MapPost("/", async (CreateDepartmentRequest request, UserContext user, DepartmentService service, CancellationToken ct) =>
            ApiResults.From(await service.CreateAsync(user, request, ct), department => Results.Created($"/api/v1/departments/{department.Id}", department)));

        departments.MapPatch("/{id:guid}", async (Guid id, PatchDocument patch, UserContext user, DepartmentService service, CancellationToken ct) =>
            ApiResults.Ok(await service.UpdateAsync(user, id, patch, ct)));

        departments.MapDelete("/{id:guid}", async (Guid id, UserContext user, DepartmentService service, CancellationToken ct) =>
            ApiResults.NoContent(await service.DeleteAsync(user, id, ct)));

        departments.MapPost("/{id:guid}/members", async (Guid id, AddDepartmentMemberRequest request, UserContext user, DepartmentService service, CancellationToken ct) =>
            ApiResults.NoContent(await service.AddMemberAsync(user, id, request, ct)));

        departments.MapPatch("/{id:guid}/members/{userId:guid}", async (Guid id, Guid userId, ChangeDepartmentMemberRequest request, UserContext user, DepartmentService service, CancellationToken ct) =>
            ApiResults.NoContent(await service.ChangeMemberRoleAsync(user, id, userId, request.Role, ct)));

        departments.MapDelete("/{id:guid}/members/{userId:guid}", async (Guid id, Guid userId, UserContext user, DepartmentService service, CancellationToken ct) =>
            ApiResults.NoContent(await service.RemoveMemberAsync(user, id, userId, ct)));

        return api;
    }
}
