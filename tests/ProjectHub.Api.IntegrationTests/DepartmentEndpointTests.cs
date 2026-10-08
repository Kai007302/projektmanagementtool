using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Modules.Departments;
using ProjectHub.Api.Modules.Knowledge;
using ProjectHub.Api.Modules.Projects;
using ProjectHub.Api.Modules.Users;
using static ProjectHub.Api.Modules.Identity.Development.DevelopmentSeedData;

namespace ProjectHub.Api.IntegrationTests;

/// <summary>Departments (ADR 0021): their management and how they decide who sees projects and knowledge.</summary>
[Collection(InfrastructureCollection.Name)]
public sealed class DepartmentEndpointTests(InfrastructureFixture infrastructure) : ApiTestBase(infrastructure)
{
    // Managing departments

    [Fact]
    public async Task Departments_list_only_the_callers_organization_with_their_role()
    {
        var page = await As(Clara).GetFromJsonAsync<PagedResponse<DepartmentSummary>>("/api/v1/departments?limit=100");

        Assert.Contains(page!.Items, d => d.Id == PlatformDepartment.Id && d.MyRole == "member" && !d.CanManage);
        Assert.Contains(page.Items, d => d.Id == SalesDepartment.Id && d.MyRole == null);
        Assert.DoesNotContain(page.Items, d => d.Id == FabrikamDepartment.Id);
    }

    [Fact]
    public async Task Me_names_the_own_departments_working_ones_first()
    {
        var me = await As(Gina).GetFromJsonAsync<MeResponse>("/api/v1/me");
        var ben = await As(Ben).GetFromJsonAsync<MeResponse>("/api/v1/me");

        Assert.Equal([new MyDepartment(GeneralDepartment.Id, "Allgemein", "guest")], me!.Departments);
        Assert.Contains(ben!.Departments, d => d.Id == PlatformDepartment.Id && d.Role == "lead");
    }

    [Fact]
    public async Task Members_are_shown_to_the_department_and_admins_only()
    {
        var byMember = await As(Clara).GetFromJsonAsync<DepartmentDetails>($"/api/v1/departments/{PlatformDepartment.Id}");
        var byOutsider = await As(Felix).GetFromJsonAsync<DepartmentDetails>($"/api/v1/departments/{PlatformDepartment.Id}");
        var byAdmin = await As(Ada).GetFromJsonAsync<DepartmentDetails>($"/api/v1/departments/{PlatformDepartment.Id}");

        Assert.Contains(byMember!.Members, m => m.UserId == Ben.Id && m.Role == "lead");
        Assert.Empty(byOutsider!.Members);
        Assert.Equal("Plattform", byOutsider.Name);
        Assert.True(byAdmin!.CanManage);
        Assert.NotEmpty(byAdmin.Members);
    }

