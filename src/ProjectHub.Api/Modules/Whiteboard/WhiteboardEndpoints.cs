using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Modules.Identity;

namespace ProjectHub.Api.Modules.Whiteboard;

public static class WhiteboardEndpoints
{
    public static IServiceCollection AddWhiteboardModule(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var options = new WhiteboardOptions
        {
            SnapshotDirectory = configuration[WhiteboardOptions.DirectoryKey] ?? Path.Combine(environment.ContentRootPath, ".data", "whiteboards"),
            CompactionInterval = TimeSpan.FromSeconds(configuration.GetValue(WhiteboardOptions.CompactionIntervalKey, 5.0)),
            QuietPeriod = TimeSpan.FromSeconds(configuration.GetValue(WhiteboardOptions.QuietPeriodKey, 5.0)),
        };

        services.AddSignalR().AddHubOptions<WhiteboardsHub>(hub => hub.MaximumReceiveMessageSize = WhiteboardsHub.MaximumReceiveMessageSize(options));
        return services
            .AddSingleton(options)
            .AddSingleton<IWhiteboardSnapshotStorage, LocalWhiteboardSnapshotStorage>()
            .AddSingleton<WhiteboardEngine>()
            .AddScoped<WhiteboardDocumentStore>()
            .AddScoped<WhiteboardService>()
            .AddHostedService<WhiteboardCompactionService>();
    }

    public static RouteGroupBuilder MapWhiteboardEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/projects/{projectId:guid}/whiteboards", async (
                Guid projectId, int? limit, int? offset, UserContext user, WhiteboardService service, CancellationToken ct) =>
            Paging.TryCreate(limit, offset, out var paging, out var error)
                ? ApiResults.Ok(await service.ListAsync(user, projectId, paging, ct))
                : error!);

        api.MapPost("/projects/{projectId:guid}/whiteboards", async (
                Guid projectId, CreateWhiteboardRequest request, UserContext user, WhiteboardService service, CancellationToken ct) =>
            ApiResults.From(await service.CreateAsync(user, projectId, request, ct),
                board => Results.Created($"/api/v1/whiteboards/{board.Id}", board)));

        api.MapGet("/whiteboards/{id:guid}", async (Guid id, UserContext user, WhiteboardService service, CancellationToken ct) =>
            ApiResults.Ok(await service.GetAsync(user, id, ct)));

        api.MapPatch("/whiteboards/{id:guid}", async (Guid id, PatchDocument patch, UserContext user, WhiteboardService service, CancellationToken ct) =>
            ApiResults.Ok(await service.UpdateAsync(user, id, patch, ct)));

        api.MapDelete("/whiteboards/{id:guid}", async (Guid id, UserContext user, WhiteboardService service, CancellationToken ct) =>
            ApiResults.NoContent(await service.DeleteAsync(user, id, ct)));

        api.MapGet("/whiteboards/{id:guid}/tasks", async (Guid id, string? ids, UserContext user, WhiteboardService service, CancellationToken ct) =>
            ApiResults.Ok(await service.TasksAsync(user, id, ids, ct)));

        api.MapGet("/tasks/{taskId:guid}/whiteboards", async (Guid taskId, UserContext user, WhiteboardService service, CancellationToken ct) =>
            ApiResults.Ok(await service.WhiteboardsOfTaskAsync(user, taskId, ct)));

        return api;
    }

    public static IEndpointRouteBuilder MapWhiteboardHub(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHub<WhiteboardsHub>(WhiteboardsHub.Path);
        return endpoints;
    }
}
