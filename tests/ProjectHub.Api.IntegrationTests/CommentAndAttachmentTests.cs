using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ProjectHub.Api.Infrastructure.Events;
using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Modules.Attachments;
using ProjectHub.Api.Modules.Comments;
using static ProjectHub.Api.Modules.Identity.Development.DevelopmentSeedData;

namespace ProjectHub.Api.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class CommentAndAttachmentTests(InfrastructureFixture infrastructure) : ApiTestBase(infrastructure)
{
    private const long MaxUploadBytes = 1024;

    private readonly MentionRecorder mentions = new();

    protected override ProjectHubApiFactory CreateFactory() =>
        new(Infrastructure.Postgres.GetConnectionString(), Infrastructure.Redis.GetConnectionString(), configure: builder =>
        {
            builder.UseSetting(AttachmentOptions.MaxSizeKey, MaxUploadBytes.ToString());
            builder.ConfigureTestServices(services => services.AddSingleton<IDomainEventHandler<UsersMentionedInComment>>(mentions));
        });

    [Fact]
    public async Task Member_comments_and_everyone_in_the_project_reads_it()
    {
        var project = await CreateTeamProjectAsync();
        var task = await CreateTaskAsync(Ben, project.Id);

        var response = await As(David).PostAsJsonAsync($"/api/v1/tasks/{task.Id}/comments", new CreateCommentRequest("Erledigt bis Freitag", null));
        var comments = await As(Gina).GetFromJsonAsync<PagedResponse<CommentResponse>>($"/api/v1/tasks/{task.Id}/comments");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var comment = Assert.Single(comments!.Items);
        Assert.Equal("Erledigt bis Freitag", comment.Content);
        Assert.Equal("David Mitglied", comment.AuthorName);
        Assert.Equal(1L, await ScalarAsync("select count(*) from activity_log where action = 'CommentAdded' and resource_id = $1", comment.Id));
    }

    [Fact]
    public async Task Viewer_cannot_comment_and_outsiders_cannot_read()
    {
        var project = await CreateTeamProjectAsync();
        var task = await CreateTaskAsync(Ben, project.Id);

        var byViewer = await As(Eva).PostAsJsonAsync($"/api/v1/tasks/{task.Id}/comments", new CreateCommentRequest("Hallo", null));
        var byOutsider = await As(Fritz).GetAsync($"/api/v1/tasks/{task.Id}/comments");

        Assert.Equal(HttpStatusCode.Forbidden, byViewer.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, byOutsider.StatusCode);
    }