    [Fact]
    public async Task Department_of_another_organization_is_not_found()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await As(Fritz).GetAsync($"/api/v1/departments/{PlatformDepartment.Id}")).StatusCode);
    }

    [Fact]
    public async Task Organization_admin_creates_department_and_it_is_audited()
    {
        var department = await CreateDepartmentAsync();

        Assert.Equal(1L, await ScalarAsync("select count(*) from audit_log where action = 'DepartmentCreated' and resource_id = $1", department.Id));
    }

    [Theory]
    [InlineData("dev-ben")]
    [InlineData("dev-eva")]
    [InlineData("dev-gina")]
    public async Task Only_organization_admins_create_departments(string objectId)
    {
        var user = Users.Single(u => u.ObjectId == objectId);

        var response = await As(user).PostAsJsonAsync("/api/v1/departments", new CreateDepartmentRequest(UniqueName(), null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Department_names_are_unique_per_organization_and_required()
    {
        var name = UniqueName();
        await As(Ada).PostAsJsonAsync("/api/v1/departments", new CreateDepartmentRequest(name, null));

        var duplicate = await As(Ada).PostAsJsonAsync("/api/v1/departments", new CreateDepartmentRequest(name.ToUpperInvariant(), null));
        var otherOrganization = await As(Fritz).PostAsJsonAsync("/api/v1/departments", new CreateDepartmentRequest(name, null));
        var empty = await As(Ada).PostAsJsonAsync("/api/v1/departments", new CreateDepartmentRequest("  ", null));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.Created, otherOrganization.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }

    [Fact]
    public async Task Lead_manages_members_and_every_change_is_audited()
    {
        var department = await CreateDepartmentAsync();
        await As(Ada).PostAsJsonAsync($"/api/v1/departments/{department.Id}/members", new AddDepartmentMemberRequest(Ben.Id, "lead"));

        var added = await As(Ben).PostAsJsonAsync($"/api/v1/departments/{department.Id}/members", new AddDepartmentMemberRequest(Felix.Id, null));
        var promoted = await As(Ben).PatchAsJsonAsync($"/api/v1/departments/{department.Id}/members/{Felix.Id}", new ChangeDepartmentMemberRequest("guest"));
        var removed = await As(Ben).DeleteAsync($"/api/v1/departments/{department.Id}/members/{Felix.Id}");

        Assert.Equal(HttpStatusCode.NoContent, added.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, promoted.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(2L, await ScalarAsync("select count(*) from audit_log where action = 'DepartmentMemberAdded' and resource_id = $1", department.Id));
        Assert.Equal(1L, await ScalarAsync("select count(*) from audit_log where action = 'DepartmentMemberRoleChanged' and resource_id = $1", department.Id));
        Assert.Equal(1L, await ScalarAsync("select count(*) from audit_log where action = 'DepartmentMemberRemoved' and resource_id = $1", department.Id));
    }

    [Fact]
    public async Task Members_guests_and_outsiders_cannot_manage_members()
    {
        var byMember = await As(Clara).PostAsJsonAsync($"/api/v1/departments/{PlatformDepartment.Id}/members", new AddDepartmentMemberRequest(Eva.Id, null));
        var byGuest = await As(Gina).PostAsJsonAsync($"/api/v1/departments/{GeneralDepartment.Id}/members", new AddDepartmentMemberRequest(Felix.Id, null));
        var byOtherOrganization = await As(Fritz).PostAsJsonAsync($"/api/v1/departments/{PlatformDepartment.Id}/members", new AddDepartmentMemberRequest(Fritz.Id, null));

        Assert.Equal(HttpStatusCode.Forbidden, byMember.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, byGuest.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, byOtherOrganization.StatusCode);
    }

    [Fact]
    public async Task Members_must_be_people_of_the_organization_with_a_valid_role_and_only_once()
    {
        var department = await CreateDepartmentAsync();
        var url = $"/api/v1/departments/{department.Id}/members";

        Assert.Equal(HttpStatusCode.BadRequest, (await As(Ada).PostAsJsonAsync(url, new AddDepartmentMemberRequest(Fritz.Id, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await As(Ada).PostAsJsonAsync(url, new AddDepartmentMemberRequest(Eva.Id, "owner"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await As(Ada).PostAsJsonAsync(url, new AddDepartmentMemberRequest(Eva.Id, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await As(Ada).PostAsJsonAsync(url, new AddDepartmentMemberRequest(Eva.Id, null))).StatusCode);
    }

    [Fact]
    public async Task Leads_rename_their_department_but_only_admins_connect_an_entra_group()
    {
        var department = await CreateDepartmentAsync();
        await As(Ada).PostAsJsonAsync($"/api/v1/departments/{department.Id}/members", new AddDepartmentMemberRequest(Ben.Id, "lead"));

        var group = await As(Ben).PatchAsJsonAsync($"/api/v1/departments/{department.Id}", new { version = department.Version, entraGroupId = "group-1" });
        var renamed = await As(Ben).PatchAsJsonAsync($"/api/v1/departments/{department.Id}", new { version = department.Version, name = department.Name + " neu" });
        var connected = await As(Ada).PatchAsJsonAsync($"/api/v1/departments/{department.Id}", new { version = department.Version + 1, entraGroupId = "group-1" });

        Assert.Equal(HttpStatusCode.Forbidden, group.StatusCode);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, connected.StatusCode);
        Assert.Equal("group-1", (await connected.Content.ReadFromJsonAsync<DepartmentSummary>())!.EntraGroupId);
    }

    [Fact]
    public async Task Only_empty_departments_can_be_deleted()
    {
        var empty = await CreateDepartmentAsync();

        Assert.Equal(HttpStatusCode.Conflict, (await As(Ada).DeleteAsync($"/api/v1/departments/{GeneralDepartment.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await As(Ben).DeleteAsync($"/api/v1/departments/{empty.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await As(Ada).DeleteAsync($"/api/v1/departments/{empty.Id}")).StatusCode);
    }

    [Fact]
    public async Task Admins_and_leads_see_who_waits_for_a_department()
    {
        var waiting = Guid.CreateVersion7();
        await ScalarAsync(
            "insert into app_user (id, organization_id, entra_object_id, email, display_name) values ($1, $2, $3, $4, 'Neu Ohne Abteilung') returning 1",
            waiting, Contoso.Id, $"dev-new-{waiting:N}", $"{waiting:N}@contoso-dev.example.invalid");

        var byLead = await As(Ben).GetFromJsonAsync<List<UserResponse>>("/api/v1/departments/unassigned");
        var byMember = await As(Eva).GetAsync("/api/v1/departments/unassigned");

        Assert.Contains(byLead!, u => u.Id == waiting);
        Assert.DoesNotContain(byLead!, u => u.Id == Felix.Id);
        Assert.Equal(HttpStatusCode.Forbidden, byMember.StatusCode);
    }

    // Projects

    [Fact]
    public async Task New_projects_go_to_the_first_department_and_its_members_read_them()
    {
        var project = await CreateProjectAsync(Ben);

        Assert.Equal(GeneralDepartment.Id, project.DepartmentId);
        Assert.Equal("department", project.Visibility);
        Assert.Equal(HttpStatusCode.OK, (await As(Eva).GetAsync($"/api/v1/projects/{project.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await As(Felix).GetAsync($"/api/v1/projects/{project.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await As(Gina).GetAsync($"/api/v1/projects/{project.Id}")).StatusCode);
        var byReader = await As(Eva).PatchAsJsonAsync($"/api/v1/projects/{project.Id}", new { version = project.Version, name = "Umbenannt" });
        Assert.Equal(HttpStatusCode.Forbidden, byReader.StatusCode);
        var tasks = await As(Eva).PostAsJsonAsync($"/api/v1/projects/{project.Id}/tasks", NewTask("Darf nicht"));
        Assert.Equal(HttpStatusCode.Forbidden, tasks.StatusCode);
    }

    [Fact]
    public async Task Private_projects_stay_with_their_members_and_organization_wide_ones_are_read_by_all()
    {
        var hidden = await CreateAsync(Ben, GeneralDepartment.Id, "private");
        var shared = await CreateAsync(Ben, PlatformDepartment.Id, "organization");

        Assert.Equal(HttpStatusCode.NotFound, (await As(Eva).GetAsync($"/api/v1/projects/{hidden.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await As(Felix).GetAsync($"/api/v1/projects/{shared.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await As(Gina).GetAsync($"/api/v1/projects/{shared.Id}")).StatusCode);
        var list = await As(Felix).GetFromJsonAsync<PagedResponse<ProjectSummary>>("/api/v1/projects?limit=100");
        Assert.Contains(list!.Items, p => p.Id == shared.Id && p.MyRole == null);
        Assert.DoesNotContain(list.Items, p => p.Id == hidden.Id);
    }

    [Fact]
    public async Task Leads_manage_every_project_of_their_department_without_being_members()
    {
        var project = await CreateAsync(Clara, PlatformDepartment.Id, "private");

        var details = await As(Ben).GetFromJsonAsync<ProjectDetails>($"/api/v1/projects/{project.Id}");
        var added = await As(Ben).PostAsJsonAsync($"/api/v1/projects/{project.Id}/members", new AddProjectMemberRequest(Eva.Id, "viewer"));

        Assert.Equal(new ProjectCapabilities(true, true, true, CanShareWithOrganization: true), details!.Capabilities);
        Assert.Equal("Plattform", details.DepartmentName);
        Assert.Equal(HttpStatusCode.NoContent, added.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await As(David).GetAsync($"/api/v1/projects/{project.Id}")).StatusCode);
    }

    [Fact]
    public async Task Only_leads_and_members_create_projects_in_a_department()
    {
        var byGuest = await As(Gina).PostAsJsonAsync("/api/v1/projects", new CreateProjectRequest("Gast", null, null, null, null));
        var byOutsider = await As(Felix).PostAsJsonAsync("/api/v1/projects", new CreateProjectRequest("Fremd", null, null, null, null, GeneralDepartment.Id));
        var otherOrganization = await As(Ben).PostAsJsonAsync("/api/v1/projects", new CreateProjectRequest("Fremd", null, null, null, null, FabrikamDepartment.Id));
        var own = await As(Felix).PostAsJsonAsync("/api/v1/projects", new CreateProjectRequest("Einkauf", null, null, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, byGuest.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, byOutsider.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, otherOrganization.StatusCode);
        Assert.Equal(PurchasingDepartment.Id, (await own.Content.ReadFromJsonAsync<ProjectSummary>())!.DepartmentId);
    }

    [Fact]
    public async Task Visibility_and_department_change_only_with_manage_and_are_audited()
    {
        var project = await CreateProjectAsync(Ben, (Clara, "editor"));

        var byEditor = await As(Clara).PatchAsJsonAsync($"/api/v1/projects/{project.Id}", new { version = project.Version, visibility = "organization" });
        var invalid = await As(Ben).PatchAsJsonAsync($"/api/v1/projects/{project.Id}", new { version = project.Version, visibility = "public" });
        var byAdmin = await As(Ben).PatchAsJsonAsync($"/api/v1/projects/{project.Id}", new { version = project.Version, visibility = "private" });
        var moved = await As(Ben).PatchAsJsonAsync($"/api/v1/projects/{project.Id}", new { version = project.Version + 1, departmentId = PlatformDepartment.Id });
        var notMine = await As(Ben).PatchAsJsonAsync($"/api/v1/projects/{project.Id}", new { version = project.Version + 2, departmentId = PurchasingDepartment.Id });

        Assert.Equal(HttpStatusCode.Forbidden, byEditor.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.OK, byAdmin.StatusCode);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, notMine.StatusCode);
        Assert.Equal(1L, await ScalarAsync("select count(*) from audit_log where action = 'ProjectVisibilityChanged' and resource_id = $1", project.Id));
        Assert.Equal(1L, await ScalarAsync("select count(*) from audit_log where action = 'ProjectMoved' and resource_id = $1", project.Id));
    }

    [Fact]
    public async Task Only_admins_and_leads_share_projects_with_the_organization()
    {
        var project = await CreateAsync(Clara, PlatformDepartment.Id, "department");

        var byMember = await As(Clara).PatchAsJsonAsync($"/api/v1/projects/{project.Id}", new { version = project.Version, visibility = "organization" });
        var createdByMember = await As(Clara).PostAsJsonAsync(
            "/api/v1/projects", new CreateProjectRequest("Für alle", null, null, null, null, PlatformDepartment.Id, "organization"));
        var capabilities = (await As(Clara).GetFromJsonAsync<ProjectDetails>($"/api/v1/projects/{project.Id}"))!.Capabilities;
        var byLead = await As(Ben).PatchAsJsonAsync($"/api/v1/projects/{project.Id}", new { version = project.Version, visibility = "organization" });

        Assert.Equal(HttpStatusCode.Forbidden, byMember.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, createdByMember.StatusCode);
        Assert.Equal(new ProjectCapabilities(true, true, true, CanShareWithOrganization: false), capabilities);
        Assert.Equal(HttpStatusCode.OK, byLead.StatusCode);
    }

    [Fact]
    public async Task Projects_can_be_listed_per_department()
    {
        var general = await CreateProjectAsync(Ben);
        var platform = await CreateAsync(Ben, PlatformDepartment.Id, "department");

        var list = await As(Ben).GetFromJsonAsync<PagedResponse<ProjectSummary>>($"/api/v1/projects?limit=100&departmentId={PlatformDepartment.Id}");

        Assert.Contains(list!.Items, p => p.Id == platform.Id);
        Assert.DoesNotContain(list.Items, p => p.Id == general.Id);
    }

    [Fact]
    public async Task Organization_admins_opening_a_foreign_project_is_audited()
    {
        var foreign = await CreateAsync(Clara, PlatformDepartment.Id, "private");
        var own = await CreateProjectAsync(Ada);

        Assert.Equal(HttpStatusCode.OK, (await As(Ada).GetAsync($"/api/v1/projects/{foreign.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await As(Ada).GetAsync($"/api/v1/projects/{own.Id}")).StatusCode);

        Assert.Equal(1L, await ScalarAsync("select count(*) from audit_log where action = 'OrganizationAdminAccess' and resource_id = $1", foreign.Id));
        Assert.Equal(0L, await ScalarAsync("select count(*) from audit_log where action = 'OrganizationAdminAccess' and resource_id = $1", own.Id));
    }

    // Knowledge

    [Fact]
    public async Task Published_department_knowledge_is_read_by_the_department_only()
    {
        var article = await PublishedArticleAsync(Ben, GeneralDepartment.Id);

        Assert.Equal("department", article.Article.Visibility);
        Assert.Equal(HttpStatusCode.OK, (await As(Eva).GetAsync($"/api/v1/knowledge/articles/{article.Article.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await As(Felix).GetAsync($"/api/v1/knowledge/articles/{article.Article.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await As(Gina).GetAsync($"/api/v1/knowledge/articles/{article.Article.Id}")).StatusCode);
    }

    [Fact]
    public async Task Only_leads_share_knowledge_with_the_whole_organization()
    {
        var article = await PublishedArticleAsync(Clara, PlatformDepartment.Id);
        var url = $"/api/v1/knowledge/articles/{article.Article.Id}";

        var byOwner = await As(Clara).PatchAsJsonAsync(url, new { version = article.Article.Version, visibility = "organization" });
        var byLead = await As(Ben).PatchAsJsonAsync(url, new { version = article.Article.Version, visibility = "organization" });
        var created = await As(Clara).PostAsJsonAsync("/api/v1/knowledge/articles", NewArticle("Für alle") with { Visibility = "organization" });

        Assert.Equal(HttpStatusCode.Forbidden, byOwner.StatusCode);
        Assert.Equal(HttpStatusCode.OK, byLead.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await As(Felix).GetAsync(url)).StatusCode);
        Assert.False((await As(Clara).GetFromJsonAsync<ArticleDetails>(url))!.Capabilities.CanShareWithOrganization);
        Assert.True((await As(Ben).GetFromJsonAsync<ArticleDetails>(url))!.Capabilities.CanShareWithOrganization);
    }

    [Fact]
    public async Task Leads_administer_drafts_of_their_department()
    {
        var draft = await CreateArticleAsync(Clara, PlatformDepartment.Id);

        var read = await As(Ben).GetFromJsonAsync<ArticleDetails>($"/api/v1/knowledge/articles/{draft.Article.Id}");

        Assert.Equal(new KnowledgeCapabilities(true, true, true), read!.Capabilities);
        Assert.Equal(HttpStatusCode.NotFound, (await As(David).GetAsync($"/api/v1/knowledge/articles/{draft.Article.Id}")).StatusCode);
    }

    [Fact]
    public async Task Knowledge_shared_with_a_department_reaches_its_members_but_not_its_guests()
    {
        var article = await CreateArticleAsync(Ben, PlatformDepartment.Id, "restricted");
        var granted = await As(Ben).PutAsJsonAsync($"/api/v1/knowledge/articles/{article.Article.Id}/permissions",
            new SetPermissionsRequest([new PermissionEntry("department", GeneralDepartment.Id, "view")]));

        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await As(Eva).GetAsync($"/api/v1/knowledge/articles/{article.Article.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await As(Gina).GetAsync($"/api/v1/knowledge/articles/{article.Article.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await As(David).GetAsync($"/api/v1/knowledge/articles/{PricingArticle.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await As(Felix).GetAsync($"/api/v1/knowledge/articles/{PricingArticle.Id}")).StatusCode);
    }

    [Fact]
    public async Task Spaces_belong_to_a_department_and_its_leads_create_them()
    {
        var byLead = await As(Ben).PostAsJsonAsync("/api/v1/knowledge/spaces", new CreateSpaceRequest(UniqueName(), null, PlatformDepartment.Id));
        var byMember = await As(Clara).PostAsJsonAsync("/api/v1/knowledge/spaces", new CreateSpaceRequest(UniqueName(), null, PlatformDepartment.Id));
        var felixSpaces = await As(Felix).GetFromJsonAsync<List<SpaceResponse>>("/api/v1/knowledge/spaces");

        Assert.Equal(HttpStatusCode.Created, byLead.StatusCode);
        Assert.Equal(PlatformDepartment.Id, (await byLead.Content.ReadFromJsonAsync<SpaceResponse>())!.DepartmentId);
        Assert.Equal(HttpStatusCode.Forbidden, byMember.StatusCode);
        Assert.DoesNotContain(felixSpaces!, s => s.Id == PlatformSpace.Id);
    }

    [Fact]
    public async Task Articles_can_only_use_spaces_of_their_department()
    {
        var response = await As(Ben).PostAsJsonAsync("/api/v1/knowledge/articles",
            NewArticle("Falscher Bereich") with { SpaceId = MethodsSpace.Id, DepartmentId = PlatformDepartment.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Search_and_galaxy_narrow_to_one_department()
    {
        var article = await PublishedArticleAsync(Ben, PlatformDepartment.Id);

        var platform = await As(Ben).GetFromJsonAsync<PagedResponse<ArticleSummary>>($"/api/v1/knowledge/articles?limit=100&departmentId={PlatformDepartment.Id}");
        var general = await As(Ben).GetFromJsonAsync<PagedResponse<ArticleSummary>>($"/api/v1/knowledge/articles?limit=100&departmentId={GeneralDepartment.Id}");
        var galaxy = await As(Ben).GetFromJsonAsync<KnowledgeGraph>($"/api/v1/knowledge/graph?departmentId={GeneralDepartment.Id}");

        Assert.Contains(platform!.Items, a => a.Id == article.Article.Id && a.DepartmentName == "Plattform");
        Assert.DoesNotContain(general!.Items, a => a.Id == article.Article.Id);
        Assert.DoesNotContain(galaxy!.Nodes, n => n.Id == article.Article.Id);
        Assert.Contains(galaxy.Nodes, n => n.Id == KickoffArticle.Id);
    }

    [Fact]
    public async Task Organization_admins_opening_foreign_knowledge_is_audited()
    {
        var article = await CreateArticleAsync(Clara, PlatformDepartment.Id);

        Assert.Equal(HttpStatusCode.OK, (await As(Ada).GetAsync($"/api/v1/knowledge/articles/{article.Article.Id}")).StatusCode);

        Assert.Equal(1L, await ScalarAsync("select count(*) from audit_log where action = 'OrganizationAdminAccess' and resource_id = $1", article.Article.Id));
    }

    private async Task<DepartmentSummary> CreateDepartmentAsync()
    {
        var response = await As(Ada).PostAsJsonAsync("/api/v1/departments", new CreateDepartmentRequest(UniqueName(), null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DepartmentSummary>())!;
    }

    private async Task<ProjectSummary> CreateAsync(SeedUser owner, Guid departmentId, string visibility)
    {
        var response = await As(owner).PostAsJsonAsync("/api/v1/projects",
            new CreateProjectRequest($"Projekt {Guid.NewGuid():N}", null, null, null, null, departmentId, visibility));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProjectSummary>())!;
    }

    private async Task<ArticleDetails> CreateArticleAsync(SeedUser owner, Guid departmentId, string? visibility = null)
    {
        var response = await As(owner).PostAsJsonAsync("/api/v1/knowledge/articles",
            NewArticle($"Artikel {Guid.NewGuid():N}") with { DepartmentId = departmentId, Visibility = visibility });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ArticleDetails>())!;
    }

    private async Task<ArticleDetails> PublishedArticleAsync(SeedUser owner, Guid departmentId)
    {
        var article = await CreateArticleAsync(owner, departmentId);
        var published = await As(owner).PostAsJsonAsync(
            $"/api/v1/knowledge/articles/{article.Article.Id}/status", new ChangeStatusRequest(article.Article.Version, "published"));
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        return (await published.Content.ReadFromJsonAsync<ArticleDetails>())!;
    }

    private static CreateArticleRequest NewArticle(string title) =>
        new(title, "article", null, null, null, new JsonObject { ["blocks"] = new JsonArray() });

    private static string UniqueName() => $"Abteilung {Guid.NewGuid():N}";
}
