using System.Net;
using System.Net.Http.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Modules.Comments;
using ProjectHub.Api.Modules.Identity.Development;
using ProjectHub.Api.Modules.Knowledge;
using ProjectHub.Api.Modules.Notifications;
using ProjectHub.Api.Modules.Projects;
using static ProjectHub.Api.Modules.Identity.Development.DevelopmentSeedData;

namespace ProjectHub.Api.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class NotificationEndpointTests(InfrastructureFixture infrastructure) : ApiTestBase(infrastructure)
{
    [Fact]
    public async Task Assigning_a_task_notifies_the_assignee_but_never_the_actor()
    {
        var project = await CreateTeamProjectAsync();

        var assigned = await CreateTaskAsync(Ben, project.Id, NewTask("Texte schreiben", assigneeId: David.Id));
        var own = await CreateTaskAsync(David, project.Id, NewTask("Eigene Aufgabe", assigneeId: David.Id));

        var notification = Assert.Single(await ForAsync(David, assigned.Id));
        Assert.Equal(NotificationTypes.TaskAssigned, notification.Type);
        Assert.Equal("Ben Projektleiter hat dir „Texte schreiben“ zugewiesen", notification.Title);
        Assert.Equal((NotificationResources.Task, project.Id), (notification.ResourceType, notification.ProjectId));
        Assert.Null(notification.ReadAt);
        Assert.Empty(await ForAsync(David, own.Id));
        Assert.Empty(await ForAsync(Ben, assigned.Id));
    }

    [Fact]
    public async Task Changing_the_assignee_notifies_only_the_new_one()
    {
        var project = await CreateTeamProjectAsync();
        var task = await CreateTaskAsync(Ben, project.Id, NewTask("Übergabe", assigneeId: Clara.Id));

        var renamed = await PatchAsync(Ben, task.Id, new { version = task.Version, title = "Übergabe neu" });
        await PatchAsync(Ben, task.Id, new { version = renamed.Version, assigneeId = David.Id });

        Assert.Single(await ForAsync(Clara, task.Id));
        Assert.Equal("Ben Projektleiter hat dir „Übergabe neu“ zugewiesen", Assert.Single(await ForAsync(David, task.Id)).Title);
    }

    [Fact]
    public async Task Mentions_in_task_comments_notify_with_an_excerpt_but_mails_stay_without_content()
    {
        var project = await CreateTeamProjectAsync();
        var task = await CreateTaskAsync(Ben, project.Id, NewTask("Freigabe"));
        var text = "Bitte prüfen: " + new string('x', 300);

        var response = await As(Clara).PostAsJsonAsync($"/api/v1/tasks/{task.Id}/comments", new CreateCommentRequest(text, [Eva.Id, Clara.Id]));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var notification = Assert.Single(await ForAsync(Eva, task.Id));
        Assert.Equal(NotificationTypes.TaskCommentMention, notification.Type);
        Assert.Equal("Clara Editor hat dich bei „Freigabe“ erwähnt", notification.Title);
        Assert.StartsWith("Bitte prüfen: xxx", notification.Body);
        Assert.Equal(NotificationDispatcher.MaxBodyLength, notification.Body!.Length);
        Assert.Empty(await ForAsync(Clara, task.Id));

        var mail = Assert.Single(Outbox(Eva), m => m.Message.Subject == notification.Title);
        Assert.Equal(Eva.Email, mail.Message.To);
        Assert.DoesNotContain("Bitte prüfen", mail.Message.Body);
    }

    [Fact]
    public async Task Mentions_in_knowledge_comments_notify_the_readers_mentioned()
    {
        var response = await As(Clara).PostAsJsonAsync($"/api/v1/knowledge/articles/{KickoffArticle.Id}/comments",
            new CreateKnowledgeCommentRequest("Schau mal, @Felix", [Felix.Id]));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var notification = Assert.Single(await ForAsync(Felix, KickoffArticle.Id), n => n.Body == "Schau mal, @Felix");
        Assert.Equal(NotificationTypes.KnowledgeCommentMention, notification.Type);
        Assert.Equal("Clara Editor hat dich im Artikel „Projekt-Kickoff durchführen“ erwähnt", notification.Title);
        Assert.Equal(NotificationResources.KnowledgeArticle, notification.ResourceType);
        Assert.Null(notification.ProjectId);
    }

