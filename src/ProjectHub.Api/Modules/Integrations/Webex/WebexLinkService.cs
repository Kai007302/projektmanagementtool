using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Infrastructure.Events;
using ProjectHub.Api.Infrastructure.Outcomes;
using ProjectHub.Api.Modules.Audit;
using ProjectHub.Api.Modules.Identity;
using ProjectHub.Api.Modules.Identity.Authorization;
using ProjectHub.Api.Modules.Notifications;
using ProjectHub.Api.Modules.Projects;
using ProjectHub.Api.Modules.Users;

namespace ProjectHub.Api.Modules.Integrations.Webex;

/// <summary>A Webex meeting or space linked to a project (ADR 0011). Spaces the bot created carry their room id.</summary>
public sealed class ProjectWebexLink
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public Guid ProjectId { get; init; }
    public required string Kind { get; init; }
    public required string Title { get; init; }
    public required string Url { get; init; }
    public string? RoomId { get; init; }
    public string Status { get; set; } = WebexLinkStatus.Active;
    public Guid CreatedBy { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public static class WebexLinkKind
{
    public const string Meeting = "meeting";
    public const string Space = "space";

    public static readonly IReadOnlyList<string> All = [Meeting, Space];
}

public static class WebexLinkStatus
{
    public const string Active = "active";

    /// <summary>The bot was removed from the space; ProjectHub can no longer post there.</summary>
    public const string Disconnected = "disconnected";
}

internal sealed class ProjectWebexLinkConfiguration : IEntityTypeConfiguration<ProjectWebexLink>
{
    public void Configure(EntityTypeBuilder<ProjectWebexLink> builder) => builder.ToTable("project_webex_link");
}

public sealed record WebexLinkResponse(Guid Id, string Kind, string Title, string Url, string Status, bool CreatedByBot, DateTimeOffset CreatedAt);

public sealed record ProjectWebexResponse(bool Available, IReadOnlyList<WebexLinkResponse> Links);

public sealed record CreateWebexLinkRequest(string? Kind, string? Title, string? Url);

public sealed class WebexLinkService(
    ProjectHubDbContext db,
    ProjectAccess projectAccess,
    IWebexClient webex,
    WebexOptions options,
    NotificationOptions app,
    IActivityLog activity,
    IDomainEventPublisher events,
    TimeProvider clock,
    ILogger<WebexLinkService> logger)
{
    public const int MaxTitleLength = 200;
    public const int MaxUrlLength = 2000;

    public async Task<ServiceResult<ProjectWebexResponse>> ListAsync(UserContext user, Guid projectId, CancellationToken ct)
    {
        if (await projectAccess.RequireAsync(user, projectId, ProjectPermission.View, ct) is { } failure)
        {
            return failure;
        }

        var links = await db.Set<ProjectWebexLink>()
            .Where(l => l.OrganizationId == user.OrganizationId && l.ProjectId == projectId)
            .OrderBy(l => l.Kind).ThenBy(l => l.CreatedAt)
            .ToListAsync(ct);
        return new ProjectWebexResponse(options.Available, links.Select(ToResponse).ToList());
    }

    public async Task<ServiceResult<WebexLinkResponse>> AddAsync(UserContext user, Guid projectId, CreateWebexLinkRequest request, CancellationToken ct)
    {
        if (await projectAccess.RequireAsync(user, projectId, ProjectPermission.Edit, ct) is { } failure)
        {
            return failure;
        }

        if (request.Kind is not { } kind || !WebexLinkKind.All.Contains(kind))
        {
            return ServiceFailure.Invalid("kind", "Must be 'meeting' or 'space'.");
        }

        var title = request.Title?.Trim() ?? "";
        if (title.Length is 0 or > MaxTitleLength)
        {
            return ServiceFailure.Invalid("title", $"Required, at most {MaxTitleLength} characters.");
        }

        if (!IsWebexUrl(request.Url))
        {
            return ServiceFailure.Invalid("url", "Must be an https link to webex.com.");
        }

        var link = new ProjectWebexLink
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = user.OrganizationId,
            ProjectId = projectId,
            Kind = kind,
            Title = title,
            Url = new Uri(request.Url!.Trim()).AbsoluteUri,
            CreatedBy = user.UserId,
            CreatedAt = clock.GetUtcNow(),
        };
        db.Add(link);
        activity.Record(user, projectId, ActivityActions.WebexLinkAdded, "webex_link", link.Id, new { link.Kind, link.Title });
        await db.SaveChangesAsync(ct);
        await events.PublishAsync(new ProjectContentChanged(projectId, ProjectContentChanged.Webex), ct);
        return ToResponse(link);
    }

    /// <summary>Removes the link only. A space the bot created stays in Webex.</summary>
    public async Task<ServiceResult<Done>> RemoveAsync(UserContext user, Guid linkId, CancellationToken ct)
    {
        var link = await db.Set<ProjectWebexLink>().AsTracking()
            .SingleOrDefaultAsync(l => l.Id == linkId && l.OrganizationId == user.OrganizationId, ct);
        if (link is null || await projectAccess.RequireAsync(user, link.ProjectId, ProjectPermission.View, ct) is not null)
        {
            return ServiceFailure.NotFound("Webex link");
        }

        if (await projectAccess.RequireAsync(user, link.ProjectId, ProjectPermission.Edit, ct) is { } failure)
        {
            return failure;
        }

        db.Remove(link);
        activity.Record(user, link.ProjectId, ActivityActions.WebexLinkRemoved, "webex_link", link.Id, new { link.Kind, link.Title });
        await db.SaveChangesAsync(ct);
        await events.PublishAsync(new ProjectContentChanged(link.ProjectId, ProjectContentChanged.Webex), ct);
        return Done.Value;
    }

    /// <summary>
    /// The bot creates a space named after the project, invites the current members and posts a link to the project.
    /// Serialized per project, so two clicks cannot create two spaces.
    /// </summary>
    public async Task<ServiceResult<WebexLinkResponse>> CreateSpaceAsync(UserContext user, Guid projectId, CancellationToken ct)
    {
        if (await projectAccess.RequireAsync(user, projectId, ProjectPermission.Edit, ct) is { } failure)
        {
            return failure;
        }

        if (!options.Available)
        {
            return ServiceFailure.Conflict("Webex is not set up.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"select pg_advisory_xact_lock(hashtextextended({"webex-space:" + projectId}, 0))", ct);
        if (await db.Set<ProjectWebexLink>().AnyAsync(l => l.ProjectId == projectId && l.RoomId != null && l.Status == WebexLinkStatus.Active, ct))
        {
            return ServiceFailure.Conflict("The project already has a Webex space.");
        }

        var project = await db.Set<Project>().SingleAsync(p => p.Id == projectId, ct);
        var emails = await (
            from member in db.Set<ProjectMember>()
            join person in db.Set<AppUser>() on member.UserId equals person.Id
            where member.ProjectId == projectId && person.OrganizationId == user.OrganizationId && person.Status == UserStatus.Active
            orderby person.Email
            select person.Email).ToListAsync(ct);

        WebexSpace space;
        var invited = 0;
        try
        {
            space = await webex.CreateSpaceAsync(project.Name.Length <= MaxTitleLength ? project.Name : project.Name[..MaxTitleLength], ct);
            foreach (var email in emails)
            {
                try
                {
                    await webex.AddMemberAsync(space.RoomId, email, ct);
                    invited++;
                }
                catch (MessageDeliveryException ex)
                {
                    // One person Webex does not know must not stop the others.
                    logger.LogWarning("Inviting a member of project {ProjectId} to its Webex space failed ({Error})", projectId, ex.Code);
                }
            }

            await webex.PostMessageAsync(space.RoomId, $"Projektraum für **{NotificationDispatcher.WebexMarkdown(project.Name)}**. [In ProjectHub öffnen]({app.AppUrl})", ct);
        }
        catch (MessageDeliveryException ex)
        {
            logger.LogWarning("Creating the Webex space for project {ProjectId} failed ({Error})", projectId, ex.Code);
            return ServiceFailure.Unavailable($"Webex did not create the space ({ex.Code}).");
        }

        var link = new ProjectWebexLink
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = user.OrganizationId,
            ProjectId = projectId,
            Kind = WebexLinkKind.Space,
            Title = space.Title,
            Url = WebexRooms.AppLink(space.RoomId) ?? "https://web.webex.com/",
            RoomId = space.RoomId,
            CreatedBy = user.UserId,
            CreatedAt = clock.GetUtcNow(),
        };
        db.Add(link);
        activity.Record(user, projectId, ActivityActions.WebexSpaceCreated, "webex_link", link.Id, new { Invited = invited, Members = emails.Count });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await events.PublishAsync(new ProjectContentChanged(projectId, ProjectContentChanged.Webex), ct);
        return ToResponse(link);
    }

    /// <summary>Only https links to webex.com or one of its subdomains (e.g. firma.webex.com).</summary>
    public static bool IsWebexUrl(string? value) =>
        value is { Length: > 0 and <= MaxUrlLength }
        && Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && string.IsNullOrEmpty(uri.UserInfo)
        && uri.IsDefaultPort
        && (uri.Host.Equals("webex.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".webex.com", StringComparison.OrdinalIgnoreCase));

    private static WebexLinkResponse ToResponse(ProjectWebexLink link) =>
        new(link.Id, link.Kind, link.Title, link.Url, link.Status, link.RoomId is not null, link.CreatedAt);
}
