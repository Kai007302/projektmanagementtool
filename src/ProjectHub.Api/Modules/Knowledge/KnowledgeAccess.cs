using Microsoft.EntityFrameworkCore;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Infrastructure.Outcomes;
using ProjectHub.Api.Modules.Identity;
using ProjectHub.Api.Modules.Identity.Authorization;
using ProjectHub.Api.Modules.Teams;
using ProjectHub.Api.Modules.Users;

namespace ProjectHub.Api.Modules.Knowledge;

/// <summary>
/// Who is reading knowledge. Every query and every retrieval (search, later AI) takes one,
/// so content the person may not see never leaves the database.
/// </summary>
public sealed record KnowledgeReader(Guid UserId, Guid OrganizationId, bool IsOrganizationAdmin, IReadOnlyList<Guid> TeamIds);

public enum KnowledgeRight
{
    None = 0,
    View = 1,
    Edit = 2,
    Admin = 3,
}

/// <summary>What the caller may do with an article, so the UI only offers allowed actions.</summary>
public sealed record KnowledgeCapabilities(bool CanEdit, bool CanAdmin);

/// <summary>
/// Knowledge permissions (docs/PERMISSIONS.md, DEC-020):
/// organization admins and the owner administer an article; explicit grants for users or teams
/// give view, edit or admin; published and archived articles with visibility "organization"
/// are readable by everyone in the organization. Drafts and reviews are never organization-wide.
/// </summary>
public sealed class KnowledgeAccess(ProjectHubDbContext db)
{
    public async Task<KnowledgeReader> ReaderAsync(UserContext user, CancellationToken ct) =>
        new(user.UserId, user.OrganizationId, user.IsOrganizationAdmin, await TeamsOfAsync(user.OrganizationId, user.UserId, ct));

    /// <summary>The reader for another active user of the organization, e.g. someone mentioned in a comment.</summary>
    public async Task<KnowledgeReader?> ReaderForAsync(Guid organizationId, Guid userId, CancellationToken ct)
    {
        var role = await db.Set<AppUser>()
            .Where(u => u.Id == userId && u.OrganizationId == organizationId && u.Status == UserStatus.Active)
            .Select(u => u.OrganizationRole)
            .SingleOrDefaultAsync(ct);
        return role is null
            ? null
            : new KnowledgeReader(userId, organizationId, role == OrganizationRole.Admin, await TeamsOfAsync(organizationId, userId, ct));
    }

    /// <summary>Active articles the reader may see.</summary>
    public IQueryable<KnowledgeArticle> Visible(KnowledgeReader reader)
    {
        var userId = reader.UserId;
        var isAdmin = reader.IsOrganizationAdmin;
        var teamIds = reader.TeamIds.ToList();
        return db.Set<KnowledgeArticle>().AsNoTracking()
            .Where(a => a.OrganizationId == reader.OrganizationId && a.DeletedAt == null)
            .Where(a => isAdmin
                        || a.OwnerId == userId
                        || (a.Visibility == KnowledgeVisibility.Organization
                            && (a.Status == KnowledgeStatus.Published || a.Status == KnowledgeStatus.Archived))
                        || db.Set<KnowledgePermission>().Any(p => p.ArticleId == a.Id
                            && ((p.PrincipalType == "user" && p.PrincipalId == userId)
                                || (p.PrincipalType == "team" && teamIds.Contains(p.PrincipalId)))));
    }

    public async Task<KnowledgeRight> RightAsync(KnowledgeReader reader, KnowledgeArticle article, CancellationToken ct)
    {
        if (article.OrganizationId != reader.OrganizationId || article.DeletedAt is not null)
        {
            return KnowledgeRight.None;
        }

        if (reader.IsOrganizationAdmin || article.OwnerId == reader.UserId)
        {
            return KnowledgeRight.Admin;
        }

        var teamIds = reader.TeamIds.ToList();
        var grants = await db.Set<KnowledgePermission>()
            .Where(p => p.ArticleId == article.Id
                        && ((p.PrincipalType == "user" && p.PrincipalId == reader.UserId)
                            || (p.PrincipalType == "team" && teamIds.Contains(p.PrincipalId))))
            .Select(p => p.Permission)
            .ToListAsync(ct);
        var granted = (KnowledgeRight)grants.Select(KnowledgeGrant.Rank).DefaultIfEmpty(0).Max();
        if (granted != KnowledgeRight.None)
        {
            return granted;
        }

        return article.Visibility == KnowledgeVisibility.Organization
               && article.Status is KnowledgeStatus.Published or KnowledgeStatus.Archived
            ? KnowledgeRight.View
            : KnowledgeRight.None;
    }

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

    private Task<List<Guid>> TeamsOfAsync(Guid organizationId, Guid userId, CancellationToken ct) =>
        db.Set<TeamMember>().Where(m => m.OrganizationId == organizationId && m.UserId == userId).Select(m => m.TeamId).ToListAsync(ct);
}
