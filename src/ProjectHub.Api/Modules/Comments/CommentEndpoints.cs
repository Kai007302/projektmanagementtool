using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Modules.Identity;

namespace ProjectHub.Api.Modules.Comments;

public static class CommentEndpoints
{
    public static IServiceCollection AddCommentsModule(this IServiceCollection services) =>
        services.AddScoped<CommentService>();

    public static RouteGroupBuilder MapCommentEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/tasks/{taskId:guid}/comments", async (Guid taskId, UserContext user, CommentService service, int? limit, int? offset, CancellationToken ct) =>
            Paging.TryCreate(limit, offset, out var paging, out var error)
                ? ApiResults.From(await service.ListAsync(user, taskId, paging, ct), rows => Results.Ok(paging.ToPage(rows)))
                : error!);

        api.MapPost("/tasks/{taskId:guid}/comments", async (Guid taskId, CreateCommentRequest request, UserContext user, CommentService service, CancellationToken ct) =>
            ApiResults.From(await service.AddAsync(user, taskId, request, ct), comment => Results.Created($"/api/v1/comments/{comment.Id}", comment)));

        api.MapPatch("/comments/{id:guid}", async (Guid id, UpdateCommentRequest request, UserContext user, CommentService service, CancellationToken ct) =>
            ApiResults.Ok(await service.UpdateAsync(user, id, request, ct)));

        api.MapDelete("/comments/{id:guid}", async (Guid id, UserContext user, CommentService service, CancellationToken ct) =>
            ApiResults.NoContent(await service.DeleteAsync(user, id, ct)));

        return api;
    }
}
