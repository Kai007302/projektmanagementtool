using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Infrastructure.Outcomes;
using ProjectHub.Api.Modules.Audit;
using ProjectHub.Api.Modules.Identity;
using ProjectHub.Api.Modules.Identity.Authorization;
using ProjectHub.Api.Modules.Projects;
using ProjectHub.Api.Modules.Tasks;
using ProjectHub.Api.Modules.Users;

namespace ProjectHub.Api.Modules.Knowledge;

public sealed record ArticleDetails(
    ArticleSummary Article,
    JsonObject Content,
    int VersionNumber,
    DateOnly? ReviewDueAt,
    KnowledgeCapabilities Capabilities,
    IReadOnlyList<RelationResponse> Relations,
    IReadOnlyList<ReferenceResponse> References);

public sealed record RelationResponse(Guid Id, string RelationType, string Direction, Guid ArticleId, string Title, string ArticleType);

public sealed record ReferenceResponse(Guid Id, string ResourceType, Guid ResourceId, string Title, Guid? ProjectId);

public sealed record CreateArticleRequest(
    string? Title,
    string? ArticleType,
    string? Summary,
    Guid? SpaceId,
    string? Visibility,
    JsonObject? Content,
    Guid? DepartmentId = null);

public sealed record SaveContentRequest(long? Version, JsonObject? Content, string? ChangeNote);

public sealed record ChangeStatusRequest(long? Version, string? Status);

public sealed record RestoreVersionRequest(long? Version);

public sealed record VersionSummary(Guid Id, int VersionNumber, Guid CreatedBy, string? CreatedByName, string? ChangeNote, DateTimeOffset CreatedAt);

public sealed record VersionDetails(VersionSummary Version, JsonObject Content);

public sealed record SetTagsRequest(IReadOnlyList<string>? Tags);

public sealed record PermissionEntry(string? PrincipalType, Guid? PrincipalId, string? Permission);

public sealed record PermissionResponse(string PrincipalType, Guid PrincipalId, string Name, string Permission);

public sealed record SetPermissionsRequest(IReadOnlyList<PermissionEntry>? Permissions);

