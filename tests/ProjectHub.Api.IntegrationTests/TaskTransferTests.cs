using System.Net;
using System.Net.Http.Json;
using System.Text;
using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Modules.Tasks;
using ProjectHub.Api.Modules.Tasks.Transfer;
using static ProjectHub.Api.Modules.Identity.Development.DevelopmentSeedData;

namespace ProjectHub.Api.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class TaskTransferTests(InfrastructureFixture infrastructure) : ApiTestBase(infrastructure)
{
    private static readonly TableLimits Limits = new(10_000, 50, 20_000);

    [Fact]
    public async Task Csv_export_lists_every_task_with_labels_and_parents_before_subtasks()
    {
        var project = await CreateTeamProjectAsync();
        var parent = await CreateTaskAsync(Ben, project.Id, NewTask("=Planung", assigneeIds: [David.Id, Clara.Id], startDate: new DateOnly(2026, 11, 2), dueDate: new DateOnly(2026, 11, 4), status: "in_progress", priority: "high"));
        await CreateTaskAsync(Ben, project.Id, NewTask("Später"));
        await CreateTaskAsync(Ben, project.Id, NewTask("Teil", parentTaskId: parent.Id, status: "in_progress"));

        var response = await As(Eva).GetAsync($"/api/v1/projects/{project.Id}/tasks/export?format=csv");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var rows = CsvTable.Read(bytes, Limits);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.EndsWith(".csv", response.Content.Headers.ContentDisposition?.FileNameStar, StringComparison.Ordinal);
        Assert.Equal(TaskSheet.Columns.Select(c => c.Header), rows[0]);
        Assert.Equal(["=Planung", "Teil", "Später"], rows.Skip(1).Select(r => r[0]));
        Assert.Equal(
            ["=Planung", "", "In Arbeit", "Hoch", $"{Clara.Email}; {David.Email}", $"{Clara.DisplayName}; {David.DisplayName}", "2026-11-02", "2026-11-04", "50", "", "",
             parent.Id.ToString()],
            rows[1]);
        Assert.Equal("50", rows[2][8]);
        Assert.Equal("=Planung", rows[2][10]);
        Assert.Contains("'=Planung", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Excel_export_opens_as_workbook()
    {
        var project = await CreateTeamProjectAsync();
        await CreateTaskAsync(Ben, project.Id, NewTask("Abgabe", dueDate: new DateOnly(2026, 12, 1)));

        var response = await As(Ben).GetAsync($"/api/v1/projects/{project.Id}/tasks/export?format=xlsx");
        var rows = XlsxTable.Read(await response.Content.ReadAsByteArrayAsync(), Limits);

        Assert.Equal(XlsxTable.ContentType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Abgabe", rows[1][0]);
        Assert.Equal(new DateOnly(2026, 12, 1), TaskSheet.ParseDate(rows[1][7]));
    }

    [Theory]
    [InlineData("dev-felix")]
    [InlineData("dev-fritz")]
    public async Task Outsiders_cannot_export_or_import(string objectId)
    {
        var project = await CreateTeamProjectAsync();
        var other = Users.Single(u => u.ObjectId == objectId);

        Assert.Equal(HttpStatusCode.NotFound, (await As(other).GetAsync($"/api/v1/projects/{project.Id}/tasks/export")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ImportAsync(other, project.Id, "Titel\nA\n")).StatusCode);
    }

    [Fact]
    public async Task Unknown_format_is_rejected()
    {
        var project = await CreateTeamProjectAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await As(Ben).GetAsync($"/api/v1/projects/{project.Id}/tasks/export?format=pdf")).StatusCode);
    }

    [Fact]
    public async Task Preview_checks_the_file_without_creating_tasks_and_import_creates_all_rows()
    {
        var project = await CreateTeamProjectAsync();
        var csv = $"Titel;Status;Priorität;Zuständig (E-Mail);Start;Fällig;Fortschritt (%);Aufwand (h);Kostenstelle\n"
                  + $"Texte;In Arbeit;Hoch;{David.Email.ToUpperInvariant()};02.11.2026;04.11.2026;50;1,5;4711\n"
                  + "Bilder;;;;;;;;\n";

        var preview = await ReadResultAsync(await ImportAsync(David, project.Id, csv, dryRun: true));
        Assert.Equal((2, 0), (preview.Rows, preview.Created));
        Assert.Equal(["Fortschritt (%)", "Kostenstelle"], preview.IgnoredColumns);
        Assert.Empty(preview.Errors);
        Assert.Equal(0, await CountTasksAsync(project.Id));

        var imported = await ReadResultAsync(await ImportAsync(David, project.Id, csv));
        Assert.Equal(2, imported.Created);

        var tasks = (await As(David).GetFromJsonAsync<PagedResponse<TaskResponse>>($"/api/v1/projects/{project.Id}/tasks?limit=10"))!.Items;
        var texte = tasks.Single(t => t.Title == "Texte");
        Assert.Equal(("in_progress", "high", David.Id, new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 4), (short)50, 1.5m),
            (texte.Status, texte.Priority, texte.Assignees.Single().Id, texte.StartDate!.Value, texte.DueDate!.Value, texte.Progress, texte.EstimatedHours!.Value));
        var bilder = tasks.Single(t => t.Title == "Bilder");
        Assert.Equal(("todo", "normal", 0, (Guid?)null), (bilder.Status, bilder.Priority, bilder.Assignees.Count, bilder.ParentTaskId));
        Assert.Equal(1, await ScalarAsync("select count(*) from activity_log where project_id = $1 and action = 'TasksImported'", project.Id));
    }