    [Fact]
    public async Task Joining_a_project_notifies_the_new_member()
    {
        var project = await CreateProjectAsync(Ben, (Felix, "member"));

        var notification = Assert.Single(await ForAsync(Felix, project.Id));
        Assert.Equal(NotificationTypes.ProjectMemberAdded, notification.Type);
        Assert.Equal($"Ben Projektleiter hat dich zum Projekt „{project.Name}“ hinzugefügt", notification.Title);
        Assert.Equal(project.Id, notification.ProjectId);
        Assert.Empty(await ForAsync(Ben, project.Id));
    }

    [Fact]
    public async Task Preferences_switch_channels_off()
    {
        var project = await CreateTeamProjectAsync();
        try
        {
            Assert.Equal(new NotificationPreferencesResponse(true, true), await As(David).GetFromJsonAsync<NotificationPreferencesResponse>("/api/v1/me/notification-preferences"));

            await SetPreferencesAsync(David, inApp: true, email: false);
            var quiet = await CreateTaskAsync(Ben, project.Id, NewTask("Ohne Mail", assigneeId: David.Id));
            await SetPreferencesAsync(David, inApp: false, email: true);
            var mailOnly = await CreateTaskAsync(Ben, project.Id, NewTask("Nur Mail", assigneeId: David.Id));

            Assert.Single(await ForAsync(David, quiet.Id));
            Assert.DoesNotContain(Outbox(David), m => m.Message.Subject.Contains("Ohne Mail"));
            Assert.Empty(await ForAsync(David, mailOnly.Id));
            Assert.Contains(Outbox(David), m => m.Message.Subject.Contains("Nur Mail"));
            Assert.Equal(new NotificationPreferencesResponse(false, true), await As(David).GetFromJsonAsync<NotificationPreferencesResponse>("/api/v1/me/notification-preferences"));

            var invalid = await As(David).PutAsJsonAsync("/api/v1/me/notification-preferences", new { inAppEnabled = true });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        finally
        {
            await SetPreferencesAsync(David, inApp: true, email: true);
        }
    }

    [Fact]
    public async Task Reading_marks_notifications_and_updates_the_counter()
    {
        var project = await CreateTeamProjectAsync();
        await As(Clara).PostAsync("/api/v1/me/notifications/read-all", null);
        var first = await CreateTaskAsync(Ben, project.Id, NewTask("Eins", assigneeId: Clara.Id));
        await CreateTaskAsync(Ben, project.Id, NewTask("Zwei", assigneeId: Clara.Id));
        Assert.Equal(2, await UnreadAsync(Clara));

        var read = await As(Clara).PostAsync($"/api/v1/notifications/{Assert.Single(await ForAsync(Clara, first.Id)).Id}/read", null);

        Assert.Equal(HttpStatusCode.NoContent, read.StatusCode);
        Assert.Equal(1, await UnreadAsync(Clara));
        Assert.NotNull(Assert.Single(await ForAsync(Clara, first.Id)).ReadAt);
        var unread = await As(Clara).GetFromJsonAsync<PagedResponse<NotificationResponse>>("/api/v1/me/notifications?unreadOnly=true");
        Assert.Equal("Ben Projektleiter hat dir „Zwei“ zugewiesen", Assert.Single(unread!.Items).Title);

        Assert.Equal(HttpStatusCode.NoContent, (await As(Clara).PostAsync("/api/v1/me/notifications/read-all", null)).StatusCode);
        Assert.Equal(0, await UnreadAsync(Clara));
    }

    [Theory]
    [InlineData("dev-eva")]
    [InlineData("dev-fritz")]
    public async Task Notifications_of_others_are_not_found(string objectId)
    {
        var project = await CreateTeamProjectAsync();
        var task = await CreateTaskAsync(Ben, project.Id, NewTask("Privat", assigneeId: Clara.Id));
        var notification = Assert.Single(await ForAsync(Clara, task.Id));
        var other = Users.Single(u => u.ObjectId == objectId);

        var response = await As(other).PostAsync($"/api/v1/notifications/{notification.Id}/read", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await ForAsync(other, task.Id));
        Assert.Null(Assert.Single(await ForAsync(Clara, task.Id)).ReadAt);
        Assert.DoesNotContain(await As(other).GetFromJsonAsync<OutboxMail[]>("/api/v1/dev/outbox") ?? [], m => m.Subject.Contains("Privat"));
    }

    [Fact]
    public async Task Paging_is_bounded()
    {
        var response = await As(Ben).GetAsync("/api/v1/me/notifications?limit=500");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task New_notifications_are_pushed_to_the_recipient_only()
    {
        var project = await CreateTeamProjectAsync();
        await As(David).PostAsync("/api/v1/me/notifications/read-all", null);
        await using var david = await ConnectAsync(David);
        await using var clara = await ConnectAsync(Clara);

        await CreateTaskAsync(Ben, project.Id, NewTask("Live", assigneeId: David.Id));

        Assert.Equal(new NotificationsChangedMessage(1), await david.NextAsync());
        await As(David).PostAsync("/api/v1/me/notifications/read-all", null);
        Assert.Equal(new NotificationsChangedMessage(0), await david.NextAsync());
        Assert.False(clara.Messages.Reader.TryRead(out _));
    }

    private async Task<List<NotificationResponse>> ForAsync(SeedUser user, Guid resourceId)
    {
        var page = await As(user).GetFromJsonAsync<PagedResponse<NotificationResponse>>("/api/v1/me/notifications?limit=100");
        return page!.Items.Where(n => n.ResourceId == resourceId).ToList();
    }

    private async Task<int> UnreadAsync(SeedUser user) =>
        (await As(user).GetFromJsonAsync<UnreadCountResponse>("/api/v1/me/notifications/unread-count"))!.Count;

    private IReadOnlyList<SentEmail> Outbox(SeedUser user) =>
        Factory.Services.GetRequiredService<FakeEmailSender>().Outbox.Where(m => m.Message.RecipientId == user.Id).ToList();

    private async Task SetPreferencesAsync(SeedUser user, bool inApp, bool email)
    {
        var response = await As(user).PutAsJsonAsync("/api/v1/me/notification-preferences", new UpdatePreferencesRequest(inApp, email));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<ProjectHub.Api.Modules.Tasks.TaskResponse> PatchAsync(SeedUser user, Guid taskId, object body)
    {
        var response = await As(user).PatchAsJsonAsync($"/api/v1/tasks/{taskId}", body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProjectHub.Api.Modules.Tasks.TaskResponse>())!;
    }

    private async Task<Listener> ConnectAsync(SeedUser user)
    {
        var server = Factory.Server;
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, NotificationsHub.Path.TrimStart('/')), options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.Headers[DevelopmentIdentityOptions.UserHeader] = user.ObjectId;
            })
            .Build();

        var listener = new Listener(connection);
        connection.On<NotificationsChangedMessage>(nameof(INotificationsClient.NotificationsChanged), m => listener.Messages.Writer.TryWrite(m));
        await connection.StartAsync();
        return listener;
    }

    private sealed class Listener(HubConnection connection) : IAsyncDisposable
    {
        public Channel<NotificationsChangedMessage> Messages { get; } = Channel.CreateUnbounded<NotificationsChangedMessage>();

        public async Task<NotificationsChangedMessage> NextAsync()
        {
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            return await Messages.Reader.ReadAsync(cancel.Token);
        }

        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }
}
