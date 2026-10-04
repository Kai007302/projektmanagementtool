using ProjectHub.Api;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Infrastructure.Health;
using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Infrastructure.Observability;
using ProjectHub.Api.Infrastructure.Events;
using ProjectHub.Api.Modules.Attachments;
using ProjectHub.Api.Modules.Audit;
using ProjectHub.Api.Modules.Calendar;
using ProjectHub.Api.Modules.Comments;
using ProjectHub.Api.Modules.Gantt;
using ProjectHub.Api.Modules.Identity;
using ProjectHub.Api.Modules.Identity.Development;
using ProjectHub.Api.Modules.Integrations.Microsoft;
using ProjectHub.Api.Modules.Integrations.Webex;
using ProjectHub.Api.Modules.Kanban;
using ProjectHub.Api.Modules.Knowledge;
using ProjectHub.Api.Modules.Notifications;
using ProjectHub.Api.Modules.Organizations;
using ProjectHub.Api.Modules.Projects;
using ProjectHub.Api.Modules.Realtime;
using ProjectHub.Api.Modules.Tasks;
using ProjectHub.Api.Modules.Teams;
using ProjectHub.Api.Modules.Users;
using ProjectHub.Api.Modules.Whiteboard;

if (args is [WhiteboardEngineHost.Argument])
{
    // Child process that decodes Yjs updates in isolation (ADR 0009); see WhiteboardEngine.
    WhiteboardEngineHost.Run();
    return;
}

if (args is [DatabaseMigrator.MigrateOnlyArgument])
{
    // One-off migration run before the API starts (docker-compose.prod.yml); the API itself never migrates there.
    DatabaseMigrator.MigrateOnly();
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.AddProjectHubTelemetry();

var settings = ProjectHubSettings.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(settings);
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddProblemDetails();
builder.Services.AddHttpHardening(builder.Configuration);
builder.Services.AddOpenApi();
builder.Services.AddProjectHubHealthChecks(settings);
builder.Services.AddProjectHubDatabase(settings);
builder.Services.AddIdentityModule(builder.Environment, builder.Configuration);
builder.Services.AddAuditModule();
builder.Services.AddDomainEvents();
builder.Services.AddTeamsModule();
builder.Services.AddProjectsModule();
builder.Services.AddTasksModule();
builder.Services.AddCommentsModule();
builder.Services.AddKanbanModule();
builder.Services.AddGanttModule();
builder.Services.AddNotificationsModule(builder.Configuration);
builder.Services.AddMailTransport(builder.Configuration);
builder.Services.AddCalendarModule();
builder.Services.AddWebexModule(builder.Configuration, builder.Environment);
builder.Services.AddKnowledgeModule();
builder.Services.AddRealtimeModule(settings);
builder.Services.AddAttachmentsModule(builder.Configuration, builder.Environment);
builder.Services.AddWhiteboardModule(builder.Configuration, builder.Environment);

var app = builder.Build();

if (settings.ApplyMigrationsOnStartup)
{
    DatabaseMigrator.Migrate(settings.DatabaseConnection, app.Logger);
}

if (settings.SeedDevelopmentData && app.Environment.IsDevelopment())
{
    await DevelopmentSeeder.SeedAsync(settings.DatabaseConnection);
}

app.UseForwardedHeadersWhenTrusted();
app.UseSecurityHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.UseUserContext();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapDevelopmentIdentityEndpoints(app.Environment);
    app.MapApiV1().MapNotificationDevelopmentEndpoints().MapWebexDevelopmentEndpoints();
}

app.MapProjectHubHealthChecks();

app.MapApiV1()
    .MapSignInEndpoints(app.Environment, app.Configuration)
    .MapUserEndpoints()
    .MapOrganizationEndpoints()
    .MapTeamEndpoints()
    .MapProjectEndpoints()
    .MapTaskEndpoints()
    .MapCommentEndpoints()
    .MapAttachmentEndpoints(app.Services.GetRequiredService<AttachmentOptions>())
    .MapKanbanEndpoints()
    .MapGanttEndpoints()
    .MapCalendarEndpoints()
    .MapWebexEndpoints()
    .MapNotificationEndpoints()
    .MapWhiteboardEndpoints()
    .MapKnowledgeEndpoints();

app.MapRealtimeEndpoints();
app.MapNotificationHub();
app.MapWhiteboardHub();
app.MapWebexWebhook();

app.Run();

public partial class Program;
