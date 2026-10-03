using System.Net;
using System.Net.Http.Json;
using Npgsql;
using ProjectHub.Api.Modules.Projects;
using ProjectHub.Api.Modules.Tasks;
using static ProjectHub.Api.Modules.Identity.Development.DevelopmentSeedData;

namespace ProjectHub.Api.IntegrationTests;

/// <summary>
/// Shared setup for API tests: one host per test class and helpers that create isolated
/// projects, so tests that change data never depend on each other.
/// </summary>
public abstract class ApiTestBase(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    protected InfrastructureFixture Infrastructure { get; } = infrastructure;

    protected ProjectHubApiFactory Factory { get; private set; } = null!;

    public Task InitializeAsync()
    {
        Factory = CreateFactory();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await Factory.DisposeAsync();

    protected virtual ProjectHubApiFactory CreateFactory() =>
        new(Infrastructure.Postgres.GetConnectionString(), Infrastructure.Redis.GetConnectionString());

    protected HttpClient As(SeedUser user) => Factory.CreateClientFor(user);

    /// <summary>Creates a project owned by <paramref name="owner"/> with the given additional members.</summary>
    protected async Task<ProjectSummary> CreateProjectAsync(SeedUser owner, params (SeedUser User, string Role)[] members)
    {
        var response = await As(owner).PostAsJsonAsync("/api/v1/projects", new CreateProjectRequest($"Projekt {Guid.NewGuid():N}", null, null, null, null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var project = (await response.Content.ReadFromJsonAsync<ProjectSummary>())!;

        foreach (var (user, role) in members)
        {
            var added = await As(owner).PostAsJsonAsync($"/api/v1/projects/{project.Id}/members", new AddProjectMemberRequest(user.Id, role));
            Assert.Equal(HttpStatusCode.NoContent, added.StatusCode);
        }

        return project;
    }

    /// <summary>Ben (admin), Clara (editor), David (member), Eva (viewer), Gina (guest).</summary>
    protected Task<ProjectSummary> CreateTeamProjectAsync() =>
        CreateProjectAsync(Ben, (Clara, "editor"), (David, "member"), (Eva, "viewer"), (Gina, "guest"));

    protected async Task<TaskResponse> CreateTaskAsync(SeedUser user, Guid projectId, CreateTaskRequest? request = null)
    {
        var response = await As(user).PostAsJsonAsync($"/api/v1/projects/{projectId}/tasks", request ?? NewTask("Aufgabe"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TaskResponse>())!;
    }

    protected static CreateTaskRequest NewTask(
        string title,
        Guid? parentTaskId = null,
        Guid? assigneeId = null,
        DateOnly? startDate = null,
        DateOnly? dueDate = null,
        short? progress = null,
        string? priority = null,
        string? status = null) =>
        new(title, null, status, priority, assigneeId, parentTaskId, startDate, dueDate, progress, null);

    protected async Task<long> ScalarAsync(string sql, params object[] values)
    {
        await using var dataSource = NpgsqlDataSource.Create(Infrastructure.Postgres.GetConnectionString());
        await using var command = dataSource.CreateCommand(sql);
        foreach (var value in values)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = value });
        }

        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
