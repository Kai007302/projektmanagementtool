using ProjectHub.Api;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Infrastructure.Health;
using ProjectHub.Api.Modules.Identity;

var builder = WebApplication.CreateBuilder(args);

var settings = ProjectHubSettings.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(settings);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddProjectHubHealthChecks(settings);
builder.Services.AddIdentityModule(builder.Environment, builder.Configuration);

var app = builder.Build();

if (settings.ApplyMigrationsOnStartup)
{
    DatabaseMigrator.Migrate(settings.DatabaseConnection, app.Logger);
}

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.MapProjectHubHealthChecks();

app.Run();

public partial class Program;
