using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Modules.Identity;

namespace ProjectHub.Api.Modules.Kanban;

public static class KanbanEndpoints
{
    public static IServiceCollection AddKanbanModule(this IServiceCollection services) =>
        services.AddScoped<KanbanService>();

    public static RouteGroupBuilder MapKanbanEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/projects/{projectId:guid}/board", async (Guid projectId, UserContext user, KanbanService service, CancellationToken ct) =>
            ApiResults.Ok(await service.GetAsync(user, projectId, ct)));

        api.MapPost("/projects/{projectId:guid}/board/columns", async (
                Guid projectId, CreateColumnRequest request, UserContext user, KanbanService service, CancellationToken ct) =>
            ApiResults.From(await service.CreateColumnAsync(user, projectId, request, ct), board => Results.Created($"/api/v1/projects/{projectId}/board", board)));

        api.MapPatch("/board-columns/{id:guid}", async (Guid id, PatchDocument patch, UserContext user, KanbanService service, CancellationToken ct) =>
            ApiResults.Ok(await service.UpdateColumnAsync(user, id, patch, ct)));

        api.MapPost("/board-columns/{id:guid}/move", async (Guid id, MoveColumnRequest request, UserContext user, KanbanService service, CancellationToken ct) =>
            ApiResults.Ok(await service.MoveColumnAsync(user, id, request, ct)));

        api.MapDelete("/board-columns/{id:guid}", async (Guid id, UserContext user, KanbanService service, CancellationToken ct) =>
            ApiResults.NoContent(await service.DeleteColumnAsync(user, id, ct)));

        api.MapPost("/tasks/{id:guid}/move", async (Guid id, MoveTaskRequest request, UserContext user, KanbanService service, CancellationToken ct) =>
            ApiResults.Ok(await service.MoveTaskAsync(user, id, request, ct)));

        return api;
    }
}
