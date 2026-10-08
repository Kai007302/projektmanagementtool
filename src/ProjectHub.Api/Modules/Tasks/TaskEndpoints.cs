using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Modules.Identity;
using ProjectHub.Api.Modules.Tasks.Transfer;

namespace ProjectHub.Api.Modules.Tasks;

public static class TaskEndpoints
{
    public static IServiceCollection AddTasksModule(this IServiceCollection services) =>
        services.AddScoped<TaskService>().AddScoped<TaskProgress>().AddScoped<TaskAccess>().AddScoped<TaskTransferService>();

    public static RouteGroupBuilder MapTaskEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/projects/{projectId:guid}/tasks", async (
            Guid projectId, UserContext user, TaskService service,
            Guid? parentTaskId, bool? topLevelOnly, string? status, Guid? assigneeId, int? limit, int? offset, CancellationToken ct) =>
        {
            if (!Paging.TryCreate(limit, offset, out var paging, out var error))
            {
                return error!;
            }

            var filter = new TaskFilter(parentTaskId, topLevelOnly ?? false, status, assigneeId);
            return ApiResults.From(await service.ListAsync(user, projectId, filter, paging, ct), rows => Results.Ok(paging.ToPage(rows)));
        });

        api.MapPost("/projects/{projectId:guid}/tasks", async (Guid projectId, CreateTaskRequest request, UserContext user, TaskService service, CancellationToken ct) =>
            ApiResults.From(await service.CreateAsync(user, projectId, request, ct), task => Results.Created($"/api/v1/tasks/{task.Id}", task)));

        api.MapGet("/tasks/{id:guid}", async (Guid id, UserContext user, TaskService service, CancellationToken ct) =>
            ApiResults.Ok(await service.GetAsync(user, id, ct)));

        api.MapPatch("/tasks/{id:guid}", async (Guid id, PatchDocument patch, UserContext user, TaskService service, CancellationToken ct) =>
            ApiResults.Ok(await service.UpdateAsync(user, id, patch, ct)));

        api.MapDelete("/tasks/{id:guid}", async (Guid id, UserContext user, TaskService service, CancellationToken ct) =>
            ApiResults.NoContent(await service.DeleteAsync(user, id, ct)));

        return api.MapTaskTransferEndpoints();
    }
}
