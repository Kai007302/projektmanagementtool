using Microsoft.EntityFrameworkCore;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Infrastructure.Outcomes;
using ProjectHub.Api.Modules.Audit;
using ProjectHub.Api.Modules.Identity;
using ProjectHub.Api.Modules.Identity.Authorization;

namespace ProjectHub.Api.Modules.Projects;

public sealed record ProjectLogoUpdated(long? LogoVersion);

public sealed record ProjectLogoContent(byte[] Content, string ContentType);

/// <summary>
/// The logo of a project (ADR 0020): editors upload a small PNG, JPEG or WebP image, everyone who can see the project can
/// load it. The type is taken from the file's first bytes, never from what the browser claims, and SVG is not accepted
/// because it can carry script.
/// </summary>
public sealed class ProjectLogoService(ProjectHubDbContext db, ProjectAccess access, IActivityLog activity, TimeProvider clock)
{
    public const int MaxSizeBytes = 256 * 1024;

    public async Task<ServiceResult<ProjectLogoContent>> GetAsync(UserContext user, Guid projectId, CancellationToken ct)
    {
        if (await access.RequireAsync(user, projectId, ProjectPermission.View, ct) is { } failure)
        {
            return failure;
        }

        var logo = await db.Set<ProjectLogo>().AsNoTracking()
            .SingleOrDefaultAsync(l => l.ProjectId == projectId && l.OrganizationId == user.OrganizationId, ct);
        return logo is null ? ServiceFailure.NotFound("Logo") : new ProjectLogoContent(logo.Content, logo.ContentType);
    }

    public async Task<ServiceResult<ProjectLogoUpdated>> UploadAsync(UserContext user, Guid projectId, IFormFile? file, CancellationToken ct)
    {
        if (await access.RequireAsync(user, projectId, ProjectPermission.Edit, ct) is { } failure)
        {
            return failure;
        }

        if (file is null || file.Length == 0)
        {
            return ServiceFailure.Invalid("file", "Required.");
        }

        if (file.Length > MaxSizeBytes)
        {
            return ServiceFailure.Invalid("file", $"At most {MaxSizeBytes / 1024} KB.");
        }

        byte[] content;
        using (var buffer = new MemoryStream((int)file.Length))
        {
            await file.CopyToAsync(buffer, ct);
            content = buffer.ToArray();
        }

        if (ImageType(content) is not { } contentType)
        {
            return ServiceFailure.Invalid("file", "PNG, JPEG or WebP image required.");
        }

        var now = clock.GetUtcNow();
        var logo = await db.Set<ProjectLogo>().SingleOrDefaultAsync(l => l.ProjectId == projectId && l.OrganizationId == user.OrganizationId, ct);
        if (logo is null)
        {
            db.Set<ProjectLogo>().Add(new ProjectLogo
            {
                ProjectId = projectId, OrganizationId = user.OrganizationId, Content = content, ContentType = contentType, UpdatedAt = now,
            });
        }
        else
        {
            logo.Content = content;
            logo.ContentType = contentType;
            logo.UpdatedAt = now;
        }

        var version = now.ToUnixTimeMilliseconds();
        activity.Record(user, projectId, ActivityActions.ProjectUpdated, "project", projectId, new { Fields = new[] { "logo" } });
        await db.SaveChangesAsync(ct);
        await SetVersionAsync(projectId, version, ct);
        return new ProjectLogoUpdated(version);
    }

    public async Task<ServiceResult<ProjectLogoUpdated>> DeleteAsync(UserContext user, Guid projectId, CancellationToken ct)
    {
        if (await access.RequireAsync(user, projectId, ProjectPermission.Edit, ct) is { } failure)
        {
            return failure;
        }

        var logo = await db.Set<ProjectLogo>().SingleOrDefaultAsync(l => l.ProjectId == projectId && l.OrganizationId == user.OrganizationId, ct);
        if (logo is null)
        {
            return ServiceFailure.NotFound("Logo");
        }

        db.Set<ProjectLogo>().Remove(logo);
        activity.Record(user, projectId, ActivityActions.ProjectUpdated, "project", projectId, new { Fields = new[] { "logo" } });
        await db.SaveChangesAsync(ct);
        await SetVersionAsync(projectId, null, ct);
        return new ProjectLogoUpdated(null);
    }

    /// <summary>The logo is not part of the project's editable fields, so it does not bump the project version.</summary>
    private Task<int> SetVersionAsync(Guid projectId, long? version, CancellationToken ct) =>
        db.Set<Project>().Where(p => p.Id == projectId).ExecuteUpdateAsync(set => set.SetProperty(p => p.LogoVersion, version), ct);

    /// <summary>The image type from the file's signature, or null for anything else.</summary>
    public static string? ImageType(ReadOnlySpan<byte> content)
    {
        if (content.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return "image/png";
        }

        if (content.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
        {
            return "image/jpeg";
        }

        if (content.Length >= 12 && content[..4].SequenceEqual("RIFF"u8) && content[8..12].SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }
}