/// <summary>Articles: metadata, lifecycle, versioned block content, tags and explicit grants.</summary>
public sealed class KnowledgeArticleService(
    ProjectHubDbContext db,
    KnowledgeAccess access,
    KnowledgeLinkService links,
    IAuditLog audit,
    IProjectHubAuthorization authorization,
    TimeProvider clock)
{
    public const int MaxTitleLength = 300;
    public const int MaxSummaryLength = 2000;
    public const int MaxChangeNoteLength = 500;
    public const int MaxTags = 30;
    public const int MaxTagLength = 50;

    /// <summary>Allowed lifecycle steps and the right each needs.</summary>
    private static readonly Dictionary<(string From, string To), KnowledgeRight> Transitions = new()
    {
        [(KnowledgeStatus.Draft, KnowledgeStatus.Review)] = KnowledgeRight.Edit,
        [(KnowledgeStatus.Review, KnowledgeStatus.Draft)] = KnowledgeRight.Edit,
        [(KnowledgeStatus.Draft, KnowledgeStatus.Published)] = KnowledgeRight.Admin,
        [(KnowledgeStatus.Review, KnowledgeStatus.Published)] = KnowledgeRight.Admin,
        [(KnowledgeStatus.Published, KnowledgeStatus.Draft)] = KnowledgeRight.Admin,
        [(KnowledgeStatus.Draft, KnowledgeStatus.Archived)] = KnowledgeRight.Admin,
        [(KnowledgeStatus.Review, KnowledgeStatus.Archived)] = KnowledgeRight.Admin,
        [(KnowledgeStatus.Published, KnowledgeStatus.Archived)] = KnowledgeRight.Admin,
        [(KnowledgeStatus.Archived, KnowledgeStatus.Draft)] = KnowledgeRight.Admin,
    };

    public async Task<ServiceResult<ArticleDetails>> GetAsync(UserContext user, Guid articleId, CancellationToken ct)
    {
        var (article, reader, right, failure) = await access.RequireAsync(user, articleId, KnowledgeRight.View, ct);
        if (failure is not null)
        {
            return failure;
        }

        await access.RecordAdminAccessAsync(user, reader, article!, ct);
        await db.SaveChangesAsync(ct);
        return await DetailsAsync(user, reader, article!, right, ct);
    }

    /// <summary>
    /// Leads and members of a department create articles in it and become their owner; new articles start as drafts.
    /// Without a department the article goes to the space's department, else to the caller's first one (ADR 0021).
    /// </summary>
    public async Task<ServiceResult<ArticleDetails>> CreateAsync(UserContext user, CreateArticleRequest request, CancellationToken ct)
    {
        var departmentId = request.DepartmentId
                           ?? (request.SpaceId is { } requestedSpace
                               ? await db.Set<KnowledgeSpace>().Where(s => s.Id == requestedSpace && s.OrganizationId == user.OrganizationId)
                                   .Select(s => (Guid?)s.DepartmentId).SingleOrDefaultAsync(ct)
                               : null)
                           ?? await db.Set<Departments.DepartmentMember>()
                               .Where(m => m.OrganizationId == user.OrganizationId && m.UserId == user.UserId && DepartmentRole.Working.Contains(m.Role))
                               .OrderBy(m => m.CreatedAt).ThenBy(m => m.DepartmentId)
                               .Select(m => (Guid?)m.DepartmentId)
                               .FirstOrDefaultAsync(ct);
        if (departmentId is null)
        {
            return ServiceFailure.Invalid("departmentId", "Required: you belong to no department you can write articles in.");
        }

        if (!await authorization.CanCreateInDepartmentAsync(user, departmentId.Value, ct))
        {
            return await db.Set<Departments.Department>().AnyAsync(d => d.Id == departmentId && d.OrganizationId == user.OrganizationId, ct)
                ? ServiceFailure.Forbidden("Only leads and members of the department write articles in it.")
                : ServiceFailure.Invalid("departmentId", "Must be a department of the organization.");
        }

        var now = clock.GetUtcNow();
        var article = new KnowledgeArticle
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = user.OrganizationId,
            KnowledgeSpaceId = request.SpaceId,
            DepartmentId = departmentId.Value,
            Title = request.Title?.Trim() ?? string.Empty,
            Slug = string.Empty,
            ArticleType = request.ArticleType ?? "article",
            Summary = Normalize(request.Summary),
            OwnerId = user.UserId,
            Visibility = request.Visibility ?? KnowledgeVisibility.Department,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };

        if (await ValidateAsync(user, article, spaceChanged: true, ct) is { } invalid)
        {
            return invalid;
        }

        var content = BlockContent.Normalize(request.Content ?? BlockContent.Empty(), out var contentError);
        if (content is null)
        {
            return ServiceFailure.Invalid("content", contentError!);
        }

        var reader = await access.ReaderAsync(user, ct);
        if (article.Visibility == KnowledgeVisibility.Organization && !KnowledgeAccess.CanShareWithOrganization(reader, article.DepartmentId))
        {
            return ServiceFailure.Forbidden("Only the department's leads and organization admins share articles with the whole organization.");
        }

        if (await links.ValidateBlockReferencesAsync(user, reader, content.References, ct) is { } referenceError)
        {
            return referenceError;
        }

        article.Slug = await UniqueSlugAsync(user.OrganizationId, article.Title, null, ct);
        article.SearchText = content.PlainText;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.Set<KnowledgeArticle>().Add(article);
        audit.Record(user, AuditActions.KnowledgeArticleCreated, "knowledge_article", article.Id, new { article.Title, article.ArticleType });
        if (await SaveUniqueAsync(ct) is { } conflict)
        {
            return conflict;
        }

        AddVersion(user, article, content.Content, "Erstellt", 1, now);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return await DetailsAsync(user, reader, article, KnowledgeRight.Admin, ct);
    }

    /// <summary>Changes metadata; visibility needs Admin, everything else Edit.</summary>
    public async Task<ServiceResult<ArticleDetails>> UpdateAsync(UserContext user, Guid articleId, PatchDocument patch, CancellationToken ct)
    {
        var (article, reader, right, failure) = await access.RequireAsync(user, articleId, KnowledgeRight.Edit, ct);
        if (failure is not null)
        {
            return failure;
        }

        if (!patch.TryGetVersion(out var expectedVersion, out var versionError))
        {
            return versionError!;
        }

        if (article!.Version != expectedVersion)
        {
            return ServiceFailure.StaleVersion(article.Version);
        }

        var changed = new List<string>();
        if (!patch.TryApply<string>("title", v => article.Title = v?.Trim() ?? string.Empty, changed, out var error)
            || !patch.TryApply<string>("summary", v => article.Summary = Normalize(v), changed, out error)
            || !patch.TryApply<string>("articleType", v => article.ArticleType = v ?? string.Empty, changed, out error)
            || !patch.TryApply<Guid?>("spaceId", v => article.KnowledgeSpaceId = v, changed, out error)
            || !patch.TryApply<string>("visibility", v => article.Visibility = v ?? string.Empty, changed, out error)
            || !patch.TryApply<Guid?>("departmentId", v => article.DepartmentId = v ?? Guid.Empty, changed, out error)
            || !patch.TryApply<DateOnly?>("reviewDueAt", v => article.ReviewDueAt = v, changed, out error))
        {
            return error!;
        }

        if ((changed.Contains("visibility") || changed.Contains("departmentId")) && right < KnowledgeRight.Admin)
        {
            return ServiceFailure.Forbidden("Only article admins change the visibility or the department.");
        }

        if (changed.Contains("departmentId") && !await authorization.CanCreateInDepartmentAsync(user, article.DepartmentId, ct))
        {
            return await db.Set<Departments.Department>().AnyAsync(d => d.Id == article.DepartmentId && d.OrganizationId == user.OrganizationId, ct)
                ? ServiceFailure.Forbidden("Articles can only be moved to departments you work in.")
                : ServiceFailure.Invalid("departmentId", "Must be a department of the organization.");
        }

        if (changed.Contains("departmentId") && !changed.Contains("spaceId"))
        {
            // Spaces belong to one department; a moved article leaves its old one.
            article.KnowledgeSpaceId = null;
        }

        // Moving to another department, or (re)sharing with the organization, is decided by the target department.
        if ((changed.Contains("visibility") || changed.Contains("departmentId"))
            && article.Visibility == KnowledgeVisibility.Organization
            && !KnowledgeAccess.CanShareWithOrganization(reader, article.DepartmentId))
        {
            return ServiceFailure.Forbidden("Only the department's leads and organization admins share articles with the whole organization.");
        }

        if (await ValidateAsync(user, article, changed.Contains("spaceId") || changed.Contains("departmentId"), ct) is { } invalid)
        {
            return invalid;
        }

        if (changed.Count > 0)
        {
            if (changed.Contains("title"))
            {
                article.Slug = await UniqueSlugAsync(user.OrganizationId, article.Title, article.Id, ct);
            }

            db.Touch(article, expectedVersion, clock.GetUtcNow());
            if (changed.Contains("visibility") || changed.Contains("departmentId"))
            {
                audit.Record(user, AuditActions.KnowledgeVisibilityChanged, "knowledge_article", article.Id, new { article.Visibility, article.DepartmentId });
            }

            if (await SaveVersionedUniqueAsync(article, ct) is { } conflict)
            {
                return conflict;
            }
        }

        return await DetailsAsync(user, reader, article, right, ct);
    }

    /// <summary>Saves new content as the next version; older versions stay untouched.</summary>
    public async Task<ServiceResult<ArticleDetails>> SaveContentAsync(UserContext user, Guid articleId, SaveContentRequest request, CancellationToken ct)
    {
        var (article, reader, right, failure) = await access.RequireAsync(user, articleId, KnowledgeRight.Edit, ct);
        if (failure is not null)
        {
            return failure;
        }

        if (request.Version is not { } expectedVersion)
        {
            return ServiceFailure.Invalid("version", "Required: the version the change is based on.");
        }

        if (request.ChangeNote?.Length > MaxChangeNoteLength)
        {
            return ServiceFailure.Invalid("changeNote", $"At most {MaxChangeNoteLength} characters.");
        }

        var content = BlockContent.Normalize(request.Content, out var contentError);
        if (content is null)
        {
            return ServiceFailure.Invalid("content", contentError!);
        }

        if (article!.Version != expectedVersion)
        {
            return ServiceFailure.StaleVersion(article.Version);
        }

        if (await links.ValidateBlockReferencesAsync(user, reader, content.References, ct) is { } referenceError)
        {
            return referenceError;
        }

        return await StoreVersionAsync(user, reader, article, right, expectedVersion, content.Content, content.PlainText, Normalize(request.ChangeNote), ct);
    }

    public async Task<ServiceResult<ArticleDetails>> ChangeStatusAsync(UserContext user, Guid articleId, ChangeStatusRequest request, CancellationToken ct)
    {
        var (article, reader, right, failure) = await access.RequireAsync(user, articleId, KnowledgeRight.View, ct);
        if (failure is not null)
        {
            return failure;
        }

        if (request.Version is not { } expectedVersion)
        {
            return ServiceFailure.Invalid("version", "Required: the version the change is based on.");
        }

        if (request.Status is not { } status || !KnowledgeStatus.All.Contains(status))
        {
            return ServiceFailure.Invalid("status", $"Must be one of: {string.Join(", ", KnowledgeStatus.All)}.");
        }

        if (!Transitions.TryGetValue((article!.Status, status), out var needed))
        {
            return ServiceFailure.Invalid("status", $"An article cannot go from '{article.Status}' to '{status}'.");
        }

        if (right < needed)
        {
            return ServiceFailure.Forbidden($"Missing knowledge permission '{needed}'.");
        }

        if (article.Version != expectedVersion)
        {
            return ServiceFailure.StaleVersion(article.Version);
        }

        var now = clock.GetUtcNow();
        var previous = article.Status;
        article.Status = status;
        if (status == KnowledgeStatus.Published)
        {
            article.PublishedAt = now;
        }

        db.Touch(article, expectedVersion, now);
        audit.Record(user, AuditActions.KnowledgeStatusChanged, "knowledge_article", article.Id, new { From = previous, To = status });
        return await db.SaveVersionedAsync(article, ct) is { } conflict
            ? conflict
            : await DetailsAsync(user, reader, article, right, ct);
    }

    public async Task<ServiceResult<Done>> DeleteAsync(UserContext user, Guid articleId, CancellationToken ct)
    {
        var (article, _, _, failure) = await access.RequireAsync(user, articleId, KnowledgeRight.Admin, ct);
        if (failure is not null)
        {
            return failure;
        }

        var now = clock.GetUtcNow();
        article!.DeletedAt = now;
        db.Touch(article, article.Version, now);
        audit.Record(user, AuditActions.KnowledgeArticleDeleted, "knowledge_article", article.Id, new { article.Title });
        return await db.SaveVersionedAsync(article, ct) is { } conflict ? conflict : Done.Value;
    }

    public async Task<ServiceResult<IReadOnlyList<VersionSummary>>> ListVersionsAsync(UserContext user, Guid articleId, CancellationToken ct)
    {
        var (_, _, _, failure) = await access.RequireAsync(user, articleId, KnowledgeRight.View, ct);
        if (failure is not null)
        {
            return failure;
        }

        return await Versions(articleId).ToListAsync(ct);
    }

    public async Task<ServiceResult<VersionDetails>> GetVersionAsync(UserContext user, Guid articleId, int number, CancellationToken ct)
    {
        var (_, _, _, failure) = await access.RequireAsync(user, articleId, KnowledgeRight.View, ct);
        if (failure is not null)
        {
            return failure;
        }

        var summary = await Versions(articleId, number).SingleOrDefaultAsync(ct);
        if (summary is null)
        {
            return ServiceFailure.NotFound("Version");
        }

        var json = await db.Set<KnowledgeVersion>().Where(v => v.Id == summary.Id).Select(v => v.ContentJson).SingleAsync(ct);
        return new VersionDetails(summary, ParseContent(json));
    }

    /// <summary>Makes an older version current again by saving its content as a new version.</summary>
    public async Task<ServiceResult<ArticleDetails>> RestoreVersionAsync(
        UserContext user, Guid articleId, int number, RestoreVersionRequest request, CancellationToken ct)
    {
        var (article, reader, right, failure) = await access.RequireAsync(user, articleId, KnowledgeRight.Edit, ct);
        if (failure is not null)
        {
            return failure;
        }

        if (request.Version is not { } expectedVersion)
        {
            return ServiceFailure.Invalid("version", "Required: the version the change is based on.");
        }

        var json = await db.Set<KnowledgeVersion>()
            .Where(v => v.ArticleId == articleId && v.VersionNumber == number)
            .Select(v => v.ContentJson)
            .SingleOrDefaultAsync(ct);
        if (json is null)
        {
            return ServiceFailure.NotFound("Version");
        }

        if (article!.Version != expectedVersion)
        {
            return ServiceFailure.StaleVersion(article.Version);
        }

        return await StoreVersionAsync(user, reader, article, right, expectedVersion, ParseContent(json), BlockContent.PlainText(json), $"Wiederhergestellt aus Version {number}", ct);
    }

    /// <summary>Replaces the article's tags; unknown tags are created for the organization.</summary>
    public async Task<ServiceResult<ArticleDetails>> SetTagsAsync(UserContext user, Guid articleId, SetTagsRequest request, CancellationToken ct)
    {
        var (article, reader, right, failure) = await access.RequireAsync(user, articleId, KnowledgeRight.Edit, ct);
        if (failure is not null)
        {
            return failure;
        }

        var names = (request.Tags ?? []).Select(t => t?.Trim() ?? string.Empty).ToList();
        if (names.Count > MaxTags || names.Any(n => n.Length is 0 or > MaxTagLength))
        {
            return ServiceFailure.Invalid("tags", $"At most {MaxTags} tags of 1 to {MaxTagLength} characters.");
        }

        var wanted = names.DistinctBy(n => n.ToLowerInvariant()).ToList();
        var lower = wanted.Select(n => n.ToLowerInvariant()).ToList();
        var existing = await db.Set<KnowledgeTag>()
            .Where(t => t.OrganizationId == user.OrganizationId && lower.Contains(t.Name.ToLower()))
            .ToListAsync(ct);
        var now = clock.GetUtcNow();
        var tags = wanted.Select(name =>
            existing.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? Track(new KnowledgeTag { Id = Guid.CreateVersion7(), OrganizationId = user.OrganizationId, Name = name, CreatedAt = now })).ToList();

        await db.Set<KnowledgeArticleTag>().Where(t => t.ArticleId == articleId).ExecuteDeleteAsync(ct);
        db.Set<KnowledgeArticleTag>().AddRange(tags.Select(t => new KnowledgeArticleTag
        {
            OrganizationId = user.OrganizationId,
            ArticleId = articleId,
            TagId = t.Id,
        }));
        article!.UpdatedAt = now;
        if (await SaveUniqueAsync(ct) is { } conflict)
        {
            return conflict;
        }

        return await DetailsAsync(user, reader, article, right, ct);
    }

    /// <summary>All tags of the organization with the number of visible articles carrying them.</summary>
    public async Task<IReadOnlyList<TagResponse>> ListTagsAsync(UserContext user, CancellationToken ct)
    {
        var visible = access.Visible(await access.ReaderAsync(user, ct)).Select(a => a.Id);
        return await (
                from tag in db.Set<KnowledgeTag>().AsNoTracking()
                where tag.OrganizationId == user.OrganizationId
                let count = db.Set<KnowledgeArticleTag>().Count(at => at.TagId == tag.Id && visible.Contains(at.ArticleId))
                where count > 0
                orderby tag.Name
                select new TagResponse(tag.Name, count))
            .ToListAsync(ct);
    }

    public async Task<ServiceResult<IReadOnlyList<PermissionResponse>>> ListPermissionsAsync(UserContext user, Guid articleId, CancellationToken ct)
    {
        var (_, _, _, failure) = await access.RequireAsync(user, articleId, KnowledgeRight.Admin, ct);
        if (failure is not null)
        {
            return failure;
        }

        return await PermissionsOf(articleId, ct);
    }

    /// <summary>Replaces the explicit grants of an article. Principals must belong to the organization.</summary>
    public async Task<ServiceResult<IReadOnlyList<PermissionResponse>>> SetPermissionsAsync(
        UserContext user, Guid articleId, SetPermissionsRequest request, CancellationToken ct)
    {
        var (_, _, _, failure) = await access.RequireAsync(user, articleId, KnowledgeRight.Admin, ct);
        if (failure is not null)
        {
            return failure;
        }

        var entries = request.Permissions ?? [];
        if (entries.Count > 200)
        {
            return ServiceFailure.Invalid("permissions", "At most 200 entries.");
        }

        foreach (var entry in entries)
        {
            if (entry.PrincipalType is not (KnowledgePrincipal.User or KnowledgePrincipal.Department) || entry.PrincipalId is null
                || entry.Permission is null || !KnowledgeGrant.All.Contains(entry.Permission))
            {
                return ServiceFailure.Invalid("permissions", "Each entry needs principalType user|department, principalId and permission view|edit|admin.");
            }
        }

        if (entries.GroupBy(e => (e.PrincipalType, e.PrincipalId)).Any(g => g.Count() > 1))
        {
            return ServiceFailure.Invalid("permissions", "Each principal may appear only once.");
        }

        var userIds = entries.Where(e => e.PrincipalType == KnowledgePrincipal.User).Select(e => e.PrincipalId!.Value).ToList();
        var departmentIds = entries.Where(e => e.PrincipalType == KnowledgePrincipal.Department).Select(e => e.PrincipalId!.Value).ToList();
        var knownUsers = await db.Set<AppUser>().CountAsync(u => u.OrganizationId == user.OrganizationId && userIds.Contains(u.Id), ct);
        var knownDepartments = await db.Set<Departments.Department>().CountAsync(d => d.OrganizationId == user.OrganizationId && departmentIds.Contains(d.Id), ct);
        if (knownUsers != userIds.Count || knownDepartments != departmentIds.Count)
        {
            return ServiceFailure.Invalid("permissions", "Users and departments must belong to the organization.");
        }

        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Set<KnowledgePermission>().Where(p => p.ArticleId == articleId).ExecuteDeleteAsync(ct);
        db.Set<KnowledgePermission>().AddRange(entries.Select(e => new KnowledgePermission
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = user.OrganizationId,
            ArticleId = articleId,
            PrincipalType = e.PrincipalType!,
            PrincipalId = e.PrincipalId!.Value,
            Permission = e.Permission!,
            CreatedAt = now,
        }));
        audit.Record(user, AuditActions.KnowledgePermissionsChanged, "knowledge_article", articleId,
            new { Entries = entries.Select(e => new { e.PrincipalType, e.PrincipalId, e.Permission }) });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return await PermissionsOf(articleId, ct);
    }

    internal async Task<ArticleDetails> DetailsAsync(UserContext user, KnowledgeReader reader, KnowledgeArticle article, KnowledgeRight right, CancellationToken ct)
    {
        var summary = (await KnowledgeSummaries.LoadAsync(db, [article.Id], ct))[0];
        var current = await db.Set<KnowledgeVersion>().AsNoTracking()
            .Where(v => v.Id == article.CurrentVersionId)
            .Select(v => new { v.ContentJson, v.VersionNumber })
            .SingleOrDefaultAsync(ct);
        return new ArticleDetails(
            summary,
            current is null ? BlockContent.Empty() : ParseContent(current.ContentJson),
            current?.VersionNumber ?? 0,
            article.ReviewDueAt,
            new KnowledgeCapabilities(
                right >= KnowledgeRight.Edit, right >= KnowledgeRight.Admin, KnowledgeAccess.CanShareWithOrganization(reader, article.DepartmentId)),
            await links.RelationsOfAsync(reader, article.Id, ct),
            await links.ReferencesOfAsync(user, article.Id, ct));
    }

    private async Task<ServiceResult<ArticleDetails>> StoreVersionAsync(
        UserContext user, KnowledgeReader reader, KnowledgeArticle article, KnowledgeRight right, long expectedVersion,
        JsonObject content, string plainText, string? changeNote, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var number = (await db.Set<KnowledgeVersion>().Where(v => v.ArticleId == article.Id).MaxAsync(v => (int?)v.VersionNumber, ct) ?? 0) + 1;
        article.SearchText = plainText;
        db.Touch(article, expectedVersion, now);
        AddVersion(user, article, content, changeNote, number, now);
        try
        {
            if (await db.SaveVersionedAsync(article, ct) is { } conflict)
            {
                return conflict;
            }
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two saves computed the same version number: the later one is stale.
            db.ChangeTracker.Clear();
            return ServiceFailure.StaleVersion(expectedVersion + 1);
        }

        return await DetailsAsync(user, reader, article, right, ct);
    }

    private void AddVersion(UserContext user, KnowledgeArticle article, JsonObject content, string? changeNote, int number, DateTimeOffset now)
    {
        var version = new KnowledgeVersion
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = article.OrganizationId,
            ArticleId = article.Id,
            VersionNumber = number,
            ContentJson = content.ToJsonString(),
            Summary = article.Summary,
            CreatedBy = user.UserId,
            ChangeNote = changeNote,
            CreatedAt = now,
        };
        db.Set<KnowledgeVersion>().Add(version);
        article.CurrentVersionId = version.Id;
    }

    /// <summary>Newest first; filter before the projection, EF cannot translate it afterwards.</summary>
    private IQueryable<VersionSummary> Versions(Guid articleId, int? number = null) =>
        from version in db.Set<KnowledgeVersion>().AsNoTracking()
        where version.ArticleId == articleId && (number == null || version.VersionNumber == number)
        orderby version.VersionNumber descending
        join author in db.Set<AppUser>() on version.CreatedBy equals author.Id into authors
        from author in authors.DefaultIfEmpty()
        select new VersionSummary(version.Id, version.VersionNumber, version.CreatedBy, author == null ? null : author.DisplayName, version.ChangeNote, version.CreatedAt);

    private async Task<List<PermissionResponse>> PermissionsOf(Guid articleId, CancellationToken ct)
    {
        var grants = await db.Set<KnowledgePermission>().AsNoTracking().Where(p => p.ArticleId == articleId).ToListAsync(ct);
        var ids = grants.Select(g => g.PrincipalId).ToList();
        var users = await db.Set<AppUser>().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var departments = await db.Set<Departments.Department>().Where(d => ids.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Name, ct);
        return grants
            .Select(g => new PermissionResponse(
                g.PrincipalType, g.PrincipalId,
                (g.PrincipalType == KnowledgePrincipal.User ? users.GetValueOrDefault(g.PrincipalId) : departments.GetValueOrDefault(g.PrincipalId)) ?? "?",
                g.Permission))
            .OrderBy(p => p.PrincipalType).ThenBy(p => p.Name)
            .ToList();
    }

    private async Task<ServiceFailure?> ValidateAsync(UserContext user, KnowledgeArticle article, bool spaceChanged, CancellationToken ct)
    {
        if (article.Title.Length is 0 or > MaxTitleLength)
        {
            return ServiceFailure.Invalid("title", $"Required, at most {MaxTitleLength} characters.");
        }

        if (article.Summary?.Length > MaxSummaryLength)
        {
            return ServiceFailure.Invalid("summary", $"At most {MaxSummaryLength} characters.");
        }

        if (!KnowledgeArticleType.All.Contains(article.ArticleType))
        {
            return ServiceFailure.Invalid("articleType", $"Must be one of: {string.Join(", ", KnowledgeArticleType.All)}.");
        }

        if (!KnowledgeVisibility.All.Contains(article.Visibility))
        {
            return ServiceFailure.Invalid("visibility", $"Must be one of: {string.Join(", ", KnowledgeVisibility.All)}.");
        }

        if (spaceChanged && article.KnowledgeSpaceId is { } spaceId
            && !await db.Set<KnowledgeSpace>().AnyAsync(
                s => s.Id == spaceId && s.OrganizationId == user.OrganizationId && s.DepartmentId == article.DepartmentId, ct))
        {
            return ServiceFailure.Invalid("spaceId", "Must be a knowledge space of the article's department.");
        }

        return null;
    }

    private async Task<string> UniqueSlugAsync(Guid organizationId, string title, Guid? articleId, CancellationToken ct)
    {
        var baseSlug = Slugify(title);
        var taken = await db.Set<KnowledgeArticle>()
            .Where(a => a.OrganizationId == organizationId && a.DeletedAt == null && a.Id != articleId
                        && (a.Slug == baseSlug || a.Slug.StartsWith(baseSlug + "-")))
            .Select(a => a.Slug)
            .ToListAsync(ct);
        if (!taken.Contains(baseSlug))
        {
            return baseSlug;
        }

        var suffix = 2;
        while (taken.Contains($"{baseSlug}-{suffix}"))
        {
            suffix++;
        }

        return $"{baseSlug}-{suffix}";
    }

    /// <summary>URL-friendly name: lower case ASCII, German umlauts spelled out.</summary>
    public static string Slugify(string title)
    {
        var text = title.ToLowerInvariant()
            .Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("ß", "ss")
            .Normalize(NormalizationForm.FormD);
        var slug = new StringBuilder();
        foreach (var c in text)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                slug.Append(c);
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        var result = slug.ToString().Trim('-');
        if (result.Length > 80)
        {
            result = result[..80].TrimEnd('-');
        }

        return result.Length == 0 ? "artikel" : result;
    }

    private static JsonObject ParseContent(string json) => JsonNode.Parse(json) as JsonObject ?? BlockContent.Empty();

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private KnowledgeTag Track(KnowledgeTag tag)
    {
        db.Set<KnowledgeTag>().Add(tag);
        return tag;
    }

    /// <summary>Two articles raced for the same slug or tag name: report a conflict the client can retry.</summary>
    private async Task<ServiceFailure?> SaveUniqueAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return ServiceFailure.Conflict("Someone else saved the same name at the same time. Please try again.");
        }
    }

    private async Task<ServiceFailure?> SaveVersionedUniqueAsync(KnowledgeArticle article, CancellationToken ct)
    {
        try
        {
            return await db.SaveVersionedAsync(article, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return ServiceFailure.Conflict("Someone else saved the same name at the same time. Please try again.");
        }
    }
}

public sealed record TagResponse(string Name, int ArticleCount);
