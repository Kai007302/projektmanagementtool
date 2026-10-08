using Microsoft.EntityFrameworkCore;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Infrastructure.Outcomes;
using ProjectHub.Api.Modules.Audit;
using ProjectHub.Api.Modules.Departments;
using ProjectHub.Api.Modules.Identity;
using ProjectHub.Api.Modules.Identity.Authorization;
using ProjectHub.Api.Modules.Users;

namespace ProjectHub.Api.Modules.Knowledge;

/// <summary>
/// Who is reading knowledge. Every query and every retrieval (search, AI) takes one, so content the person may not
/// see never leaves the database. <c>DepartmentIds</c>: departments where the person is lead or member (guests see no
/// department knowledge); <c>LeadDepartmentIds</c>: those they lead (ADR 0021).
/// </summary>
public sealed record KnowledgeReader(
    Guid UserId, Guid OrganizationId, bool IsOrganizationAdmin, IReadOnlyList<Guid> DepartmentIds, IReadOnlyList<Guid> LeadDepartmentIds);

public enum KnowledgeRight
{
    None = 0,
    View = 1,
    Edit = 2,
    Admin = 3,
}

/// <summary>What the caller may do with an article, so the UI only offers allowed actions.</summary>
public sealed record KnowledgeCapabilities(bool CanEdit, bool CanAdmin, bool CanShareWithOrganization = false);

/// <summary>
/// Knowledge permissions (docs/PERMISSIONS.md, DEC-020, ADR 0021): organization admins, the owner and the leads of
/// the article's department administer it; explicit grants for users or departments give view, edit or admin;
/// published and archived articles are readable by the article's department (visibility "department") or by everyone
/// in the organization ("organization"). Drafts and reviews are never readable through the visibility.
/// </summary>
public sealed class KnowledgeAccess(ProjectHubDbContext db, IAuditLog audit)
{
    public Task<KnowledgeReader> ReaderAsync(UserContext user, CancellationToken ct) =>
        ReaderAsync(user.OrganizationId, user.UserId, user.IsOrganizationAdmin, ct);

    /// <summary>The reader for another active user of the organization, e.g. someone mentioned in a comment.</summary>
    public async Task<KnowledgeReader?> ReaderForAsync(Guid organizationId, Guid userId, CancellationToken ct)
    {
        var role = await db.Set<AppUser>()
            .Where(u => u.Id == userId && u.OrganizationId == organizationId && u.Status == UserStatus.Active)
            .Select(u => u.OrganizationRole)
            .SingleOrDefaultAsync(ct);
        return role is null ? null : await ReaderAsync(organizationId, userId, role == OrganizationRole.Admin, ct);
    }

    /// <summary>Active articles the reader may see.</summary>
    public IQueryable<KnowledgeArticle> Visible(KnowledgeReader reader)
    {
        var userId = reader.UserId;
        var isAdmin = reader.IsOrganizationAdmin;
        var departmentIds = reader.DepartmentIds.ToList();
        var leadIds = reader.LeadDepartmentIds.ToList();
        return db.Set<KnowledgeArticle>().AsNoTracking()
            .Where(a => a.OrganizationId == reader.OrganizationId && a.DeletedAt == null)
            .Where(a => isAdmin
                        || a.OwnerId == userId
                        || leadIds.Contains(a.DepartmentId)
                        || ((a.Status == KnowledgeStatus.Published || a.Status == KnowledgeStatus.Archived)
                            && (a.Visibility == KnowledgeVisibility.Organization
                                || (a.Visibility == KnowledgeVisibility.Department && departmentIds.Contains(a.DepartmentId))))
                        || db.Set<KnowledgePermission>().Any(p => p.ArticleId == a.Id
                            && ((p.PrincipalType == KnowledgePrincipal.User && p.PrincipalId == userId)
                                || (p.PrincipalType == KnowledgePrincipal.Department && departmentIds.Contains(p.PrincipalId)))));
    }

    /// <summary>
    /// Knowledge spaces of the reader's departments (all of them for organization admins). Articles of other
    /// departments shared with the reader still name their space.
    /// </summary>
    public IQueryable<KnowledgeSpace> Spaces(KnowledgeReader reader)
    {
        var departmentIds = reader.DepartmentIds.ToList();
        var isAdmin = reader.IsOrganizationAdmin;
        return db.Set<KnowledgeSpace>().AsNoTracking()
            .Where(s => s.OrganizationId == reader.OrganizationId && (isAdmin || departmentIds.Contains(s.DepartmentId)));
    }

