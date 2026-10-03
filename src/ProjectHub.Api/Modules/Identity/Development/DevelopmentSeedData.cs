using ProjectHub.Api.Modules.Identity.Authorization;

namespace ProjectHub.Api.Modules.Identity.Development;

/// <summary>
/// Synthetic organizations, users, teams and projects for local development and tests.
/// Never real people or company data. Two organizations exist so cross-organization
/// isolation can be exercised.
/// </summary>
public static class DevelopmentSeedData
{
    public sealed record SeedOrganization(Guid Id, string Name, string Slug, string TenantId);

    public sealed record SeedUser(Guid Id, Guid OrganizationId, string ObjectId, string DisplayName, string Email, string OrganizationRole, string? Department);

    public sealed record SeedTeam(Guid Id, Guid OrganizationId, string Name, string Description, IReadOnlyList<(Guid UserId, string Role)> Members);

    public sealed record SeedProject(Guid Id, Guid OrganizationId, string Name, Guid OwnerId, IReadOnlyList<(Guid UserId, string Role)> Members);

    public static readonly SeedOrganization Contoso = new(Guid.Parse("01920000-0000-7000-8000-000000000001"), "Contoso (Dev)", "contoso-dev", "dev-tenant-contoso");
    public static readonly SeedOrganization Fabrikam = new(Guid.Parse("01920000-0000-7000-8000-000000000002"), "Fabrikam (Dev)", "fabrikam-dev", "dev-tenant-fabrikam");

    public static readonly SeedUser Ada = User(1, Contoso, "dev-ada", "Ada Admin", OrganizationRole.Admin, "IT");
    public static readonly SeedUser Ben = User(2, Contoso, "dev-ben", "Ben Projektleiter", OrganizationRole.Member, "Digital");
    public static readonly SeedUser Clara = User(3, Contoso, "dev-clara", "Clara Editor", OrganizationRole.Member, "Digital");
    public static readonly SeedUser David = User(4, Contoso, "dev-david", "David Mitglied", OrganizationRole.Member, "Marketing");
    public static readonly SeedUser Eva = User(5, Contoso, "dev-eva", "Eva Viewer", OrganizationRole.Member, "Controlling");
    public static readonly SeedUser Gina = User(6, Contoso, "dev-gina", "Gina Gast", OrganizationRole.Member, null);
    public static readonly SeedUser Felix = User(7, Contoso, "dev-felix", "Felix ohne Projekt", OrganizationRole.Member, "Einkauf");
    public static readonly SeedUser Fritz = User(8, Fabrikam, "dev-fritz", "Fritz Fabrikam", OrganizationRole.Admin, "IT");

    public static readonly IReadOnlyList<SeedOrganization> Organizations = [Contoso, Fabrikam];

    public static readonly IReadOnlyList<SeedUser> Users = [Ada, Ben, Clara, David, Eva, Gina, Felix, Fritz];

    public static readonly SeedTeam PlatformTeam = new(
        Guid.Parse("01920000-0000-7000-8000-000000000301"), Contoso.Id, "Plattform", "Betrieb und Weiterentwicklung der Plattform",
        [(Ben.Id, TeamRole.Owner), (Clara.Id, TeamRole.Member)]);

    public static readonly SeedTeam SalesTeam = new(
        Guid.Parse("01920000-0000-7000-8000-000000000302"), Contoso.Id, "Vertrieb", "Vertrieb und Kundenbetreuung",
        [(David.Id, TeamRole.Member)]);

    public static readonly SeedTeam FabrikamTeam = new(
        Guid.Parse("01920000-0000-7000-8000-000000000303"), Fabrikam.Id, "Fabrikam Intern", "Team der zweiten Organisation",
        [(Fritz.Id, TeamRole.Owner)]);

    public static readonly IReadOnlyList<SeedTeam> Teams = [PlatformTeam, SalesTeam, FabrikamTeam];

    public static readonly SeedProject IntranetProject = new(
        Guid.Parse("01920000-0000-7000-8000-000000000401"), Contoso.Id, "Intranet-Relaunch", Ben.Id,
        [
            (Ben.Id, ProjectRole.Admin),
            (Clara.Id, ProjectRole.Editor),
            (David.Id, ProjectRole.Member),
            (Eva.Id, ProjectRole.Viewer),
            (Gina.Id, ProjectRole.Guest),
        ]);

    public static readonly SeedProject FabrikamProject = new(
        Guid.Parse("01920000-0000-7000-8000-000000000402"), Fabrikam.Id, "Fabrikam Portal", Fritz.Id,
        [(Fritz.Id, ProjectRole.Admin)]);

    public static readonly IReadOnlyList<SeedProject> Projects = [IntranetProject, FabrikamProject];

    public static SeedOrganization OrganizationOf(SeedUser user) => Organizations.Single(o => o.Id == user.OrganizationId);

    private static SeedUser User(int number, SeedOrganization organization, string objectId, string displayName, string role, string? department) =>
        new(
            Guid.Parse($"01920000-0000-7000-8000-{100 + number:D12}"),
            organization.Id,
            objectId,
            displayName,
            $"{objectId.Replace("dev-", string.Empty)}@{organization.Slug}.example.invalid",
            role,
            department);
}
