using ProjectHub.Api;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Infrastructure.Health;
using ProjectHub.Api.Modules.Audit;
using ProjectHub.Api.Modules.Identity;
using ProjectHub.Api.Modules.Identity.Development;
using ProjectHub.Api.Modules.Organizations;
using ProjectHub.Api.Modules.Teams;
using ProjectHub.Api.Modules.Users;

var builder = WebApplication.CreateBuilder(args);

var settings = ProjectHubSettings.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(settings);
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddProjectHubHealthChecks(settings);
builder.Services.AddProjectHubDatabase(settings);
builder.Services.AddIdentityModule(builder.Environment, builder.Configuration);
builder.Services.AddAuditModule();
builder.Services.AddTeamsModule();

var app = builder.Build();

if (settings.ApplyMigrationsOnStartup)
{
    DatabaseMigrator.Migrate(settings.DatabaseConnection, app.Logger);
}

if (settings.SeedDevelopmentData && app.Environment.IsDevelopment())
{
    await DevelopmentSeeder.SeedAsync(settings.DatabaseConnection);
}

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseAuthentication();
app.UseAuthorization();
app.UseUserContext();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapDevelopmentIdentityEndpoints(app.Environment);
}

app.MapProjectHubHealthChecks();

app.MapApiV1()
    .MapUserEndpoints()
    .MapOrganizationEndpoints()
    .MapTeamEndpoints();

app.Run();

public partial class Program;