    public async Task<KnowledgeRight> RightAsync(KnowledgeReader reader, KnowledgeArticle article, CancellationToken ct)
    {
        if (article.OrganizationId != reader.OrganizationId || article.DeletedAt is not null)
        {
            return KnowledgeRight.None;
        }

        if (reader.IsOrganizationAdmin || article.OwnerId == reader.UserId || reader.LeadDepartmentIds.Contains(article.DepartmentId))
        {
            return KnowledgeRight.Admin;
        }

        var departmentIds = reader.DepartmentIds.ToList();
        var grants = await db.Set<KnowledgePermission>()
            .Where(p => p.ArticleId == article.Id
                        && ((p.PrincipalType == KnowledgePrincipal.User && p.PrincipalId == reader.UserId)
                            || (p.PrincipalType == KnowledgePrincipal.Department && departmentIds.Contains(p.PrincipalId))))
            .Select(p => p.Permission)
            .ToListAsync(ct);
        var granted = (KnowledgeRight)grants.Select(KnowledgeGrant.Rank).DefaultIfEmpty(0).Max();
        if (granted != KnowledgeRight.None)
        {
            return granted;
        }

        var readable = article.Status is KnowledgeStatus.Published or KnowledgeStatus.Archived
                       && (article.Visibility == KnowledgeVisibility.Organization
                           || (article.Visibility == KnowledgeVisibility.Department && reader.DepartmentIds.Contains(article.DepartmentId)));
        return readable ? KnowledgeRight.View : KnowledgeRight.None;
    }

    /// <summary>Making an article readable for the whole organization: the department's leads and organization admins.</summary>
    public static bool CanShareWithOrganization(KnowledgeReader reader, Guid departmentId) =>
        reader.IsOrganizationAdmin || reader.LeadDepartmentIds.Contains(departmentId);

    /// <summary>
    /// Loads the article (tracked, for changes) and checks <paramref name="needed"/>.
    /// Articles the caller cannot see are "not found"; visible ones without the right are "forbidden".
    /// </summary>
    public async Task<(KnowledgeArticle? Article, KnowledgeReader Reader, KnowledgeRight Right, ServiceFailure? Failure)> RequireAsync(
        UserContext user, Guid articleId, KnowledgeRight needed, CancellationToken ct)
    {
        var reader = await ReaderAsync(user, ct);
        var article = await db.Set<KnowledgeArticle>().AsTracking()
            .SingleOrDefaultAsync(a => a.Id == articleId && a.OrganizationId == user.OrganizationId && a.DeletedAt == null, ct);
        var right = article is null ? KnowledgeRight.None : await RightAsync(reader, article, ct);
        if (article is null || right == KnowledgeRight.None)
        {
            return (null, reader, KnowledgeRight.None, ServiceFailure.NotFound("Article"));
        }

        return right < needed
            ? (article, reader, right, ServiceFailure.Forbidden($"Missing knowledge permission '{needed}'."))
            : (article, reader, right, null);
    }

    /// <summary>
    /// Records when an organization admin opens an article only their admin role lets them see (ADR 0021).
    /// Adds the entry to the unit of work; the caller saves.
    /// </summary>
    public async Task RecordAdminAccessAsync(UserContext user, KnowledgeReader reader, KnowledgeArticle article, CancellationToken ct)
    {
        if (reader.IsOrganizationAdmin && await RightAsync(reader with { IsOrganizationAdmin = false }, article, ct) == KnowledgeRight.None)
        {
            audit.Record(user, AuditActions.OrganizationAdminAccess, "knowledge_article", article.Id);
        }
    }

    private async Task<KnowledgeReader> ReaderAsync(Guid organizationId, Guid userId, bool isAdmin, CancellationToken ct)
    {
        var memberships = await db.Set<DepartmentMember>()
            .Where(m => m.OrganizationId == organizationId && m.UserId == userId && m.Role != DepartmentRole.Guest)
            .Select(m => new { m.DepartmentId, m.Role })
            .ToListAsync(ct);
        return new KnowledgeReader(
            userId, organizationId, isAdmin,
            memberships.Select(m => m.DepartmentId).ToList(),
            memberships.Where(m => m.Role == DepartmentRole.Lead).Select(m => m.DepartmentId).ToList());
    }
}