    [Fact]
    public async Task One_bad_row_stops_the_whole_import_and_every_error_is_listed()
    {
        var project = await CreateTeamProjectAsync();
        var csv = "Titel;Zuständig;Fällig;Start\n"
                  + "Gut;;;\n"
                  + $"Gast zuweisen;{Gina.Email};;\n"
                  + ";;morgen;\n"
                  + "Rückwärts;;01.11.2026;05.11.2026\n";

        var result = await ReadResultAsync(await ImportAsync(Ben, project.Id, csv));

        Assert.Equal(0, result.Created);
        Assert.Equal(
            [(3, TaskImportErrorCodes.UnknownAssignee), (4, TaskImportErrorCodes.Required), (4, TaskImportErrorCodes.InvalidDate), (5, TaskImportErrorCodes.Invalid)],
            result.Errors.Select(e => (e.Row, e.Code)));
        Assert.Equal(0, await CountTasksAsync(project.Id));
    }

    [Fact]
    public async Task Viewers_cannot_import()
    {
        var project = await CreateTeamProjectAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await ImportAsync(Eva, project.Id, "Titel\nA\n")).StatusCode);
    }

    [Fact]
    public async Task Excel_files_import_and_an_export_imports_again()
    {
        var project = await CreateTeamProjectAsync();
        await CreateTaskAsync(Ben, project.Id, NewTask("Rundreise", assigneeId: Clara.Id, dueDate: new DateOnly(2026, 12, 24), priority: "urgent"));
        var export = await (await As(Ben).GetAsync($"/api/v1/projects/{project.Id}/tasks/export?format=xlsx")).Content.ReadAsByteArrayAsync();
        var target = await CreateTeamProjectAsync();

        var result = await ReadResultAsync(await ImportAsync(Ben, target.Id, export, "aufgaben.xlsx"));

        Assert.Empty(result.Errors);
        Assert.Equal(["Fortschritt (%)", "Übergeordnete Aufgabe", "ID"], result.IgnoredColumns);
        var task = (await As(Ben).GetFromJsonAsync<PagedResponse<TaskResponse>>($"/api/v1/projects/{target.Id}/tasks?limit=10"))!.Items.Single();
        Assert.Equal(("Rundreise", Clara.Id, new DateOnly(2026, 12, 24), "urgent"), (task.Title, task.Assignees.Single().Id, task.DueDate!.Value, task.Priority));
    }

    [Theory]
    [InlineData("aufgaben.docx", "Titel\nA\n")]
    [InlineData("aufgaben.csv", "Titel\n\"offen\n")]
    [InlineData("aufgaben.xlsx", "kein Excel")]
    public async Task Unreadable_files_are_rejected(string fileName, string content)
    {
        var project = await CreateTeamProjectAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await ImportAsync(Ben, project.Id, Encoding.UTF8.GetBytes(content), fileName)).StatusCode);
    }

    [Fact]
    public async Task A_file_without_title_column_is_explained()
    {
        var project = await CreateTeamProjectAsync();

        var result = await ReadResultAsync(await ImportAsync(Ben, project.Id, "Name der Aufgabe;Wann\nA;morgen\n"));

        Assert.Equal(TaskImportErrorCodes.MissingTitleColumn, Assert.Single(result.Errors).Code);
    }

    private Task<HttpResponseMessage> ImportAsync(SeedUser user, Guid projectId, string csv, bool dryRun = false) =>
        ImportAsync(user, projectId, Encoding.UTF8.GetBytes(csv), "aufgaben.csv", dryRun);

    private Task<HttpResponseMessage> ImportAsync(SeedUser user, Guid projectId, byte[] content, string fileName, bool dryRun = false)
    {
        var form = new MultipartFormDataContent { { new ByteArrayContent(content), "file", fileName } };
        return As(user).PostAsync($"/api/v1/projects/{projectId}/tasks/import?dryRun={dryRun.ToString().ToLowerInvariant()}", form);
    }

    private static async Task<TaskImportResult> ReadResultAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TaskImportResult>())!;
    }

    private Task<long> CountTasksAsync(Guid projectId) => ScalarAsync("select count(*) from task where project_id = $1", projectId);
}
