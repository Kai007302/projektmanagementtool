using System.Net.Http.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using ProjectHub.Api.Modules.Identity.Development;
using ProjectHub.Api.Modules.Realtime;
using static ProjectHub.Api.Modules.Identity.Development.DevelopmentSeedData;

namespace ProjectHub.Api.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class RealtimeTests(InfrastructureFixture infrastructure) : ApiTestBase(infrastructure)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Project_members_are_notified_about_task_changes()
    {
        var project = await CreateTeamProjectAsync();
        await using var eva = await ConnectAsync(Eva);
        await eva.Connection.InvokeAsync("JoinProject", project.Id);

        await CreateTaskAsync(Ben, project.Id);

        var message = await eva.NextAsync();
        Assert.Equal(new ProjectChangedMessage(project.Id, "tasks"), message);
    }

    [Fact]
    public async Task Board_changes_are_announced_to_the_group()
    {
        var project = await CreateTeamProjectAsync();
        await using var david = await ConnectAsync(David, viaQuery: true);
        await david.Connection.InvokeAsync("JoinProject", project.Id);

        await As(Ben).PostAsJsonAsync($"/api/v1/projects/{project.Id}/board/columns", new { name = "Review", taskStatus = "in_progress" });

        Assert.Equal(new ProjectChangedMessage(project.Id, "board"), await david.NextAsync());
    }

    [Fact]
    public async Task Gantt_changes_are_announced_to_the_group()
    {
        var project = await CreateTeamProjectAsync();
        await using var eva = await ConnectAsync(Eva);
        await eva.Connection.InvokeAsync("JoinProject", project.Id);

        await As(Clara).PostAsJsonAsync($"/api/v1/projects/{project.Id}/gantt/milestones", new { name = "Abnahme", date = "2026-11-02" });

        Assert.Equal(new ProjectChangedMessage(project.Id, "gantt"), await eva.NextAsync());
    }

    [Theory]
    [InlineData("dev-felix")]
    [InlineData("dev-fritz")]
    public async Task Outsiders_cannot_join_a_project(string objectId)
    {
        var project = await CreateTeamProjectAsync();
        await using var outsider = await ConnectAsync(Users.Single(u => u.ObjectId == objectId));

        await Assert.ThrowsAsync<HubException>(() => outsider.Connection.InvokeAsync("JoinProject", project.Id));
    }

    [Fact]
    public async Task Only_joined_projects_are_delivered()
    {
        var watched = await CreateTeamProjectAsync();
        var other = await CreateTeamProjectAsync();
        await using var clara = await ConnectAsync(Clara);
        await clara.Connection.InvokeAsync("JoinProject", watched.Id);

        await CreateTaskAsync(Ben, other.Id);
        await CreateTaskAsync(Ben, watched.Id);

        Assert.Equal(watched.Id, (await clara.NextAsync()).ProjectId);
    }

    private async Task<Listener> ConnectAsync(SeedUser user, bool viaQuery = false)
    {
        var server = Factory.Server;
        var url = new Uri(server.BaseAddress, ProjectEventsHub.Path.TrimStart('/') + (viaQuery ? $"?devUser={user.ObjectId}" : string.Empty));
        var connection = new HubConnectionBuilder()
            .WithUrl(url, options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                if (!viaQuery)
                {
                    options.Headers[DevelopmentIdentityOptions.UserHeader] = user.ObjectId;
                }
            })
            .Build();

        var listener = new Listener(connection);
        connection.On<ProjectChangedMessage>(nameof(IProjectEventsClient.ProjectChanged), message => listener.Messages.Writer.TryWrite(message));
        await connection.StartAsync();
        return listener;
    }

    private sealed class Listener(HubConnection connection) : IAsyncDisposable
    {
        public HubConnection Connection { get; } = connection;

        public Channel<ProjectChangedMessage> Messages { get; } = Channel.CreateUnbounded<ProjectChangedMessage>();

        public async Task<ProjectChangedMessage> NextAsync()
        {
            using var cancel = new CancellationTokenSource(Timeout);
            return await Messages.Reader.ReadAsync(cancel.Token);
        }

        public ValueTask DisposeAsync() => Connection.DisposeAsync();
    }
}
