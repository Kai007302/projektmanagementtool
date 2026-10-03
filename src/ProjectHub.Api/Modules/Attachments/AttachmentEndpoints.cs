using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Modules.Identity;

namespace ProjectHub.Api.Modules.Attachments;

public static class AttachmentEndpoints
{
    public static IServiceCollection AddAttachmentsModule(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var options = new AttachmentOptions
        {
            Directory = configuration[AttachmentOptions.DirectoryKey] ?? Path.Combine(environment.ContentRootPath, ".data", "attachments"),
            MaxSizeBytes = configuration.GetValue(AttachmentOptions.MaxSizeKey, AttachmentOptions.DefaultMaxSizeBytes),
        };

        return services
            .AddSingleton(options)
            .AddSingleton<IAttachmentStorage, LocalAttachmentStorage>()
            .AddScoped<AttachmentService>();
    }

    public static RouteGroupBuilder MapAttachmentEndpoints(this RouteGroupBuilder api, AttachmentOptions options)
    {
        api.MapGet("/tasks/{taskId:guid}/attachments", async (Guid taskId, UserContext user, AttachmentService service, CancellationToken ct) =>
            ApiResults.Ok(await service.ListAsync(user, taskId, ct)));

        // Bearer-token API without cookies, so no antiforgery token is needed.
        api.MapPost("/tasks/{taskId:guid}/attachments", async (Guid taskId, IFormFile? file, UserContext user, AttachmentService service, CancellationToken ct) =>
                ApiResults.From(await service.UploadAsync(user, taskId, file, ct), a => Results.Created($"/api/v1/attachments/{a.Id}", a)))
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttributeMetadata(options.MaxSizeBytes + 1024 * 1024));

        api.MapGet("/attachments/{id:guid}/content", async (Guid id, UserContext user, AttachmentService service, HttpContext http, CancellationToken ct) =>
            ApiResults.From(await service.DownloadAsync(user, id, ct), content =>
            {
                // Always a download, never rendered inline: uploaded content is untrusted.
                http.Response.Headers.XContentTypeOptions = "nosniff";
                return Results.File(content.Content, content.ContentType, content.FileName);
            }));

        api.MapDelete("/attachments/{id:guid}", async (Guid id, UserContext user, AttachmentService service, CancellationToken ct) =>
            ApiResults.NoContent(await service.DeleteAsync(user, id, ct)));

        return api;
    }

    private sealed class RequestSizeLimitAttributeMetadata(long maxRequestBodySize) : Microsoft.AspNetCore.Http.Metadata.IRequestSizeLimitMetadata
    {
        public long? MaxRequestBodySize { get; } = maxRequestBodySize;
    }
}
