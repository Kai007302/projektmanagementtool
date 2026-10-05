using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Modules.Comments;
using ProjectHub.Api.Modules.Privacy;
using ProjectHub.Api.Modules.Projects;
using ProjectHub.Api.Modules.Users;
using static ProjectHub.Api.Modules.Identity.Development.DevelopmentSeedData;

namespace ProjectHub.Api.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class PrivacyTests(InfrastructureFixture infrastructure) : ApiTestBase(infrastructure)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Legal_links_are_public_and_empty_until_configured()
    {
        var legal = await Factory.CreateClient().GetFromJsonAsync<LegalInformation>("/api/v1/legal");
        Assert.Equal(new LegalInformation(null, null), legal);

        await using var configured = new ProjectHubApiFactory(
            Infrastructure.Postgres.GetConnectionString(), Infrastructure.Redis.GetConnectionString(), configure: builder => builder
                .UseSetting(LegalInformation.PrivacyNoticeUrlKey, "/rechtliches/datenschutz.html")
                .UseSetting(LegalInformation.ImprintUrlKey, "https://example.com/impressum"));
        legal = await configured.CreateClient().GetFromJsonAsync<LegalInformation>("/api/v1/legal");
        Assert.Equal(new LegalInformation("/rechtliches/datenschutz.html", "https://example.com/impressum"), legal);
    }

    [Fact]
    public async Task Data_export_contains_the_persons_own_data_and_is_audited()
    {
        var project = await CreateTeamProjectAsync();
        var task = await CreateTaskAsync(Ben, project.Id, NewTask("Export prüfen", assigneeId: David.Id));
        var own = await As(David).PostAsJsonAsync($"/api/v1/tasks/{task.Id}/comments", new CreateCommentRequest("Mein Kommentar", null));
        var other = await As(Clara).PostAsJsonAsync($"/api/v1/tasks/{task.Id}/comments", new CreateCommentRequest("Kommentar von Clara", null));
        Assert.Equal((HttpStatusCode.Created, HttpStatusCode.Created), (own.StatusCode, other.StatusCode));

        var response = await As(David).GetAsync("/api/v1/me/data-export");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("projecthub-meine-daten-", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        var export = (await response.Content.ReadFromJsonAsync<PersonalDataExport>(Json))!;
        Assert.Equal((David.Id, David.Email), (export.Profile.Id, export.Profile.Email));
        Assert.Contains(export.ProjectMemberships, m => m.Id == project.Id && m.Role == "member");
        Assert.Contains(export.TasksAssigned, t => t.Id == task.Id && t.Title == "Export prüfen");
        Assert.DoesNotContain(export.TasksCreated, t => t.Id == task.Id);
        Assert.Contains(export.TaskComments, c => c.ParentId == task.Id && c.Content == "Mein Kommentar");
        Assert.DoesNotContain(export.TaskComments, c => c.Content == "Kommentar von Clara");
        Assert.Contains(export.Notifications, n => n.Title.Contains("Export prüfen"));
        Assert.Contains(export.ActivityEntries, a => a.Action == "CommentAdded" && a.ProjectId == project.Id);

        Assert.True(await ScalarAsync(
            "select count(*) from audit_log where actor_id = $1 and action = 'PersonalDataExported' and created_at > now() - interval '1 minute'",
            David.Id) >= 1);
    }

    [Fact]
    public async Task Anonymizing_removes_identifying_data_and_keeps_content()
    {
        var person = await CreateUserAsync("Hanna Test");
        var project = await CreateProjectAsync(Ben, (person, "member"));
        var task = await CreateTaskAsync(Ben, project.Id, NewTask("Übergabe", assigneeId: person.Id));
        Assert.True(await ScalarAsync("select count(*) from notification where user_id = $1", person.Id) > 0);

        var response = await As(Ada).PostAsync($"/api/v1/admin/users/{person.Id}/anonymize", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(1, await ScalarAsync(
            """
            select count(*) from app_user
            where id = $1 and display_name = 'Ehemalige Person' and email = $2 and entra_object_id = $3
              and status = 'inactive' and department is null and anonymized_at is not null
            """,
            person.Id, UserAnonymizationService.AnonymizedEmail(person.Id), UserAnonymizationService.AnonymizedObjectId(person.Id)));
        Assert.Equal(0, await ScalarAsync("select count(*) from notification where user_id = $1", person.Id));
        Assert.Equal(0, await ScalarAsync("select count(*) from mail_outbox where recipient_id = $1", person.Id));
        Assert.Equal(0, await ScalarAsync("select count(*) from project_member where user_id = $1", person.Id));
        Assert.Equal(1, await ScalarAsync("select count(*) from task where id = $1 and assignee_id is null and deleted_at is null", task.Id));
        Assert.Equal(1, await ScalarAsync("select count(*) from audit_log where action = 'UserAnonymized' and resource_id = $1 and actor_id = $2", person.Id, Ada.Id));

        var users = await As(Ada).GetFromJsonAsync<PagedResponse<UserResponse>>("/api/v1/users?search=Ehemalige&limit=100");
        Assert.DoesNotContain(users!.Items, u => u.Id == person.Id);

        // Doing it again changes nothing.
        Assert.Equal(HttpStatusCode.NoContent, (await As(Ada).PostAsync($"/api/v1/admin/users/{person.Id}/anonymize", null)).StatusCode);
    }

    [Fact]
    public async Task Only_admins_of_the_same_organization_anonymize_and_never_themselves()
    {
        var person = await CreateUserAsync("Ida Test");

        Assert.Equal(HttpStatusCode.Forbidden, (await As(Ben).PostAsync($"/api/v1/admin/users/{person.Id}/anonymize", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await As(Fritz).PostAsync($"/api/v1/admin/users/{person.Id}/anonymize", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await As(Ada).PostAsync($"/api/v1/admin/users/{Ada.Id}/anonymize", null)).StatusCode);
        Assert.Equal(1, await ScalarAsync("select count(*) from app_user where id = $1 and anonymized_at is null", person.Id));
    }

    [Fact]
    public async Task Retention_deletes_only_entries_older_than_configured()
    {
        var oldNotification = await InsertAsync(
            "insert into notification (organization_id, user_id, type, title, created_at) values ($1, $2, 'task_assigned', 'alt', now() - interval '200 days') returning id",
            Contoso.Id, Felix.Id);
        var newNotification = await InsertAsync(
            "insert into notification (organization_id, user_id, type, title, created_at) values ($1, $2, 'task_assigned', 'neu', now() - interval '10 days') returning id",
            Contoso.Id, Felix.Id);
        var oldActivity = await InsertAsync(
            "insert into activity_log (organization_id, actor_id, resource_type, action, created_at) values ($1, $2, 'task', 'TaskUpdated', now() - interval '400 days') returning id",
            Contoso.Id, Felix.Id);
        var oldAudit = await InsertAsync(
            "insert into audit_log (organization_id, actor_id, action, created_at) values ($1, $2, 'TaskDeleted', now() - interval '800 days') returning id",
            Contoso.Id, Felix.Id);
        var youngAudit = await InsertAsync(
            "insert into audit_log (organization_id, actor_id, action, created_at) values ($1, $2, 'TaskDeleted', now() - interval '400 days') returning id",
            Contoso.Id, Felix.Id);

        await Factory.Services.GetRequiredService<DataRetention>().PurgeAsync(CancellationToken.None);

        Assert.Equal(0, await ScalarAsync("select count(*) from notification where id = $1", oldNotification));
        Assert.Equal(1, await ScalarAsync("select count(*) from notification where id = $1", newNotification));
        Assert.Equal(0, await ScalarAsync("select count(*) from activity_log where id = $1", oldActivity));
        Assert.Equal(0, await ScalarAsync("select count(*) from audit_log where id = $1", oldAudit));
        Assert.Equal(1, await ScalarAsync("select count(*) from audit_log where id = $1", youngAudit));
    }

    /// <summary>A person of Contoso that no other test knows, so anonymizing them changes nothing elsewhere.</summary>
    private async Task<SeedUser> CreateUserAsync(string name)
    {
        var objectId = $"privacy-{Guid.NewGuid():N}";
        var email = $"{objectId}@contoso-dev.example.invalid";
        var id = await InsertAsync(
            "insert into app_user (organization_id, entra_object_id, email, display_name, department) values ($1, $2, $3, $4, 'Test') returning id",
            Contoso.Id, objectId, email, name);
        return new SeedUser(id, Contoso.Id, objectId, name, email, "member", "Test");
    }

    private async Task<Guid> InsertAsync(string sql, params object[] values)
    {
        _ = Factory.Services; // starts the host, which migrates and seeds the database
        await using var dataSource = Npgsql.NpgsqlDataSource.Create(Infrastructure.Postgres.GetConnectionString());
        await using var command = dataSource.CreateCommand(sql);
        foreach (var value in values)
        {
            command.Parameters.Add(new Npgsql.NpgsqlParameter { Value = value });
        }

        return (Guid)(await command.ExecuteScalarAsync())!;
    }
}
