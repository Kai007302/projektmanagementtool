using System.Net;
using System.Net.Http.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Modules.Identity.Development;
using ProjectHub.Api.Modules.Projects;
using ProjectHub.Api.Modules.Whiteboard;
using static ProjectHub.Api.Modules.Identity.Development.DevelopmentSeedData;

namespace ProjectHub.Api.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class WhiteboardTests(InfrastructureFixture infrastructure) : ApiTestBase(infrastructure)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Editors_create_rename_and_delete_whiteboards()
    {
        var project = await CreateTeamProjectAsync();
        var board = await CreateBoardAsync(Clara, project.Id, "  Ideen  ");
        Assert.Equal("Ideen", board.Name);

        var list = await As(Eva).GetFromJsonAsync<PagedResponse<WhiteboardResponse>>($"/api/v1/projects/{project.Id}/whiteboards");
        Assert.Equal([board.Id], list!.Items.Select(b => b.Id));

        var renamed = await As(Ben).PatchAsJsonAsync($"/api/v1/whiteboards/{board.Id}", new { version = board.Version, name = "Workshop" });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal("Workshop", (await renamed.Content.ReadFromJsonAsync<WhiteboardResponse>())!.Name);

        var stale = await As(Clara).PatchAsJsonAsync($"/api/v1/whiteboards/{board.Id}", new { version = board.Version, name = "Alt" });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await As(Clara).DeleteAsync($"/api/v1/whiteboards/{board.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await As(Ben).GetAsync($"/api/v1/whiteboards/{board.Id}")).StatusCode);
        Assert.Equal(1L, await ScalarAsync("select count(*) from activity_log where action = 'WhiteboardCreated' and resource_id = $1", board.Id));
        Assert.Equal(1L, await ScalarAsync("select count(*) from audit_log where action = 'WhiteboardDeleted' and resource_id = $1", board.Id));
    }

    [Fact]
    public async Task Members_viewers_and_outsiders_cannot_manage_whiteboards()
    {
        var project = await CreateTeamProjectAsync();
        var board = await CreateBoardAsync(Ben, project.Id, "Plan");

        foreach (var user in new[] { David, Eva, Gina })
        {
            var created = await As(user).PostAsJsonAsync($"/api/v1/projects/{project.Id}/whiteboards", new CreateWhiteboardRequest("Neu"));
            Assert.Equal(HttpStatusCode.Forbidden, created.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await As(user).DeleteAsync($"/api/v1/whiteboards/{board.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await As(user).GetAsync($"/api/v1/whiteboards/{board.Id}")).StatusCode);
        }

        foreach (var outsider in new[] { Felix, Fritz })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await As(outsider).GetAsync($"/api/v1/whiteboards/{board.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await As(outsider).DeleteAsync($"/api/v1/whiteboards/{board.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await As(outsider).GetAsync($"/api/v1/projects/{project.Id}/whiteboards")).StatusCode);
        }

        var invalid = await As(Ben).PostAsJsonAsync($"/api/v1/projects/{project.Id}/whiteboards", new CreateWhiteboardRequest(new string('x', 201)));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Updates_reach_the_others_and_are_part_of_the_state_for_newcomers()
    {
        var project = await CreateTeamProjectAsync();
        var board = await CreateBoardAsync(Ben, project.Id, "Live");
        await using var ben = await ConnectAsync(Ben);
        await using var david = await ConnectAsync(David);
        Assert.True((await ben.JoinAsync(board.Id)).CanEdit);
        Assert.True((await david.JoinAsync(board.Id)).CanEdit);

        var update = Sticky("s1", "Hallo");
        await david.Connection.InvokeAsync(nameof(WhiteboardsHub.PushUpdate), board.Id, update);

        var received = await ben.Updates.NextAsync();
        Assert.Equal(board.Id, received.WhiteboardId);
        Assert.Equal(update, received.Update);
        Assert.False(david.Updates.HasMessage, "The sender does not get its own update back.");

        await using var eva = await ConnectAsync(Eva);
        var joined = await eva.JoinAsync(board.Id);
        Assert.False(joined.CanEdit);
        Assert.Equal("Hallo", WhiteboardDocuments.ReadObjects(joined.State)["s1"]["text"]);
    }

    [Fact]
    public async Task Viewers_cannot_draw()
    {
        var project = await CreateTeamProjectAsync();
        var board = await CreateBoardAsync(Ben, project.Id, "Nur lesen");
        await using var eva = await ConnectAsync(Eva);
        await eva.JoinAsync(board.Id);

        await Assert.ThrowsAsync<HubException>(() => eva.Connection.InvokeAsync(nameof(WhiteboardsHub.PushUpdate), board.Id, Sticky("s1", "Nein")));
        Assert.Equal(0L, await ScalarAsync("select count(*) from whiteboard_update where whiteboard_id = $1", board.Id));
    }

    [Fact]
    public async Task Invalid_and_oversized_updates_are_rejected()
    {
        var project = await CreateTeamProjectAsync();
        var board = await CreateBoardAsync(Ben, project.Id, "Prüfung");
        await using var ben = await ConnectAsync(Ben);
        await ben.JoinAsync(board.Id);

        await Assert.ThrowsAsync<HubException>(() => ben.Connection.InvokeAsync(nameof(WhiteboardsHub.PushUpdate), board.Id, new byte[] { 1, 2, 3, 99, 7 }));
        await Assert.ThrowsAsync<HubException>(() => ben.Connection.InvokeAsync(nameof(WhiteboardsHub.PushUpdate), board.Id, Array.Empty<byte>()));
        await Assert.ThrowsAsync<HubException>(() => ben.Connection.InvokeAsync(nameof(WhiteboardsHub.PushUpdate), board.Id, new byte[260 * 1024]));
        Assert.Equal(0L, await ScalarAsync("select count(*) from whiteboard_update where whiteboard_id = $1", board.Id));
    }

    [Theory]
    [InlineData("dev-felix")]
    [InlineData("dev-fritz")]
    public async Task Outsiders_cannot_join_or_push(string objectId)
    {
        var project = await CreateTeamProjectAsync();
        var board = await CreateBoardAsync(Ben, project.Id, "Geheim");
        await using var outsider = await ConnectAsync(Users.Single(u => u.ObjectId == objectId));

        await Assert.ThrowsAsync<HubException>(() => outsider.JoinAsync(board.Id));
        await Assert.ThrowsAsync<HubException>(() => outsider.Connection.InvokeAsync(nameof(WhiteboardsHub.PushUpdate), board.Id, Sticky("s1", "x")));
    }

    [Fact]
    public async Task Presence_is_forwarded_and_cleared_when_someone_leaves()
    {
        var project = await CreateTeamProjectAsync();
        var board = await CreateBoardAsync(Ben, project.Id, "Presence");
        await using var ben = await ConnectAsync(Ben);
        var eva = await ConnectAsync(Eva);
        await ben.JoinAsync(board.Id);

        await Assert.ThrowsAsync<HubException>(() =>
            eva.Connection.InvokeAsync(nameof(WhiteboardsHub.UpdatePresence), board.Id, new PresenceRequest(1, 2, null)));

        await eva.JoinAsync(board.Id);
        await eva.Connection.InvokeAsync(nameof(WhiteboardsHub.UpdatePresence), board.Id, new PresenceRequest(120.5, 80, "s1"));
        var presence = await ben.Presence.NextAsync();
        Assert.Equal((Eva.Id, Eva.DisplayName, 120.5, 80.0, "s1"), (presence.UserId, presence.Name, presence.X!.Value, presence.Y!.Value, presence.SelectedObjectId));

        await eva.DisposeAsync();
        var left = await ben.Left.NextAsync();
        Assert.Equal(presence.ConnectionId, left.ConnectionId);
        Assert.Equal(0L, await ScalarAsync("select count(*) from whiteboard_update where whiteboard_id = $1", board.Id));
    }

    [Fact]
    public async Task Compaction_merges_updates_into_a_snapshot_and_keeps_the_content()
    {
        var project = await CreateTeamProjectAsync();
        var board = await CreateBoardAsync(Ben, project.Id, "Compaction");
        await using var ben = await ConnectAsync(Ben);
        await ben.JoinAsync(board.Id);
        await ben.Connection.InvokeAsync(nameof(WhiteboardsHub.PushUpdate), board.Id, Sticky("s1", "Eins"));
        await ben.Connection.InvokeAsync(nameof(WhiteboardsHub.PushUpdate), board.Id, Sticky("s2", "Zwei"));

        Assert.True(await CompactAsync(board.Id));
        Assert.False(await CompactAsync(board.Id), "Nothing left to compact.");
        Assert.Equal(0L, await ScalarAsync("select count(*) from whiteboard_update where whiteboard_id = $1", board.Id));
        Assert.Equal(2L, await ScalarAsync("select max(sequence_number) from whiteboard_snapshot where whiteboard_id = $1", board.Id));

        // Later updates continue the sequence; old snapshots beyond the newest two are removed with their files.
        for (var i = 3; i <= 5; i++)
        {
            await ben.Connection.InvokeAsync(nameof(WhiteboardsHub.PushUpdate), board.Id, Sticky($"s{i}", $"Nummer {i}"));
            Assert.True(await CompactAsync(board.Id));
        }

        Assert.Equal(WhiteboardDocumentStore.SnapshotsToKeep, await ScalarAsync("select count(*) from whiteboard_snapshot where whiteboard_id = $1", board.Id));
        Assert.Equal(WhiteboardDocumentStore.SnapshotsToKeep, Directory.GetFiles(SnapshotDirectory, "*.ybin", SearchOption.AllDirectories).Count(f => f.Contains(board.Id.ToString("N"))));

        await using var david = await ConnectAsync(David);
        var objects = WhiteboardDocuments.ReadObjects((await david.JoinAsync(board.Id)).State);
        Assert.Equal(["s1", "s2", "s3", "s4", "s5"], objects.Keys.Order());
        Assert.Equal("Nummer 5", objects["s5"]["text"]);

        Assert.Equal(HttpStatusCode.NoContent, (await As(Ben).DeleteAsync($"/api/v1/whiteboards/{board.Id}")).StatusCode);
        Assert.DoesNotContain(Directory.GetFiles(SnapshotDirectory, "*.ybin", SearchOption.AllDirectories), f => f.Contains(board.Id.ToString("N")));
    }

    [Fact]
    public async Task Task_cards_show_live_task_data_and_become_references()
    {
        var project = await CreateTeamProjectAsync();
        var other = await CreateTeamProjectAsync();
        var task = await CreateTaskAsync(Ben, project.Id, NewTask("Startseite", assigneeId: Clara.Id));
        var foreign = await CreateTaskAsync(Ben, other.Id, NewTask("Anderes Projekt"));
        var board = await CreateBoardAsync(Ben, project.Id, "Aufgaben");
        await using var ben = await ConnectAsync(Ben);
        await ben.JoinAsync(board.Id);
        await ben.Connection.InvokeAsync(nameof(WhiteboardsHub.PushUpdate), board.Id, WhiteboardDocuments.CreateUpdate(new Dictionary<string, IReadOnlyDictionary<string, object>>
        {
            ["card"] = TaskCardFields(task.Id),
            ["foreign"] = TaskCardFields(foreign.Id),
            ["broken"] = new Dictionary<string, object> { ["type"] = "task", ["taskId"] = "keine-id" },
        }));

        var tasks = await As(Eva).GetFromJsonAsync<List<WhiteboardTask>>($"/api/v1/whiteboards/{board.Id}/tasks?ids={task.Id},{foreign.Id}");
        Assert.Equal([new WhiteboardTask(task.Id, "Startseite", "todo", Clara.DisplayName, null)], tasks);
        Assert.Equal(HttpStatusCode.BadRequest, (await As(Eva).GetAsync($"/api/v1/whiteboards/{board.Id}/tasks?ids=nope")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await As(Felix).GetAsync($"/api/v1/whiteboards/{board.Id}/tasks?ids={task.Id}")).StatusCode);

        // The title is never copied into the document: renaming the task shows up on the card right away.
        await As(Ben).PatchAsJsonAsync($"/api/v1/tasks/{task.Id}", new { version = task.Version, title = "Startseite neu" });
        Assert.Equal("Startseite neu", (await As(Eva).GetFromJsonAsync<List<WhiteboardTask>>($"/api/v1/whiteboards/{board.Id}/tasks?ids={task.Id}"))!.Single().Title);

        Assert.True(await CompactAsync(board.Id));
        Assert.Equal([new TaskWhiteboard(board.Id, "Aufgaben")], await As(Eva).GetFromJsonAsync<List<TaskWhiteboard>>($"/api/v1/tasks/{task.Id}/whiteboards"));
        Assert.Equal(1L, await ScalarAsync("select count(*) from whiteboard_reference where whiteboard_id = $1", board.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await As(Felix).GetAsync($"/api/v1/tasks/{task.Id}/whiteboards")).StatusCode);

        // Turning the card into a note removes the reference at the next compaction.
        // The change builds on the current state; otherwise it would be concurrent and Yjs might keep the card.
        var current = (await ben.JoinAsync(board.Id)).State;
        await ben.Connection.InvokeAsync(nameof(WhiteboardsHub.PushUpdate), board.Id, Sticky("card", "Erledigt", current));
        Assert.True(await CompactAsync(board.Id));
        Assert.Empty((await As(Eva).GetFromJsonAsync<List<TaskWhiteboard>>($"/api/v1/tasks/{task.Id}/whiteboards"))!);
    }

    [Fact]
    public async Task An_update_that_crashes_the_decoder_is_rejected_and_the_api_keeps_working()
    {
        var project = await CreateTeamProjectAsync();
        var board = await CreateBoardAsync(Ben, project.Id, "Absturz");
        await using var ben = await ConnectAsync(Ben);
        await ben.JoinAsync(board.Id);

        await Assert.ThrowsAsync<HubException>(() => ben.Connection.InvokeAsync(nameof(WhiteboardsHub.PushUpdate), board.Id, Poison.CrashesAlone));

        // The engine process is replaced; the API and the connection are unaffected.
        await ben.Connection.InvokeAsync(nameof(WhiteboardsHub.PushUpdate), board.Id, Sticky("s1", "Weiter"));
        Assert.Equal(HttpStatusCode.OK, (await As(Ben).GetAsync($"/api/v1/whiteboards/{board.Id}")).StatusCode);
        Assert.Equal(1L, await ScalarAsync("select count(*) from whiteboard_update where whiteboard_id = $1", board.Id));
    }

    [Fact]
    public async Task An_update_that_breaks_the_document_together_with_others_is_removed()
    {
        var project = await CreateTeamProjectAsync();
        var board = await CreateBoardAsync(Ben, project.Id, "Gift");
        await using var ben = await ConnectAsync(Ben);
        await ben.JoinAsync(board.Id);
        await ben.Connection.InvokeAsync(nameof(WhiteboardsHub.PushUpdate), board.Id, Poison.ValidState);
        await ben.Connection.InvokeAsync(nameof(WhiteboardsHub.PushUpdate), board.Id, Poison.BreaksValidState);
        await ben.Connection.InvokeAsync(nameof(WhiteboardsHub.PushUpdate), board.Id, Sticky("after", "Danach"));
        Assert.Equal(3L, await ScalarAsync("select count(*) from whiteboard_update where whiteboard_id = $1", board.Id));

        await using var david = await ConnectAsync(David);
        var objects = WhiteboardDocuments.ReadObjects((await david.JoinAsync(board.Id)).State);

        Assert.Contains("o0", objects.Keys);
        Assert.Contains("after", objects.Keys);
        Assert.Equal(2L, await ScalarAsync("select count(*) from whiteboard_update where whiteboard_id = $1", board.Id));
        Assert.Equal(0L, await ScalarAsync("select count(*) from whiteboard_update where whiteboard_id = $1 and sequence_number = 2", board.Id));
        Assert.True(await CompactAsync(board.Id));
    }

    [Fact]
    public async Task The_seeded_whiteboard_has_content_and_a_task_card()
    {
        await using var eva = await ConnectAsync(Eva);
        var objects = WhiteboardDocuments.ReadObjects((await eva.JoinAsync(IdeasWhiteboardId)).State);

        Assert.Equal("Suche ganz oben", objects["seed-sticky-1"]["text"]);
        Assert.Equal(StartPageTask.Id.ToString(), objects["seed-task-1"]["taskId"]);
    }

    [Fact]
    public async Task The_background_service_compacts_quiet_whiteboards()
    {
        await using var factory = new ProjectHubApiFactory(
            Infrastructure.Postgres.GetConnectionString(),
            Infrastructure.Redis.GetConnectionString(),
            configure: builder =>
            {
                builder.UseSetting(WhiteboardOptions.CompactionIntervalKey, "0.2");
                builder.UseSetting(WhiteboardOptions.QuietPeriodKey, "0.2");
            });
        var project = await CreateTeamProjectAsync();
        var board = await CreateBoardAsync(Ben, project.Id, "Im Hintergrund");
        await using var ben = await ConnectAsync(Ben, factory);
        await ben.JoinAsync(board.Id);
        await ben.Connection.InvokeAsync(nameof(WhiteboardsHub.PushUpdate), board.Id, Sticky("s1", "Später"));

        var deadline = DateTime.UtcNow + Timeout;
        while (await ScalarAsync("select count(*) from whiteboard_snapshot where whiteboard_id = $1", board.Id) == 0)
        {
            Assert.True(DateTime.UtcNow < deadline, "The whiteboard was not compacted in time.");
            await Task.Delay(100);
        }

        Assert.Equal(0L, await ScalarAsync("select count(*) from whiteboard_update where whiteboard_id = $1", board.Id));
    }

    /// <summary>Fixtures from fuzzing; see TestData/yjs-poison.txt.</summary>
    private static class Poison
    {
        private static readonly string[] Lines = File.ReadLines(Path.Combine(AppContext.BaseDirectory, "TestData", "yjs-poison.txt"))
            .Where(l => !l.StartsWith('#') && l.Length > 0)
            .ToArray();

        public static byte[] ValidState => Convert.FromHexString(Lines[0]);

        public static byte[] BreaksValidState => Convert.FromHexString(Lines[1]);

        public static byte[] CrashesAlone => Convert.FromHexString(Lines[2]);
    }

    private string SnapshotDirectory => Path.Combine(Factory.AttachmentDirectory, "whiteboards");

    private static byte[] Sticky(string id, string text, byte[]? basedOn = null) =>
        WhiteboardDocuments.CreateUpdate(new Dictionary<string, IReadOnlyDictionary<string, object>>
        {
            [id] = new Dictionary<string, object> { ["type"] = "sticky", ["x"] = 10, ["y"] = 20, ["w"] = 160, ["h"] = 100, ["text"] = text },
        }, basedOn);

    private static Dictionary<string, object> TaskCardFields(Guid taskId) =>
        new() { ["type"] = "task", ["x"] = 0, ["y"] = 0, ["w"] = 200, ["h"] = 80, ["taskId"] = taskId.ToString() };

    private async Task<WhiteboardResponse> CreateBoardAsync(SeedUser user, Guid projectId, string name)
    {
        var response = await As(user).PostAsJsonAsync($"/api/v1/projects/{projectId}/whiteboards", new CreateWhiteboardRequest(name));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<WhiteboardResponse>())!;
    }

    private async Task<bool> CompactAsync(Guid whiteboardId)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<WhiteboardDocumentStore>().CompactAsync(whiteboardId, CancellationToken.None);
    }

    private async Task<Client> ConnectAsync(SeedUser user, ProjectHubApiFactory? factory = null)
    {
        var server = (factory ?? Factory).Server;
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, WhiteboardsHub.Path.TrimStart('/')), options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.Headers[DevelopmentIdentityOptions.UserHeader] = user.ObjectId;
            })
            .Build();

        var client = new Client(connection);
        connection.On<WhiteboardUpdateMessage>(nameof(IWhiteboardsClient.Update), m => client.Updates.Add(m));
        connection.On<PresenceMessage>(nameof(IWhiteboardsClient.Presence), m => client.Presence.Add(m));
        connection.On<PresenceLeftMessage>(nameof(IWhiteboardsClient.PresenceLeft), m => client.Left.Add(m));
        await connection.StartAsync();
        return client;
    }

    private sealed class Inbox<T>
    {
        private readonly Channel<T> channel = Channel.CreateUnbounded<T>();

        public bool HasMessage => channel.Reader.TryPeek(out _);

        public void Add(T message) => channel.Writer.TryWrite(message);

        public async Task<T> NextAsync()
        {
            using var cancel = new CancellationTokenSource(Timeout);
            return await channel.Reader.ReadAsync(cancel.Token);
        }
    }

    private sealed class Client(HubConnection connection) : IAsyncDisposable
    {
        public HubConnection Connection { get; } = connection;

        public Inbox<WhiteboardUpdateMessage> Updates { get; } = new();

        public Inbox<PresenceMessage> Presence { get; } = new();

        public Inbox<PresenceLeftMessage> Left { get; } = new();

        public Task<JoinResponse> JoinAsync(Guid whiteboardId) =>
            Connection.InvokeAsync<JoinResponse>(nameof(WhiteboardsHub.Join), whiteboardId);

        public ValueTask DisposeAsync() => Connection.DisposeAsync();
    }
}
