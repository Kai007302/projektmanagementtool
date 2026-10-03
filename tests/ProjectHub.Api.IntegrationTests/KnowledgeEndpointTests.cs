using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ProjectHub.Api.Infrastructure.Events;
using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Modules.Knowledge;
using static ProjectHub.Api.Modules.Identity.Development.DevelopmentSeedData;

namespace ProjectHub.Api.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class KnowledgeEndpointTests(InfrastructureFixture infrastructure) : ApiTestBase(infrastructure)
{
    private readonly MentionRecorder mentions = new();

    protected override ProjectHubApiFactory CreateFactory() =>
        new(Infrastructure.Postgres.GetConnectionString(), Infrastructure.Redis.GetConnectionString(), configure: builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IDomainEventHandler<UsersMentionedInKnowledgeComment>>(mentions)));

    [Fact]
    public async Task Published_articles_are_readable_in_the_whole_organization_with_only_visible_references()
    {
        var article = await GetAsync(Felix, KickoffArticle.Id);

        Assert.Equal("Projekt-Kickoff durchführen", article.Article.Title);
        Assert.Equal("heading", article.Content["blocks"]![0]!["type"]!.GetValue<string>());
        Assert.Equal(new KnowledgeCapabilities(false, false), article.Capabilities);
        Assert.Contains(article.Relations, r => r.ArticleId == RolesArticle.Id && r.Direction == "outgoing");
        Assert.Empty(article.References);
        Assert.Contains((await GetAsync(Eva, KickoffArticle.Id)).References, r => r.ResourceId == IntranetProject.Id && r.Title == "Intranet-Relaunch");
    }

    [Fact]
    public async Task Other_organizations_never_see_an_article()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await As(Fritz).GetAsync($"/api/v1/knowledge/articles/{KickoffArticle.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await As(Ada).GetAsync($"/api/v1/knowledge/articles/{FabrikamArticle.Id}")).StatusCode);
        Assert.DoesNotContain(await SearchAsync(Fritz, ""), a => a.Id == KickoffArticle.Id);
    }

    [Theory]
    [InlineData("dev-david", HttpStatusCode.OK)]
    [InlineData("dev-ada", HttpStatusCode.OK)]
    [InlineData("dev-eva", HttpStatusCode.NotFound)]
    [InlineData("dev-ben", HttpStatusCode.NotFound)]
    public async Task Restricted_articles_are_readable_only_with_a_grant(string objectId, HttpStatusCode expected)
    {
        var user = Users.Single(u => u.ObjectId == objectId);

        var response = await As(user).GetAsync($"/api/v1/knowledge/articles/{PricingArticle.Id}");

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(expected == HttpStatusCode.OK, (await SearchAsync(user, "Rabattstaffeln")).Any(a => a.Id == PricingArticle.Id));
    }

    [Fact]
    public async Task Drafts_are_visible_to_their_owner_and_grantees_only()
    {
        var draft = await CreateAsync(Clara, "Entwurf nur für Clara");

        Assert.Equal("draft", draft.Article.Status);
        Assert.Equal(HttpStatusCode.NotFound, (await As(Eva).GetAsync($"/api/v1/knowledge/articles/{draft.Article.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await As(Ada).GetAsync($"/api/v1/knowledge/articles/{draft.Article.Id}")).StatusCode);

        await SetPermissionsAsync(Clara, draft.Article.Id, new PermissionEntry("user", Eva.Id, "view"));

        Assert.Equal(HttpStatusCode.OK, (await As(Eva).GetAsync($"/api/v1/knowledge/articles/{draft.Article.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await As(Felix).GetAsync($"/api/v1/knowledge/articles/{draft.Article.Id}")).StatusCode);
    }

    [Fact]
    public async Task Readers_without_edit_rights_cannot_change_an_article()
    {
        var response = await As(Felix).PutAsJsonAsync(
            $"/api/v1/knowledge/articles/{KickoffArticle.Id}/content",
            new SaveContentRequest(1, Blocks(Paragraph("Übernommen")), null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await As(Felix).DeleteAsync($"/api/v1/knowledge/articles/{KickoffArticle.Id}")).StatusCode);
    }

    [Fact]
    public async Task Team_grants_give_edit_rights()
    {
        var article = await CreateAsync(Ada, "Plattform-Runbook");
        await SetPermissionsAsync(Ada, article.Article.Id, new PermissionEntry("team", PlatformTeam.Id, "edit"));

        var details = await GetAsync(Clara, article.Article.Id);
        var saved = await As(Clara).PutAsJsonAsync(
            $"/api/v1/knowledge/articles/{article.Article.Id}/content",
            new SaveContentRequest(details.Article.Version, Blocks(Paragraph("Neuer Text")), "Ergänzt"));

        Assert.Equal(new KnowledgeCapabilities(true, false), details.Capabilities);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await As(Clara).DeleteAsync($"/api/v1/knowledge/articles/{article.Article.Id}")).StatusCode);
    }

    [Fact]
    public async Task Creating_validates_title_type_space_and_content()
    {
        var client = As(Ben);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/knowledge/articles", NewArticle(" "))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/knowledge/articles", NewArticle("Titel") with { ArticleType = "roman" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/knowledge/articles", NewArticle("Titel") with { SpaceId = FabrikamSpace.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/knowledge/articles", NewArticle("Titel") with { Visibility = "public" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/knowledge/articles", NewArticle("Titel", Blocks(new JsonObject { ["type"] = "html", ["text"] = "<b>x</b>" })))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/knowledge/articles", NewArticle("Titel", Blocks(new JsonObject { ["type"] = "link", ["url"] = "javascript:alert(1)" })))).StatusCode);
    }

    [Fact]
    public async Task Content_references_must_point_to_visible_resources()
    {
        var hidden = await CreateProjectAsync(Ben);
        var reference = new JsonObject { ["type"] = "project_reference", ["projectId"] = hidden.Id.ToString() };

        var forClara = await As(Clara).PostAsJsonAsync("/api/v1/knowledge/articles", NewArticle("Verweis", Blocks(reference)));
        var forBen = await As(Ben).PostAsJsonAsync("/api/v1/knowledge/articles", NewArticle("Verweis", Blocks(reference.DeepClone())));

        Assert.Equal(HttpStatusCode.BadRequest, forClara.StatusCode);
        Assert.Equal(HttpStatusCode.Created, forBen.StatusCode);
    }

    [Fact]
    public async Task Slugs_are_unique_per_organization()
    {
        var first = await CreateAsync(Ben, "Größe & Übersicht");
        var second = await CreateAsync(Ben, "Größe & Übersicht");

        Assert.StartsWith("groesse-uebersicht", first.Article.Slug);
        Assert.NotEqual(first.Article.Slug, second.Article.Slug);
    }

    [Fact]
    public async Task Every_content_save_is_a_new_version_and_old_versions_can_be_restored()
    {
        var article = await CreateAsync(Ben, "Versioniert", Blocks(Paragraph("Erste Fassung")));
        var second = await SaveContentAsync(Ben, article, Blocks(Paragraph("Zweite Fassung")), "Überarbeitet");

        var versions = await As(Ben).GetFromJsonAsync<List<VersionSummary>>($"/api/v1/knowledge/articles/{article.Article.Id}/versions");
        var first = await As(Ben).GetFromJsonAsync<VersionDetails>($"/api/v1/knowledge/articles/{article.Article.Id}/versions/1");
        var restored = await As(Ben).PostAsJsonAsync(
            $"/api/v1/knowledge/articles/{article.Article.Id}/versions/1/restore", new RestoreVersionRequest(second.Article.Version));
        var current = (await restored.Content.ReadFromJsonAsync<ArticleDetails>())!;

        Assert.Equal(2, second.VersionNumber);
        Assert.Equal([2, 1], versions!.Select(v => v.VersionNumber));
        Assert.Equal("Überarbeitet", versions![0].ChangeNote);
        Assert.Equal("Erste Fassung", first!.Content["blocks"]![0]!["text"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        Assert.Equal(3, current.VersionNumber);
        Assert.Equal("Erste Fassung", current.Content["blocks"]![0]!["text"]!.GetValue<string>());
        Assert.Contains(await SearchAsync(Ben, "Fassung"), a => a.Id == article.Article.Id);
    }

    [Fact]
    public async Task Stale_content_saves_are_rejected()
    {
        var article = await CreateAsync(Ben, "Parallel bearbeitet");
        await SaveContentAsync(Ben, article, Blocks(Paragraph("Ben war schneller")), null);

        var stale = await As(Ben).PutAsJsonAsync(
            $"/api/v1/knowledge/articles/{article.Article.Id}/content",
            new SaveContentRequest(article.Article.Version, Blocks(Paragraph("Veraltet")), null));

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    [Fact]
    public async Task Status_changes_follow_the_lifecycle_and_need_the_right_permission()
    {
        var article = await CreateAsync(Ada, "Lebenszyklus");
        await SetPermissionsAsync(Ada, article.Article.Id, new PermissionEntry("user", Clara.Id, "edit"));

        var review = await ChangeStatusAsync(Clara, article.Article.Id, article.Article.Version, "review");
        var reviewed = (await review.Content.ReadFromJsonAsync<ArticleDetails>())!;
        var publishByEditor = await ChangeStatusAsync(Clara, article.Article.Id, reviewed.Article.Version, "published");
        var publish = await ChangeStatusAsync(Ada, article.Article.Id, reviewed.Article.Version, "published");
        var published = (await publish.Content.ReadFromJsonAsync<ArticleDetails>())!;
        var backToReview = await ChangeStatusAsync(Ada, article.Article.Id, published.Article.Version, "review");

        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, publishByEditor.StatusCode);
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        Assert.NotNull(published.Article.PublishedAt);
        Assert.Equal(HttpStatusCode.BadRequest, backToReview.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await As(Felix).GetAsync($"/api/v1/knowledge/articles/{article.Article.Id}")).StatusCode);
        Assert.Equal(1L, await ScalarAsync(
            "select count(*) from audit_log where resource_id = $1 and action = 'KnowledgeStatusChanged' and metadata->>'To' = 'published'", article.Article.Id));
    }

    [Fact]
    public async Task Only_article_admins_change_the_visibility()
    {
        var article = await CreateAsync(Ada, "Sichtbarkeit");
        await SetPermissionsAsync(Ada, article.Article.Id, new PermissionEntry("user", Clara.Id, "edit"));

        var byEditor = await As(Clara).PatchAsJsonAsync($"/api/v1/knowledge/articles/{article.Article.Id}", new { version = article.Article.Version, visibility = "restricted" });
        var byAdmin = await As(Ada).PatchAsJsonAsync($"/api/v1/knowledge/articles/{article.Article.Id}", new { version = article.Article.Version, visibility = "restricted" });
        var renamed = await As(Clara).PatchAsJsonAsync($"/api/v1/knowledge/articles/{article.Article.Id}", new { version = article.Article.Version + 1, title = "Neuer Titel" });

        Assert.Equal(HttpStatusCode.Forbidden, byEditor.StatusCode);
        Assert.Equal(HttpStatusCode.OK, byAdmin.StatusCode);
        Assert.Equal("restricted", (await byAdmin.Content.ReadFromJsonAsync<ArticleDetails>())!.Article.Visibility);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
    }

    [Fact]
    public async Task Restricted_published_articles_stay_hidden_from_the_organization()
    {
        var article = await CreateAsync(Ada, "Vertraulich", visibility: "restricted");
        var published = await ChangeStatusAsync(Ada, article.Article.Id, article.Article.Version, "published");

        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await As(Felix).GetAsync($"/api/v1/knowledge/articles/{article.Article.Id}")).StatusCode);
    }

    [Fact]
    public async Task Permissions_can_only_be_managed_by_article_admins()
    {
        var article = await CreateAsync(Ben, "Rechte");

        var byOther = await As(Clara).PutAsJsonAsync(
            $"/api/v1/knowledge/articles/{article.Article.Id}/permissions", new SetPermissionsRequest([new PermissionEntry("user", Clara.Id, "admin")]));
        var foreignTeam = await As(Ben).PutAsJsonAsync(
            $"/api/v1/knowledge/articles/{article.Article.Id}/permissions", new SetPermissionsRequest([new PermissionEntry("team", FabrikamTeam.Id, "view")]));
        var granted = await SetPermissionsAsync(Ben, article.Article.Id, new PermissionEntry("team", SalesTeam.Id, "view"), new PermissionEntry("user", Eva.Id, "edit"));

        Assert.Equal(HttpStatusCode.NotFound, byOther.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, foreignTeam.StatusCode);
        Assert.Equal(["Eva Viewer", "Vertrieb"], granted.Select(p => p.Name).Order());
    }

    [Fact]
    public async Task Deleted_articles_disappear()
    {
        var article = await CreateAsync(Ben, "Wird gelöscht");

        var deleted = await As(Ben).DeleteAsync($"/api/v1/knowledge/articles/{article.Article.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await As(Ben).GetAsync($"/api/v1/knowledge/articles/{article.Article.Id}")).StatusCode);
        Assert.DoesNotContain(await SearchAsync(Ben, "gelöscht"), a => a.Id == article.Article.Id);
    }

    [Fact]
    public async Task Full_text_search_uses_german_stemming_and_filters()
    {
        var results = await SearchAsync(Eva, "Störung");
        var byType = await As(Eva).GetFromJsonAsync<PagedResponse<ArticleSummary>>("/api/v1/knowledge/articles?type=process");
        var bySpace = await As(Eva).GetFromJsonAsync<PagedResponse<ArticleSummary>>($"/api/v1/knowledge/articles?spaceId={PlatformSpace.Id}");
        var byTag = await As(Eva).GetFromJsonAsync<PagedResponse<ArticleSummary>>("/api/v1/knowledge/articles?tag=betrieb");

        Assert.Contains(results, a => a.Id == IncidentArticle.Id);
        Assert.Contains(await SearchAsync(Eva, "Kickoffs Meilensteine"), a => a.Id == KickoffArticle.Id);
        Assert.Contains(byType!.Items, a => a.Id == DeploymentArticle.Id);
        Assert.All(byType.Items, a => Assert.Equal("process", a.ArticleType));
        Assert.All(bySpace!.Items, a => Assert.Equal(PlatformSpace.Id, a.SpaceId));
        Assert.Contains(byTag!.Items, a => a.Id == IncidentArticle.Id);
        Assert.All(byTag.Items, a => Assert.Contains("Betrieb", a.Tags));
    }

    [Fact]
    public async Task Search_never_returns_drafts_of_others()
    {
        var results = await SearchAsync(Eva, "Styleguide");

        Assert.DoesNotContain(results, a => a.Id == StyleGuideDraft.Id);
        Assert.Contains(await SearchAsync(Clara, "Styleguide"), a => a.Id == StyleGuideDraft.Id);
    }

    [Fact]
    public async Task Tags_are_shared_in_the_organization_case_insensitively()
    {
        var article = await CreateAsync(Ben, "Getaggt");

        var response = await As(Ben).PutAsJsonAsync($"/api/v1/knowledge/articles/{article.Article.Id}/tags", new SetTagsRequest(["betrieb", "Neu-Tag", "neu-tag"]));
        var tags = await As(Ben).GetFromJsonAsync<List<TagResponse>>("/api/v1/knowledge/tags");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["Betrieb", "Neu-Tag"], (await response.Content.ReadFromJsonAsync<ArticleDetails>())!.Article.Tags);
        Assert.Single(tags!, t => t.Name.Equals("betrieb", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(tags!, t => t.Name == "Onboarding");
    }

    [Fact]
    public async Task Relations_connect_visible_articles_without_cycles()
    {
        var parent = await CreateAsync(Ben, "Handbuch");
        var child = await CreateAsync(Ben, "Kapitel");
        var url = $"/api/v1/knowledge/articles/{child.Article.Id}/relations";

        var created = await As(Ben).PostAsJsonAsync(url, new CreateRelationRequest(parent.Article.Id, "PART_OF"));
        var duplicate = await As(Ben).PostAsJsonAsync(url, new CreateRelationRequest(parent.Article.Id, "PART_OF"));
        var cycle = await As(Ben).PostAsJsonAsync($"/api/v1/knowledge/articles/{parent.Article.Id}/relations", new CreateRelationRequest(child.Article.Id, "PART_OF"));
        var self = await As(Ben).PostAsJsonAsync(url, new CreateRelationRequest(child.Article.Id, "RELATED"));
        var hidden = await As(Ben).PostAsJsonAsync(url, new CreateRelationRequest(PricingArticle.Id, "RELATED"));
        var unknownType = await As(Ben).PostAsJsonAsync(url, new CreateRelationRequest(KickoffArticle.Id, "LIKES"));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, cycle.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, hidden.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknownType.StatusCode);
        Assert.Contains((await GetAsync(Ben, parent.Article.Id)).Relations, r => r.ArticleId == child.Article.Id && r.Direction == "incoming");

        var relation = (await created.Content.ReadFromJsonAsync<RelationResponse>())!;
        Assert.Equal(HttpStatusCode.NotFound, (await As(Fritz).DeleteAsync($"/api/v1/knowledge/relations/{relation.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await As(Ben).DeleteAsync($"/api/v1/knowledge/relations/{relation.Id}")).StatusCode);
    }

    [Fact]
    public async Task Relations_hide_articles_the_reader_cannot_see()
    {
        var article = await CreateAsync(Ada, "Öffentlich mit Geheimnis");
        await As(Ada).PostAsJsonAsync($"/api/v1/knowledge/articles/{article.Article.Id}/relations", new CreateRelationRequest(PricingArticle.Id, "REFERENCES"));
        var published = await ChangeStatusAsync(Ada, article.Article.Id, article.Article.Version, "published");
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);

        Assert.Single((await GetAsync(Ada, article.Article.Id)).Relations);
        Assert.Empty((await GetAsync(Felix, article.Article.Id)).Relations);
    }

    [Fact]
    public async Task References_link_projects_tasks_and_teams_and_work_in_both_directions()
    {
        var project = await CreateTeamProjectAsync();
        var task = await CreateTaskAsync(Ben, project.Id);
        var article = await CreateAsync(Ben, "Projektwissen");
        var url = $"/api/v1/knowledge/articles/{article.Article.Id}/references";

        var toProject = await As(Ben).PostAsJsonAsync(url, new CreateReferenceRequest("project", project.Id));
        var toTask = await As(Ben).PostAsJsonAsync(url, new CreateReferenceRequest("task", task.Id));
        var toTeam = await As(Ben).PostAsJsonAsync(url, new CreateReferenceRequest("team", SalesTeam.Id));
        var toForeign = await As(Ben).PostAsJsonAsync(url, new CreateReferenceRequest("project", FabrikamProject.Id));
        var toWhiteboard = await As(Ben).PostAsJsonAsync(url, new CreateReferenceRequest("whiteboard", Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Created, toProject.StatusCode);
        Assert.Equal(project.Id, (await toTask.Content.ReadFromJsonAsync<ReferenceResponse>())!.ProjectId);
        Assert.Equal(HttpStatusCode.Created, toTeam.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, toForeign.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, toWhiteboard.StatusCode);
        Assert.Contains(await As(Ben).GetFromJsonAsync<List<ArticleSummary>>($"/api/v1/projects/{project.Id}/knowledge") ?? [], a => a.Id == article.Article.Id);
        Assert.Contains(await As(Ben).GetFromJsonAsync<List<ArticleSummary>>($"/api/v1/tasks/{task.Id}/knowledge") ?? [], a => a.Id == article.Article.Id);
        Assert.Contains(await As(Ben).GetFromJsonAsync<List<ArticleSummary>>($"/api/v1/teams/{SalesTeam.Id}/knowledge") ?? [], a => a.Id == article.Article.Id);

        // The draft stays Ben's; project members see the project but not the article.
        Assert.DoesNotContain(await As(Eva).GetFromJsonAsync<List<ArticleSummary>>($"/api/v1/projects/{project.Id}/knowledge") ?? [], a => a.Id == article.Article.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await As(Felix).GetAsync($"/api/v1/projects/{project.Id}/knowledge")).StatusCode);
    }

    [Fact]
    public async Task Readers_comment_and_mention_people_who_can_read_the_article()
    {
        var url = $"/api/v1/knowledge/articles/{KickoffArticle.Id}/comments";

        var created = await As(Eva).PostAsJsonAsync(url, new CreateKnowledgeCommentRequest("@Felix gute Agenda", [Felix.Id]));
        var foreign = await As(Eva).PostAsJsonAsync(url, new CreateKnowledgeCommentRequest("@Fritz", [Fritz.Id]));
        var comment = (await created.Content.ReadFromJsonAsync<KnowledgeCommentResponse>())!;
        var list = await As(Felix).GetFromJsonAsync<PagedResponse<KnowledgeCommentResponse>>(url);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
        Assert.Contains(list!.Items, c => c.Id == comment.Id && c.AuthorName == "Eva Viewer");
        var mention = Assert.Single(mentions.Events, e => e.CommentId == comment.Id);
        Assert.Equal([Felix.Id], mention.MentionedUserIds);
    }

    [Fact]
    public async Task Restricted_articles_only_allow_mentions_of_grantees()
    {
        var url = $"/api/v1/knowledge/articles/{PricingArticle.Id}/comments";

        Assert.Equal(HttpStatusCode.BadRequest, (await As(David).PostAsJsonAsync(url, new CreateKnowledgeCommentRequest("@Eva", [Eva.Id]))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await As(David).PostAsJsonAsync(url, new CreateKnowledgeCommentRequest("@Ada", [Ada.Id]))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await As(Eva).GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Only_the_author_edits_and_admins_may_delete_comments()
    {
        var article = await CreateAsync(Ben, "Diskussion");
        await SetPermissionsAsync(Ben, article.Article.Id, new PermissionEntry("user", Eva.Id, "view"));
        var created = await As(Eva).PostAsJsonAsync($"/api/v1/knowledge/articles/{article.Article.Id}/comments", new CreateKnowledgeCommentRequest("Frage", null));
        var comment = (await created.Content.ReadFromJsonAsync<KnowledgeCommentResponse>())!;

        var byOther = await As(Ben).PatchAsJsonAsync($"/api/v1/knowledge/comments/{comment.Id}", new UpdateKnowledgeCommentRequest("Geändert", comment.Version));
        var byAuthor = await As(Eva).PatchAsJsonAsync($"/api/v1/knowledge/comments/{comment.Id}", new UpdateKnowledgeCommentRequest("Präzisiert", comment.Version));
        var stale = await As(Eva).PatchAsJsonAsync($"/api/v1/knowledge/comments/{comment.Id}", new UpdateKnowledgeCommentRequest("Nochmal", comment.Version));
        var deleted = await As(Ben).DeleteAsync($"/api/v1/knowledge/comments/{comment.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, byOther.StatusCode);
        Assert.Equal(HttpStatusCode.OK, byAuthor.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [Fact]
    public async Task Spaces_are_managed_by_organization_admins_and_count_visible_articles()
    {
        var byMember = await As(Ben).PostAsJsonAsync("/api/v1/knowledge/spaces", new CreateSpaceRequest("Vertrieb", null));
        var byAdmin = await As(Ada).PostAsJsonAsync("/api/v1/knowledge/spaces", new CreateSpaceRequest($"Bereich {Guid.NewGuid():N}", "Neu"));
        var spaces = await As(Eva).GetFromJsonAsync<List<SpaceResponse>>("/api/v1/knowledge/spaces");

        Assert.Equal(HttpStatusCode.Forbidden, byMember.StatusCode);
        Assert.Equal(HttpStatusCode.Created, byAdmin.StatusCode);
        Assert.DoesNotContain(spaces!, s => s.Id == FabrikamSpace.Id);
        var methods = Assert.Single(spaces!, s => s.Id == MethodsSpace.Id);
        Assert.Equal(
            await ScalarAsync("select count(*) from knowledge_article where knowledge_space_id = $1 and status = 'published' and deleted_at is null and visibility = 'organization'", MethodsSpace.Id),
            methods.ArticleCount);
    }

    private static JsonObject Paragraph(string text) => new() { ["type"] = "paragraph", ["text"] = text };

    private static JsonObject Blocks(params JsonNode[] blocks) => new() { ["blocks"] = new JsonArray(blocks) };

    private static CreateArticleRequest NewArticle(string title, JsonObject? content = null) =>
        new(title, "article", null, null, null, content);

    private async Task<ArticleDetails> CreateAsync(SeedUser user, string title, JsonObject? content = null, string? visibility = null)
    {
        var response = await As(user).PostAsJsonAsync("/api/v1/knowledge/articles", NewArticle(title, content) with { Visibility = visibility });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ArticleDetails>())!;
    }

    private async Task<ArticleDetails> GetAsync(SeedUser user, Guid articleId) =>
        (await As(user).GetFromJsonAsync<ArticleDetails>($"/api/v1/knowledge/articles/{articleId}"))!;

    private async Task<IReadOnlyList<ArticleSummary>> SearchAsync(SeedUser user, string text) =>
        (await As(user).GetFromJsonAsync<PagedResponse<ArticleSummary>>($"/api/v1/knowledge/articles?q={Uri.EscapeDataString(text)}&limit=100"))!.Items;

    private async Task<ArticleDetails> SaveContentAsync(SeedUser user, ArticleDetails article, JsonObject content, string? note)
    {
        var response = await As(user).PutAsJsonAsync(
            $"/api/v1/knowledge/articles/{article.Article.Id}/content", new SaveContentRequest(article.Article.Version, content, note));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ArticleDetails>())!;
    }

    private Task<HttpResponseMessage> ChangeStatusAsync(SeedUser user, Guid articleId, long version, string status) =>
        As(user).PostAsJsonAsync($"/api/v1/knowledge/articles/{articleId}/status", new ChangeStatusRequest(version, status));

    private async Task<List<PermissionResponse>> SetPermissionsAsync(SeedUser user, Guid articleId, params PermissionEntry[] entries)
    {
        var response = await As(user).PutAsJsonAsync($"/api/v1/knowledge/articles/{articleId}/permissions", new SetPermissionsRequest(entries));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<PermissionResponse>>())!;
    }

    private sealed class MentionRecorder : IDomainEventHandler<UsersMentionedInKnowledgeComment>
    {
        private readonly ConcurrentQueue<UsersMentionedInKnowledgeComment> events = new();

        public IReadOnlyCollection<UsersMentionedInKnowledgeComment> Events => events;

        public Task HandleAsync(UsersMentionedInKnowledgeComment domainEvent, CancellationToken ct)
        {
            events.Enqueue(domainEvent);
            return Task.CompletedTask;
        }
    }
}
