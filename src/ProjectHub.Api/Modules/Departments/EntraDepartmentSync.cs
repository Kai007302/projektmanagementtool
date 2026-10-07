using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Modules.Audit;
using ProjectHub.Api.Modules.Identity;
using ProjectHub.Api.Modules.Identity.Authorization;
using ProjectHub.Api.Modules.Users;

namespace ProjectHub.Api.Modules.Departments;

/// <summary>Whether departments follow the Entra ID groups in the sign-in token (ADR 0021). Off by default.</summary>
public sealed record EntraDepartmentOptions(bool Enabled)
{
    public const string EnabledKey = "PROJECTHUB_DEPARTMENTS_FROM_ENTRA_GROUPS";

    public static EntraDepartmentOptions FromConfiguration(IConfiguration configuration) =>
        new(bool.TryParse(configuration[EnabledKey], out var enabled) && enabled);
}

/// <summary>
/// Takes over department memberships from Entra ID security groups (ADR 0021): a department connected to a group
/// gets everyone in it as a member, and loses the members who came from the group once they leave it. Memberships
/// added by hand are never touched, roles changed by hand are kept. Runs on requests, but only does work when the
/// groups in the token differ from the last sync (or a department's group changed, which clears the stored hash).
/// Needs no Microsoft Graph permission: the groups come from the token's "groups" claim.
/// </summary>
internal sealed class EntraDepartmentSync(
    EntraDepartmentOptions options,
    ProjectHubDbContext db,
    IAuditLog audit,
    TimeProvider clock,
    ILogger<EntraDepartmentSync> logger)
{
    public async Task SyncAsync(UserContext user, ICurrentUser caller, CancellationToken ct)
    {
        if (!options.Enabled)
        {
            return;
        }

        if (caller.EntraGroupIds is not { } groups)
        {
            // Too many groups for the token: leave the departments as they are rather than guess.
            logger.LogWarning("The token of user {UserId} lists no groups because of group overage; departments were not synced.", user.UserId);
            return;
        }

        var groupIds = groups.Select(g => g.Trim().ToLowerInvariant()).Where(g => g.Length > 0).Distinct().Order(StringComparer.Ordinal).ToList();
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(',', groupIds))));
        var me = db.Set<AppUser>().Where(u => u.Id == user.UserId && u.OrganizationId == user.OrganizationId);
        if (await me.Select(u => u.EntraGroupsHash).SingleOrDefaultAsync(ct) == hash)
        {
            return;
        }

        var connected = await db.Set<Department>().AsNoTracking()
            .Where(d => d.OrganizationId == user.OrganizationId && d.EntraGroupId != null)
            .Select(d => new { d.Id, d.EntraGroupId })
            .ToListAsync(ct);
        var wanted = connected.Where(d => groupIds.Contains(d.EntraGroupId!.ToLowerInvariant())).Select(d => d.Id).ToHashSet();
        var memberships = await db.Set<DepartmentMember>().AsTracking()
            .Where(m => m.OrganizationId == user.OrganizationId && m.UserId == user.UserId)
            .ToListAsync(ct);

        var now = clock.GetUtcNow();
        foreach (var departmentId in wanted.Where(id => memberships.All(m => m.DepartmentId != id)))
        {
            db.Set<DepartmentMember>().Add(new DepartmentMember
            {
                OrganizationId = user.OrganizationId,
                DepartmentId = departmentId,
                UserId = user.UserId,
                Role = DepartmentRole.Member,
                Source = DepartmentMemberSource.Entra,
                CreatedAt = now,
            });
            audit.Record(user, AuditActions.DepartmentMemberAdded, "department", departmentId,
                new { user.UserId, Role = DepartmentRole.Member, Source = DepartmentMemberSource.Entra });
        }

        foreach (var membership in memberships.Where(m => m.Source == DepartmentMemberSource.Entra && !wanted.Contains(m.DepartmentId)))
        {
            db.Set<DepartmentMember>().Remove(membership);
            audit.Record(user, AuditActions.DepartmentMemberRemoved, "department", membership.DepartmentId,
                new { user.UserId, Source = DepartmentMemberSource.Entra });
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A parallel request of the same person synced first.
            db.ChangeTracker.Clear();
            return;
        }

        // Not through the tracked user: the hash is bookkeeping and must not bump the user's version.
        await me.ExecuteUpdateAsync(s => s.SetProperty(u => u.EntraGroupsHash, hash), ct);
    }
}
