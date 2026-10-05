using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Modules.Identity;

namespace ProjectHub.Api.Modules.Tasks.Transfer;

public static class TaskTransferEndpoints
{
    public static RouteGroupBuilder MapTaskTransferEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/projects/{projectId:guid}/tasks/export", async (
            Guid projectId, string? format, UserContext user, TaskTransferService service, CancellationToken ct) =>
        {
            if (!TryParseFormat(format, out var fileFormat))
            {
                return ApiResults.Validation("format", "Must be 'csv' or 'xlsx'.");
            }

            return ApiResults.From(await service.ExportAsync(user, projectId, fileFormat, ct), file => Results.File(file.Content, file.ContentType, file.FileName));
        });

        // Bearer-token API without cookies, so no antiforgery token is needed.
        api.MapPost("/projects/{projectId:guid}/tasks/import", async (
                Guid projectId, IFormFile? file, bool? dryRun, UserContext user, TaskTransferService service, CancellationToken ct) =>
            {
                if (file is null || file.Length == 0)
                {
                    return ApiResults.Validation("file", "A non-empty file is required.");
                }

                if (file.Length > TaskTransferService.MaxImportBytes)
                {
                    return ApiResults.Validation("file", $"At most {TaskTransferService.MaxImportBytes / 1024 / 1024} MB.");
                }

                using var buffer = new MemoryStream((int)file.Length);
                await file.CopyToAsync(buffer, ct);
                return ApiResults.Ok(await service.ImportAsync(user, projectId, file.FileName, buffer.ToArray(), dryRun ?? false, ct));
            })
            .DisableAntiforgery()
            .RequireRateLimiting(HttpHardening.UploadPolicy);

        return api;
    }

    private static bool TryParseFormat(string? format, out TaskFileFormat fileFormat)
    {
        fileFormat = TaskFileFormat.Csv;
        switch (format?.ToLowerInvariant())
        {
            case null or "csv":
                return true;
            case "xlsx":
                fileFormat = TaskFileFormat.Xlsx;
                return true;
            default:
                return false;
        }
    }
}
