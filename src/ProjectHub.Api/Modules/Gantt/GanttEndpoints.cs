using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Modules.Identity;

namespace ProjectHub.Api.Modules.Gantt;

public static class GanttEndpoints
{
    public static IServiceCollection AddGanttModule(this IServiceCollection services) =>
        services.AddScoped<GanttService>();

    public static RouteGroupBuilder MapGanttEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/projects/{projectId:guid}/gantt", async (Guid projectId, UserContext user, GanttService service, CancellationToken ct) =>
            ApiResults.Ok(await service.GetAsync(user, projectId, ct)));

        api.MapGet("/projects/{projectId:guid}/gantt/export.pdf", async (Guid projectId, UserContext user, GanttService service, CancellationToken ct) =>
            ApiResults.From(await service.ExportPdfAsync(user, projectId, ct), file => Results.File(file.Content, "application/pdf", file.FileName)));

        api.MapPost("/projects/{projectId:guid}/gantt/dependencies", async (
                Guid projectId, CreateDependencyRequest request, UserContext user, GanttService service, CancellationToken ct) =>
            ApiResults.From(await service.CreateDependencyAsync(user, projectId, request, ct),
                dependency => Results.Created($"/api/v1/task-dependencies/{dependency.Id}", dependency)));

        api.MapDelete("/task-dependencies/{id:guid}", async (Guid id, UserContext user, GanttService service, CancellationToken ct) =>
            ApiResults.NoContent(await service.DeleteDependencyAsync(user, id, ct)));

        api.MapPost("/projects/{projectId:guid}/gantt/milestones", async (
                Guid projectId, CreateMilestoneRequest request, UserContext user, GanttService service, CancellationToken ct) =>
            ApiResults.From(await service.CreateMilestoneAsync(user, projectId, request, ct),
                milestone => Results.Created($"/api/v1/gantt-milestones/{milestone.Id}", milestone)));

        api.MapPatch("/gantt-milestones/{id:guid}", async (Guid id, PatchDocument patch, UserContext user, GanttService service, CancellationToken ct) =>
            ApiResults.Ok(await service.UpdateMilestoneAsync(user, id, patch, ct)));

        api.MapDelete("/gantt-milestones/{id:guid}", async (Guid id, UserContext user, GanttService service, CancellationToken ct) =>
            ApiResults.NoContent(await service.DeleteMilestoneAsync(user, id, ct)));

        return api;
    }
}