    [Fact]
    public async Task Empty_comment_is_rejected()
    {
        var project = await CreateTeamProjectAsync();
        var task = await CreateTaskAsync(Ben, project.Id);

        var response = await As(Ben).PostAsJsonAsync($"/api/v1/tasks/{task.Id}/comments", new CreateCommentRequest("  ", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Mentions_raise_a_domain_event_for_project_members()
    {
        var project = await CreateTeamProjectAsync();
        var task = await CreateTaskAsync(Ben, project.Id);

        var response = await As(Ben).PostAsJsonAsync($"/api/v1/tasks/{task.Id}/comments", new CreateCommentRequest("@Eva @Clara bitte prüfen", [Eva.Id, Clara.Id]));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var mention = Assert.Single(mentions.Events, e => e.TaskId == task.Id);
        Assert.Equal([Eva.Id, Clara.Id], mention.MentionedUserIds);
        Assert.Equal(Ben.Id, mention.AuthorId);
    }

    [Theory]
    [InlineData("dev-felix")]
    [InlineData("dev-fritz")]
    public async Task Only_project_members_can_be_mentioned(string objectId)
    {
        var project = await CreateTeamProjectAsync();
        var task = await CreateTaskAsync(Ben, project.Id);
        var outsider = Users.Single(u => u.ObjectId == objectId);

        var response = await As(Ben).PostAsJsonAsync($"/api/v1/tasks/{task.Id}/comments", new CreateCommentRequest("Hallo", [outsider.Id]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(mentions.Events, e => e.TaskId == task.Id);
    }

    [Fact]
    public async Task Only_the_author_edits_a_comment_with_the_current_version()
    {
        var project = await CreateTeamProjectAsync();
        var task = await CreateTaskAsync(Ben, project.Id);
        var comment = await AddCommentAsync(David, task.Id, "Erste Fassung");

        var byOther = await As(Ben).PatchAsJsonAsync($"/api/v1/comments/{comment.Id}", new UpdateCommentRequest("Fremd", comment.Version));
        var byAuthor = await As(David).PatchAsJsonAsync($"/api/v1/comments/{comment.Id}", new UpdateCommentRequest("Zweite Fassung", comment.Version));
        var stale = await As(David).PatchAsJsonAsync($"/api/v1/comments/{comment.Id}", new UpdateCommentRequest("Veraltet", comment.Version));

        Assert.Equal(HttpStatusCode.Forbidden, byOther.StatusCode);
        Assert.Equal(HttpStatusCode.OK, byAuthor.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    [Fact]
    public async Task Author_or_project_admin_deletes_a_comment()
    {
        var project = await CreateTeamProjectAsync();
        var task = await CreateTaskAsync(Ben, project.Id);
        var comment = await AddCommentAsync(David, task.Id, "Kommentar");

        var byEditor = await As(Clara).DeleteAsync($"/api/v1/comments/{comment.Id}");
        var byAdmin = await As(Ben).DeleteAsync($"/api/v1/comments/{comment.Id}");
        var comments = await As(Ben).GetFromJsonAsync<PagedResponse<CommentResponse>>($"/api/v1/tasks/{task.Id}/comments");

        Assert.Equal(HttpStatusCode.Forbidden, byEditor.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, byAdmin.StatusCode);
        Assert.Empty(comments!.Items);
    }

    [Fact]
    public async Task Attachment_is_uploaded_listed_and_downloaded()
    {
        var project = await CreateTeamProjectAsync();
        var task = await CreateTaskAsync(Ben, project.Id);
        var bytes = "Inhalt der Datei"u8.ToArray();

        var upload = await UploadAsync(David, task.Id, bytes, "../../notiz.txt");
        var list = await As(Eva).GetFromJsonAsync<List<AttachmentResponse>>($"/api/v1/tasks/{task.Id}/attachments");
        var download = await As(Eva).GetAsync($"/api/v1/attachments/{list![0].Id}/content");

        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var attachment = Assert.Single(list);
        Assert.Equal("notiz.txt", attachment.FileName);
        Assert.Equal(bytes.Length, attachment.SizeBytes);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(bytes, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Contains("nosniff", download.Headers.GetValues("X-Content-Type-Options"));
    }

    [Fact]
    public async Task Viewer_cannot_upload_and_outsiders_cannot_download()
    {
        var project = await CreateTeamProjectAsync();
        var task = await CreateTaskAsync(Ben, project.Id);
        var upload = await UploadAsync(Ben, task.Id, [1, 2, 3], "daten.bin");
        var attachment = (await upload.Content.ReadFromJsonAsync<AttachmentResponse>())!;

        var byViewer = await UploadAsync(Eva, task.Id, [1], "x.bin");
        var byOutsider = await As(Fritz).GetAsync($"/api/v1/attachments/{attachment.Id}/content");

        Assert.Equal(HttpStatusCode.Forbidden, byViewer.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, byOutsider.StatusCode);
    }

    [Fact]
    public async Task Oversized_attachment_is_rejected()
    {
        var project = await CreateTeamProjectAsync();
        var task = await CreateTaskAsync(Ben, project.Id);

        var response = await UploadAsync(Ben, task.Id, new byte[MaxUploadBytes + 1], "gross.bin");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Uploader_deletes_an_attachment_and_its_file()
    {
        var project = await CreateTeamProjectAsync();
        var task = await CreateTaskAsync(Ben, project.Id);
        var attachment = (await (await UploadAsync(David, task.Id, [1, 2], "x.bin")).Content.ReadFromJsonAsync<AttachmentResponse>())!;

        var byEditor = await As(Clara).DeleteAsync($"/api/v1/attachments/{attachment.Id}");
        var byUploader = await As(David).DeleteAsync($"/api/v1/attachments/{attachment.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, byEditor.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, byUploader.StatusCode);
        Assert.Empty(Directory.EnumerateFiles(Factory.AttachmentDirectory, "*", SearchOption.AllDirectories));
    }

    private async Task<CommentResponse> AddCommentAsync(SeedUser user, Guid taskId, string content)
    {
        var response = await As(user).PostAsJsonAsync($"/api/v1/tasks/{taskId}/comments", new CreateCommentRequest(content, null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CommentResponse>())!;
    }

    private Task<HttpResponseMessage> UploadAsync(SeedUser user, Guid taskId, byte[] bytes, string fileName)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var form = new MultipartFormDataContent { { file, "file", fileName } };
        return As(user).PostAsync($"/api/v1/tasks/{taskId}/attachments", form);
    }

    private sealed class MentionRecorder : IDomainEventHandler<UsersMentionedInComment>
    {
        private readonly ConcurrentQueue<UsersMentionedInComment> events = new();

        public IReadOnlyCollection<UsersMentionedInComment> Events => events;

        public Task HandleAsync(UsersMentionedInComment domainEvent, CancellationToken ct)
        {
            events.Enqueue(domainEvent);
            return Task.CompletedTask;
        }
    }
}
