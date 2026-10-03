namespace ProjectHub.Api.Modules.Identity.Authorization;

/// <summary>Organization-wide roles (app_user.organization_role).</summary>
public static class OrganizationRole
{
    public const string Admin = "admin";
    public const string Member = "member";
}

/// <summary>Per-project roles (project_member.role).</summary>
public static class ProjectRole
{
    public const string Admin = "admin";
    public const string Editor = "editor";
    public const string Member = "member";
    public const string Viewer = "viewer";
    public const string Guest = "guest";

    public static readonly IReadOnlyList<string> All = [Admin, Editor, Member, Viewer, Guest];
}

/// <summary>Per-team roles (team_member.role).</summary>
public static class TeamRole
{
    public const string Owner = "owner";
    public const string Member = "member";

    public static readonly IReadOnlyList<string> All = [Owner, Member];
}

public enum ProjectPermission
{
    /// <summary>Read the project and its content.</summary>
    View,

    /// <summary>Work on content: tasks, comments.</summary>
    Contribute,

    /// <summary>Change project data and structure.</summary>
    Edit,

    /// <summary>Settings, members, deletion.</summary>
    Manage,
}

/// <summary>
/// The single place that says which project role grants which permission.
/// Organization admins hold every permission on every project of their own organization.
/// </summary>
public static class ProjectPermissions
{
    public static bool Grants(string projectRole, ProjectPermission permission) => projectRole switch
    {
        ProjectRole.Admin => true,
        ProjectRole.Editor => permission is ProjectPermission.View or ProjectPermission.Contribute or ProjectPermission.Edit,
        ProjectRole.Member => permission is ProjectPermission.View or ProjectPermission.Contribute,
        ProjectRole.Viewer or ProjectRole.Guest => permission is ProjectPermission.View,
        _ => false,
    };
}
