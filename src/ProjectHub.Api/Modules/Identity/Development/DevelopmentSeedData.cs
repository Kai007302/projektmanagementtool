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

    /// <summary>A task; <see cref="StartDay"/> and <see cref="DueDay"/> count days from the day of seeding.</summary>
    public sealed record SeedTask(
        Guid Id, SeedProject Project, Guid? ParentTaskId, string Title, string Status, string Priority, Guid? AssigneeId, Guid CreatorId,
        int? StartDay = null, int? DueDay = null, short Progress = 0);

    public sealed record SeedSpace(Guid Id, Guid OrganizationId, string Name, string Description);

    public sealed record SeedArticle(
        Guid Id, Guid OrganizationId, Guid? SpaceId, string Title, string Slug, string ArticleType, string Summary,
        string Status, string Visibility, Guid OwnerId, IReadOnlyList<string> Tags, string ContentJson);

    public sealed record SeedRelation(Guid SourceId, Guid TargetId, string RelationType);

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

    public static readonly SeedTask ConceptTask = Task(1, IntranetProject, null, "Konzept abstimmen", "done", "high", Ben.Id, Ben.Id) with { StartDay = -14, DueDay = -5, Progress = 100 };
    public static readonly SeedTask DesignTask = Task(2, IntranetProject, null, "Design erstellen", "in_progress", "normal", Clara.Id, Ben.Id) with { StartDay = -4, DueDay = 10, Progress = 30 };
    public static readonly SeedTask StartPageTask = Task(3, IntranetProject, DesignTask.Id, "Startseite gestalten", "in_progress", "normal", Clara.Id, Clara.Id) with { StartDay = -4, DueDay = 3, Progress = 50 };
    public static readonly SeedTask NavigationTask = Task(4, IntranetProject, DesignTask.Id, "Navigation entwerfen", "todo", "low", David.Id, Clara.Id) with { StartDay = 4, DueDay = 10 };
    public static readonly SeedTask ContentTask = Task(5, IntranetProject, null, "Inhalte migrieren", "todo", "urgent", null, Ben.Id);
    public static readonly SeedTask FabrikamTask = Task(6, FabrikamProject, null, "Portal planen", "todo", "normal", Fritz.Id, Fritz.Id);

    public static readonly IReadOnlyList<SeedTask> Tasks = [ConceptTask, DesignTask, StartPageTask, NavigationTask, ContentTask, FabrikamTask];

    /// <summary>Gantt dependencies (source, target, type) of the synthetic tasks.</summary>
    public static readonly IReadOnlyList<(Guid Id, SeedTask Source, SeedTask Target, string Type)> Dependencies =
    [
        (Guid.Parse("01920000-0000-7000-8000-000000000601"), ConceptTask, DesignTask, "finish_to_start"),
        (Guid.Parse("01920000-0000-7000-8000-000000000602"), StartPageTask, NavigationTask, "finish_to_start"),
    ];

    /// <summary>Gantt milestones; <c>Day</c> counts days from the day of seeding.</summary>
    public static readonly IReadOnlyList<(Guid Id, SeedProject Project, string Name, int Day)> Milestones =
    [
        (Guid.Parse("01920000-0000-7000-8000-000000000651"), IntranetProject, "Go-live Intranet", 21),
    ];

    public static readonly SeedSpace PlatformSpace = new(
        Guid.Parse("01920000-0000-7000-8000-000000000601"), Contoso.Id, "IT & Plattform", "Betrieb, Werkzeuge und Richtlinien der Plattform");

    public static readonly SeedSpace MethodsSpace = new(
        Guid.Parse("01920000-0000-7000-8000-000000000602"), Contoso.Id, "Projektmethodik", "Wie wir Projekte planen und durchführen");

    public static readonly SeedSpace FabrikamSpace = new(
        Guid.Parse("01920000-0000-7000-8000-000000000603"), Fabrikam.Id, "Fabrikam Wissen", "Wissen der Fabrikam-Organisation");

    public static readonly IReadOnlyList<SeedSpace> Spaces = [PlatformSpace, MethodsSpace, FabrikamSpace];

    public static readonly SeedArticle KickoffArticle = Article(
        1, Contoso, MethodsSpace, "Projekt-Kickoff durchführen", "how_to", "Ablauf und Agenda für den Start eines Projekts.",
        "published", "organization", Ben, ["Projektstart", "Meeting"],
        """{"blocks":[{"type":"heading","level":2,"text":"Ziel"},{"type":"paragraph","text":"Im Kickoff einigen sich alle Beteiligten auf Ziel, Umfang und Rollen."},{"type":"numbered_list","items":["Ziele vorstellen","Rollen klären","Meilensteine festlegen","Nächste Schritte vereinbaren"]},{"type":"callout","tone":"info","text":"Das Protokoll gehört in das Projekt als Aufgabe."}]}""");

    public static readonly SeedArticle RolesArticle = Article(
        2, Contoso, MethodsSpace, "Rollen im Projekt", "glossary", "Was Admin, Editor, Mitglied, Leser und Gast im Projekt dürfen.",
        "published", "organization", Ben, ["Rollen", "Berechtigungen"],
        """{"blocks":[{"type":"paragraph","text":"Jedes Projektmitglied hat genau eine Rolle."},{"type":"bullet_list","items":["Admin: verwaltet Mitglieder und Projekt","Editor: ändert Projektdaten","Mitglied: arbeitet an Aufgaben","Leser: liest mit","Gast: liest eingeschränkt mit"]}]}""");

    public static readonly SeedArticle ChecklistArticle = Article(
        3, Contoso, MethodsSpace, "Checkliste Projektabschluss", "checklist", "Was vor dem Abschluss eines Projekts erledigt sein muss.",
        "published", "organization", Clara, ["Projektabschluss"],
        """{"blocks":[{"type":"checklist","items":[{"text":"Alle Aufgaben erledigt oder übergeben","checked":false},{"text":"Lessons Learned dokumentiert","checked":false},{"text":"Wissen im Knowledge Hub abgelegt","checked":false}]}]}""");

    public static readonly SeedArticle DeploymentArticle = Article(
        4, Contoso, PlatformSpace, "Deployment-Prozess", "process", "Wie Änderungen von der Entwicklung in die Produktion gelangen.",
        "published", "organization", Ada, ["Deployment", "Betrieb"],
        """{"blocks":[{"type":"heading","level":2,"text":"Ablauf"},{"type":"numbered_list","items":["Pull Request mit grüner CI","Review durch zweite Person","Freigabe durch Plattform-Team","Deployment im Wartungsfenster"]},{"type":"code","language":"bash","code":"dotnet test && npm run build"},{"type":"callout","tone":"warning","text":"Datenbankmigrationen in Produktion nur nach Freigabe."}]}""");

    public static readonly SeedArticle IncidentArticle = Article(
        5, Contoso, PlatformSpace, "Störungen melden", "faq", "Wer bei einer Störung wann informiert wird.",
        "published", "organization", Ada, ["Betrieb", "Support"],
        """{"blocks":[{"type":"heading","level":3,"text":"Wen rufe ich an?"},{"type":"paragraph","text":"Zuerst den Service Desk, außerhalb der Geschäftszeiten die Rufbereitschaft."},{"type":"quote","text":"Lieber einmal zu viel melden als einmal zu wenig."}]}""");

    public static readonly SeedArticle PricingArticle = Article(
        6, Contoso, null, "Preisliste Vertrieb 2027", "policy", "Interne Preise und Rabattregeln.",
        "published", "restricted", Ada, ["Vertrieb"],
        """{"blocks":[{"type":"paragraph","text":"Vertrauliche Rabattstaffeln für das Vertriebsteam."}]}""");

    public static readonly SeedArticle StyleGuideDraft = Article(
        7, Contoso, MethodsSpace, "Styleguide Intranet", "best_practice", "Entwurf für Gestaltungsregeln im Intranet.",
        "draft", "organization", Clara, ["Design"],
        """{"blocks":[{"type":"paragraph","text":"Farben, Schriften und Bildsprache für das neue Intranet."},{"type":"project_reference","projectId":"01920000-0000-7000-8000-000000000401"}]}""");

    public static readonly SeedArticle FabrikamArticle = Article(
        8, Fabrikam, FabrikamSpace, "Fabrikam Onboarding", "how_to", "Erste Schritte bei Fabrikam.",
        "published", "organization", Fritz, ["Onboarding"],
        """{"blocks":[{"type":"paragraph","text":"Willkommen bei Fabrikam."}]}""");

    public static readonly IReadOnlyList<SeedArticle> Articles =
        [KickoffArticle, RolesArticle, ChecklistArticle, DeploymentArticle, IncidentArticle, PricingArticle, StyleGuideDraft, FabrikamArticle];

    public static readonly IReadOnlyList<SeedRelation> Relations =
    [
        new(KickoffArticle.Id, RolesArticle.Id, "REFERENCES"),
        new(ChecklistArticle.Id, KickoffArticle.Id, "RELATED"),
        new(IncidentArticle.Id, DeploymentArticle.Id, "RELATED"),
        new(StyleGuideDraft.Id, KickoffArticle.Id, "REQUIRES"),
    ];

    /// <summary>The restricted price list is shared with the sales team only.</summary>
    public static readonly IReadOnlyList<(Guid ArticleId, string PrincipalType, Guid PrincipalId, string Permission)> KnowledgeGrants =
    [
        (PricingArticle.Id, "team", SalesTeam.Id, "view"),
    ];

    public static readonly IReadOnlyList<(Guid ArticleId, string ResourceType, Guid ResourceId, Guid CreatedBy)> KnowledgeReferences =
    [
        (KickoffArticle.Id, "project", IntranetProject.Id, Ben.Id),
        (DeploymentArticle.Id, "team", PlatformTeam.Id, Ada.Id),
    ];

    public static SeedOrganization OrganizationOf(SeedUser user) => Organizations.Single(o => o.Id == user.OrganizationId);

    private static SeedArticle Article(
        int number, SeedOrganization organization, SeedSpace? space, string title, string type, string summary,
        string status, string visibility, SeedUser owner, IReadOnlyList<string> tags, string contentJson) =>
        new(
            Guid.Parse($"01920000-0000-7000-8000-{700 + number:D12}"), organization.Id, space?.Id, title,
            Knowledge.KnowledgeArticleService.Slugify(title), type, summary, status, visibility, owner.Id, tags, contentJson);

    private static SeedTask Task(int number, SeedProject project, Guid? parentTaskId, string title, string status, string priority, Guid? assigneeId, Guid creatorId) =>
        new(Guid.Parse($"01920000-0000-7000-8000-{500 + number:D12}"), project, parentTaskId, title, status, priority, assigneeId, creatorId);

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
