using Microsoft.EntityFrameworkCore;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Infrastructure.Outcomes;
using ProjectHub.Api.Modules.Audit;
using ProjectHub.Api.Modules.Identity;
using ProjectHub.Api.Modules.Identity.Authorization;
using ProjectHub.Api.Modules.Projects;
using ProjectHub.Api.Modules.Tasks;
using ProjectHub.Api.Modules.Users;

namespace ProjectHub.Api.Modules.Attachments;

public sealed record AttachmentResponse(
    Guid Id, Guid TaskId, string FileName, string ContentType, long SizeBytes, Guid UploadedBy, string UploadedByName, DateTimeOffset CreatedAt);

public sealed record AttachmentContent(Stream Content, string FileName, string ContentType);

public sealed class AttachmentService(
    ProjectHubDbContext db,
    TaskAccess tasks,
    IProjectHubAuthorization authorization,
    IAttachmentStorage storage,
    AttachmentOptions options,
    IActivityLog activity,
    TimeProvider clock)
{
    public const int MaxFileNameLength = 255;

    /// <summary>Upper bound per task; attachment lists stay bounded.</summary>
    public const int MaxAttachmentsPerTask = 200;

    public async Task<ServiceResult<IReadOnlyList<AttachmentResponse>>> ListAsync(UserContext user, Guid taskId, CancellationToken ct)
    {
        if (await tasks.RequireAsync(user, taskId, ProjectPermission.View, ct) is { Failure: { } failure })
        {
            return failure;
        }

        var attachments = db.Set<TaskAttachment>().AsNoTracking()
            .Where(a => a.TaskId == taskId && a.OrganizationId == user.OrganizationId)
            .OrderBy(a => a.CreatedAt).ThenBy(a => a.Id)
            .Take(MaxAttachmentsPerTask);
        return await Project(attachments).ToListAsync(ct);
    }

    public async Task<ServiceResult<AttachmentResponse>> UploadAsync(UserContext user, Guid taskId, IFormFile? file, CancellationToken ct)
    {
        var (projectId, failure) = await tasks.RequireAsync(user, taskId, ProjectPermission.Contribute, ct);
        if (failure is not null)
        {
            return failure;
        }

        if (file is null || file.Length == 0)
        {
            return ServiceFailure.Invalid("file", "Required.");
        }

        if (file.Length > options.MaxSizeBytes)
        {
            return ServiceFailure.Invalid("file", $"At most {options.MaxSizeBytes / (1024 * 1024)} MB.");
        }

        var fileName = Path.GetFileName(file.FileName ?? string.Empty).Trim();
        if (fileName.Length is 0 or > MaxFileNameLength)
        {
            return ServiceFailure.Invalid("file", $"File name required, at most {MaxFileNameLength} characters.");
        }

        if (await db.Set<TaskAttachment>().CountAsync(a => a.TaskId == taskId, ct) >= MaxAttachmentsPerTask)
        {
            return ServiceFailure.Conflict($"A task can have at most {MaxAttachmentsPerTask} attachments.");
        }

        var attachmentId = Guid.CreateVersion7();
        var attachment = new TaskAttachment
        {
            Id = attachmentId,
            OrganizationId = user.OrganizationId,
            TaskId = taskId,
            FileName = fileName,
            ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            SizeBytes = file.Length,
            StorageKey = $"{user.OrganizationId:N}/{attachmentId:N}",
            UploadedBy = user.UserId,
            CreatedAt = clock.GetUtcNow(),
        };

        await using (var stream = file.OpenReadStream())
        {
            await storage.SaveAsync(attachment.StorageKey, stream, ct);
        }

        db.Set<TaskAttachment>().Add(attachment);
        activity.Record(user, projectId, ActivityActions.AttachmentAdded, "task_attachment", attachment.Id, new { TaskId = taskId, attachment.SizeBytes });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            await storage.DeleteAsync(attachment.StorageKey, CancellationToken.None);
            throw;
        }

        return await Project(db.Set<TaskAttachment>().AsNoTracking().Where(a => a.Id == attachment.Id)).SingleAsync(ct);
    }

    public async Task<ServiceResult<AttachmentContent>> DownloadAsync(UserContext user, Guid attachmentId, CancellationToken ct)
    {
        var attachment = await FindVisibleAsync(user, attachmentId, ct);
        if (attachment is null)
        {
            return ServiceFailure.NotFound("Attachment");
        }

        var stream = await storage.OpenReadAsync(attachment.StorageKey, ct);
        return stream is null
            ? ServiceFailure.NotFound("Attachment")
            : new AttachmentContent(stream, attachment.FileName, attachment.ContentType);
    }

    /// <summary>The uploader or a project manager deletes an attachment.</summary>
    public async Task<ServiceResult<Done>> DeleteAsync(UserContext user, Guid attachmentId, CancellationToken ct)
    {
        var attachment = await FindVisibleAsync(user, attachmentId, ct);
        if (attachment is null)
        {
            return ServiceFailure.NotFound("Attachment");
        }

        var projectId = await db.Set<ProjectTask>().Where(t => t.Id == attachment.TaskId).Select(t => t.ProjectId).SingleAsync(ct);
        if (attachment.UploadedBy != user.UserId && !await authorization.CanManageProjectAsync(user, projectId, ct))
        {
            return ServiceFailure.Forbidden("Only the uploader or a project admin can delete an attachment.");
        }

        db.Set<TaskAttachment>().Remove(attachment);
        activity.Record(user, projectId, ActivityActions.AttachmentDeleted, "task_attachment", attachment.Id, new { attachment.TaskId });
        await db.SaveChangesAsync(ct);
        await storage.DeleteAsync(attachment.StorageKey, ct);
        return Done.Value;
    }

    private async Task<TaskAttachment?> FindVisibleAsync(UserContext user, Guid attachmentId, CancellationToken ct)
    {
        var attachment = await db.Set<TaskAttachment>().SingleOrDefaultAsync(a => a.Id == attachmentId && a.OrganizationId == user.OrganizationId, ct);
        if (attachment is null)
        {
            return null;
        }

        return await tasks.RequireAsync(user, attachment.TaskId, ProjectPermission.View, ct) is { Failure: null } ? attachment : null;
    }

    private IQueryable<AttachmentResponse> Project(IQueryable<TaskAttachment> attachments) =>
        from attachment in attachments
        join uploader in db.Set<AppUser>() on attachment.UploadedBy equals uploader.Id
        select new AttachmentResponse(
            attachment.Id, attachment.TaskId, attachment.FileName, attachment.ContentType, attachment.SizeBytes,
            attachment.UploadedBy, uploader.DisplayName, attachment.CreatedAt);
}
