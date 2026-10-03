using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Modules.Audit;
using ProjectHub.Api.Modules.Identity;
using ProjectHub.Api.Modules.Identity.Authorization;
using ProjectHub.Api.Modules.Users;

namespace ProjectHub.Api.Modules.Teams;

public sealed record TeamSummary(Guid Id, string Name, string? Description, int MemberCount);

public sealed record TeamMemberResponse(Guid UserId, string DisplayName, string Email, string Role);

public sealed record TeamDetails(Guid Id, string Name, string? Description, bool CanManage, IReadOnlyList<TeamMemberResponse> Members);

public enum TeamOutcome
{
    Success,
    NotFound,
    Forbidden,
    UserNotFound,
    Duplicate,
}

public sealed class TeamService(
    ProjectHubDbContext db,
    IProjectHubAuthorization authorization,
    IAuditLog audit,
    TimeProvider clock)
{
    public const int MaxNameLength = 200;
    public const int MaxDescriptionLength = 2000;

    public async Task<IReadOnlyList<TeamSummary>> ListAsync(UserContext user, int skip, int take, CancellationToken ct) =>
        await db.Set<Team>().AsNoTracking()
            .Where(t => t.OrganizationId == user.OrganizationId)
            .OrderBy(t => t.Name).ThenBy(t => t.Id)
            .Skip(skip).Take(take)
            .Select(t => new TeamSummary(
                t.Id,
                t.Name,
                t.Description,
                db.Set<TeamMember>().Count(m => m.TeamId == t.Id)))
            .ToListAsync(ct);

    public async Task<TeamDetails?> GetAsync(UserContext user, Guid teamId, CancellationToken ct)
    {
        var team = await db.Set<Team>().AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == teamId && t.OrganizationId == user.OrganizationId, ct);
        if (team is null)
        {
            return null;
        }

        var members = await (
                from member in db.Set<TeamMember>().AsNoTracking()
                join appUser in db.Set<AppUser>().AsNoTracking() on member.UserId equals appUser.Id
                where member.TeamId == teamId && member.OrganizationId == user.OrganizationId
                orderby appUser.DisplayName
                select new TeamMemberResponse(appUser.Id, appUser.DisplayName, appUser.Email, member.Role))
            .ToListAsync(ct);

        var canManage = await authorization.CanManageTeamAsync(user, teamId, ct);
        return new TeamDetails(team.Id, team.Name, team.Description, canManage, members);
    }

    public async Task<(TeamOutcome Outcome, Team? Team)> CreateAsync(UserContext user, string name, string? description, CancellationToken ct)
    {
        if (!authorization.CanCreateTeam(user))
        {
            return (TeamOutcome.Forbidden, null);
        }

        var now = clock.GetUtcNow();
        var team = new Team
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = user.OrganizationId,
            Name = name,
            Description = description,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };

        db.Set<Team>().Add(team);
        audit.Record(user, AuditActions.TeamCreated, "team", team.Id, new { team.Name });

        return await SaveAsync(ct) ? (TeamOutcome.Success, team) : (TeamOutcome.Duplicate, null);
    }

    public async Task<TeamOutcome> AddMemberAsync(UserContext user, Guid teamId, Guid memberUserId, string role, CancellationToken ct)
    {
        var access = await CheckManageAccessAsync(user, teamId, ct);
        if (access != TeamOutcome.Success)
        {
            return access;
        }

        var memberExists = await db.Set<AppUser>().AnyAsync(u => u.Id == memberUserId && u.OrganizationId == user.OrganizationId, ct);
        if (!memberExists)
        {
            return TeamOutcome.UserNotFound;
        }

        db.Set<TeamMember>().Add(new TeamMember
        {
            OrganizationId = user.OrganizationId,
            TeamId = teamId,
            UserId = memberUserId,
            Role = role,
            CreatedAt = clock.GetUtcNow(),
        });
        audit.Record(user, AuditActions.TeamMemberAdded, "team", teamId, new { UserId = memberUserId, Role = role });

        return await SaveAsync(ct) ? TeamOutcome.Success : TeamOutcome.Duplicate;
    }

    public async Task<TeamOutcome> RemoveMemberAsync(UserContext user, Guid teamId, Guid memberUserId, CancellationToken ct)
    {
        var access = await CheckManageAccessAsync(user, teamId, ct);
        if (access != TeamOutcome.Success)
        {
            return access;
        }

        var member = await db.Set<TeamMember>()
            .SingleOrDefaultAsync(m => m.TeamId == teamId && m.UserId == memberUserId && m.OrganizationId == user.OrganizationId, ct);
        if (member is null)
        {
            return TeamOutcome.UserNotFound;
        }

        db.Set<TeamMember>().Remove(member);
        audit.Record(user, AuditActions.TeamMemberRemoved, "team", teamId, new { UserId = memberUserId });
        await db.SaveChangesAsync(ct);
        return TeamOutcome.Success;
    }

    /// <summary>Teams of other organizations are reported as not found, never as forbidden.</summary>
    private async Task<TeamOutcome> CheckManageAccessAsync(UserContext user, Guid teamId, CancellationToken ct)
    {
        var exists = await db.Set<Team>().AnyAsync(t => t.Id == teamId && t.OrganizationId == user.OrganizationId, ct);
        if (!exists)
        {
            return TeamOutcome.NotFound;
        }

        return await authorization.CanManageTeamAsync(user, teamId, ct) ? TeamOutcome.Success : TeamOutcome.Forbidden;
    }

    private async Task<bool> SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }
}
