using ProjectHub.Api.Infrastructure.Events;

namespace ProjectHub.Api.Modules.Projects;

/// <summary>
/// Tasks, the board or the Gantt chart of a project changed and was committed. Carries only ids: listeners
/// (e.g. realtime clients) reload through the authorized API instead of receiving content.
/// </summary>
public sealed record ProjectContentChanged(Guid ProjectId, string Area) : IDomainEvent
{
    public const string Tasks = "tasks";
    public const string Board = "board";
    public const string Gantt = "gantt";
}
