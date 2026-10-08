using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Modules.Identity;

namespace ProjectHub.Api.Modules.Knowledge;

public static class KnowledgeEndpoints
{
    public static IServiceCollection AddKnowledgeModule(this IServiceCollection services) =>
        services
            .AddScoped<KnowledgeAccess>()
            .AddScoped<KnowledgeArticleService>()
            .AddScoped<KnowledgeLinkService>()
            .AddScoped<KnowledgeCommentService>()
            .AddScoped<KnowledgeSpaceService>()
            .AddScoped<KnowledgeGraphService>()
            .AddScoped<IKnowledgeSearch, PostgresKnowledgeSearch>()
            .AddScoped<IKnowledgeRetrieval, PostgresKnowledgeRetrieval>();

    public static RouteGroupBuilder MapKnowledgeEndpoints(this RouteGroupBuilder api)
    {
        var knowledge = api.MapGroup("/knowledge");

        knowledge.MapGet("/spaces", async (UserContext user, KnowledgeSpaceService service, Guid? departmentId, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(user, ct, departmentId)));

        knowledge.MapPost("/spaces", async (CreateSpaceRequest request, UserContext user, KnowledgeSpaceService service, CancellationToken ct) =>
            ApiResults.From(await service.CreateAsync(user, request, ct), space => Results.Created($"/api/v1/knowledge/spaces/{space.Id}", space)));

        knowledge.MapPatch("/spaces/{id:guid}", async (Guid id, PatchDocument patch, UserContext user, KnowledgeSpaceService service, CancellationToken ct) =>
            ApiResults.Ok(await service.UpdateAsync(user, id, patch, ct)));

        knowledge.MapGet("/articles", async (
            UserContext user, KnowledgeAccess access, IKnowledgeSearch search,
            string? q, string? type, string? status, Guid? spaceId, string? tag, Guid? departmentId, int? limit, int? offset, CancellationToken ct) =>
        {
            if (!Paging.TryCreate(limit, offset, out var paging, out var error))
            {
                return error!;
            }

            var reader = await access.ReaderAsync(user, ct);
            var rows = await search.SearchAsync(reader, new KnowledgeQuery(q, type, status, spaceId, tag, departmentId), paging, ct);
            return Results.Ok(paging.ToPage(rows));
        });

        knowledge.MapGet("/graph", async (UserContext user, KnowledgeGraphService service, Guid? spaceId, string? type, Guid? departmentId, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(user, spaceId, type, ct, departmentId)));

        knowledge.MapPost("/articles", async (CreateArticleRequest request, UserContext user, KnowledgeArticleService service, CancellationToken ct) =>
            ApiResults.From(await service.CreateAsync(user, request, ct), article => Results.Created($"/api/v1/knowledge/articles/{article.Article.Id}", article)));

        knowledge.MapGet("/articles/{id:guid}", async (Guid id, UserContext user, KnowledgeArticleService service, CancellationToken ct) =>
            ApiResults.Ok(await service.GetAsync(user, id, ct)));

        knowledge.MapPatch("/articles/{id:guid}", async (Guid id, PatchDocument patch, UserContext user, KnowledgeArticleService service, CancellationToken ct) =>
            ApiResults.Ok(await service.UpdateAsync(user, id, patch, ct)));

        knowledge.MapDelete("/articles/{id:guid}", async (Guid id, UserContext user, KnowledgeArticleService service, CancellationToken ct) =>
            ApiResults.NoContent(await service.DeleteAsync(user, id, ct)));

        knowledge.MapPut("/articles/{id:guid}/content", async (Guid id, SaveContentRequest request, UserContext user, KnowledgeArticleService service, CancellationToken ct) =>
            ApiResults.Ok(await service.SaveContentAsync(user, id, request, ct)));

        knowledge.MapPost("/articles/{id:guid}/status", async (Guid id, ChangeStatusRequest request, UserContext user, KnowledgeArticleService service, CancellationToken ct) =>
            ApiResults.Ok(await service.ChangeStatusAsync(user, id, request, ct)));

        knowledge.MapGet("/articles/{id:guid}/versions", async (Guid id, UserContext user, KnowledgeArticleService service, CancellationToken ct) =>
            ApiResults.Ok(await service.ListVersionsAsync(user, id, ct)));

        knowledge.MapGet("/articles/{id:guid}/versions/{number:int}", async (Guid id, int number, UserContext user, KnowledgeArticleService service, CancellationToken ct) =>
            ApiResults.Ok(await service.GetVersionAsync(user, id, number, ct)));

        knowledge.MapPost("/articles/{id:guid}/versions/{number:int}/restore", async (
                Guid id, int number, RestoreVersionRequest request, UserContext user, KnowledgeArticleService service, CancellationToken ct) =>
            ApiResults.Ok(await service.RestoreVersionAsync(user, id, number, request, ct)));

        knowledge.MapPut("/articles/{id:guid}/tags", async (Guid id, SetTagsRequest request, UserContext user, KnowledgeArticleService service, CancellationToken ct) =>
            ApiResults.Ok(await service.SetTagsAsync(user, id, request, ct)));

        knowledge.MapGet("/tags", async (UserContext user, KnowledgeArticleService service, CancellationToken ct) =>
            Results.Ok(await service.ListTagsAsync(user, ct)));

        knowledge.MapGet("/articles/{id:guid}/permissions", async (Guid id, UserContext user, KnowledgeArticleService service, CancellationToken ct) =>
            ApiResults.Ok(await service.ListPermissionsAsync(user, id, ct)));

        knowledge.MapPut("/articles/{id:guid}/permissions", async (
                Guid id, SetPermissionsRequest request, UserContext user, KnowledgeArticleService service, CancellationToken ct) =>
            ApiResults.Ok(await service.SetPermissionsAsync(user, id, request, ct)));

        knowledge.MapPost("/articles/{id:guid}/relations", async (Guid id, CreateRelationRequest request, UserContext user, KnowledgeLinkService service, CancellationToken ct) =>
            ApiResults.From(await service.AddRelationAsync(user, id, request, ct), relation => Results.Created($"/api/v1/knowledge/relations/{relation.Id}", relation)));

        knowledge.MapDelete("/relations/{id:guid}", async (Guid id, UserContext user, KnowledgeLinkService service, CancellationToken ct) =>
            ApiResults.NoContent(await service.RemoveRelationAsync(user, id, ct)));

        knowledge.MapPost("/articles/{id:guid}/references", async (Guid id, CreateReferenceRequest request, UserContext user, KnowledgeLinkService service, CancellationToken ct) =>
            ApiResults.From(await service.AddReferenceAsync(user, id, request, ct), reference => Results.Created($"/api/v1/knowledge/references/{reference.Id}", reference)));

        knowledge.MapDelete("/references/{id:guid}", async (Guid id, UserContext user, KnowledgeLinkService service, CancellationToken ct) =>
            ApiResults.NoContent(await service.RemoveReferenceAsync(user, id, ct)));

        knowledge.MapGet("/articles/{id:guid}/comments", async (Guid id, UserContext user, KnowledgeCommentService service, int? limit, int? offset, CancellationToken ct) =>
            Paging.TryCreate(limit, offset, out var paging, out var error)
                ? ApiResults.From(await service.ListAsync(user, id, paging, ct), rows => Results.Ok(paging.ToPage(rows)))
                : error!);

        knowledge.MapPost("/articles/{id:guid}/comments", async (
                Guid id, CreateKnowledgeCommentRequest request, UserContext user, KnowledgeCommentService service, CancellationToken ct) =>
            ApiResults.From(await service.AddAsync(user, id, request, ct), comment => Results.Created($"/api/v1/knowledge/comments/{comment.Id}", comment)));

        knowledge.MapPatch("/comments/{id:guid}", async (Guid id, UpdateKnowledgeCommentRequest request, UserContext user, KnowledgeCommentService service, CancellationToken ct) =>
            ApiResults.Ok(await service.UpdateAsync(user, id, request, ct)));

        knowledge.MapDelete("/comments/{id:guid}", async (Guid id, UserContext user, KnowledgeCommentService service, CancellationToken ct) =>
            ApiResults.NoContent(await service.DeleteAsync(user, id, ct)));

        // The other direction of references: knowledge linked to a project, task, department or whiteboard.
        api.MapGet("/projects/{id:guid}/knowledge", async (Guid id, UserContext user, KnowledgeLinkService service, CancellationToken ct) =>
            ApiResults.Ok(await service.ArticlesReferencingAsync(user, KnowledgeResourceType.Project, id, ct)));

        api.MapGet("/tasks/{id:guid}/knowledge", async (Guid id, UserContext user, KnowledgeLinkService service, CancellationToken ct) =>
            ApiResults.Ok(await service.ArticlesReferencingAsync(user, KnowledgeResourceType.Task, id, ct)));

        api.MapGet("/departments/{id:guid}/knowledge", async (Guid id, UserContext user, KnowledgeLinkService service, CancellationToken ct) =>
            ApiResults.Ok(await service.ArticlesReferencingAsync(user, KnowledgeResourceType.Department, id, ct)));

        api.MapGet("/whiteboards/{id:guid}/knowledge", async (Guid id, UserContext user, KnowledgeLinkService service, CancellationToken ct) =>
            ApiResults.Ok(await service.ArticlesReferencingAsync(user, KnowledgeResourceType.Whiteboard, id, ct)));

        return api;
    }
}
