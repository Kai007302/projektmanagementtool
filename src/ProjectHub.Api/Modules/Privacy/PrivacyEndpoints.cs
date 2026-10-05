using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using ProjectHub.Api.Infrastructure.Http;
using ProjectHub.Api.Modules.Identity;

namespace ProjectHub.Api.Modules.Privacy;

/// <summary>Data protection functions (ADR 0017): legal links, own data export, anonymization, retention.</summary>
public static class PrivacyEndpoints
{
    public static IServiceCollection AddPrivacyModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(LegalInformation.FromConfiguration(configuration));
        services.AddSingleton(RetentionOptions.FromConfiguration(configuration));
        services.AddSingleton<DataRetention>();
        services.AddHostedService<DataRetentionWorker>();
        services.AddScoped<PersonalDataExportService>();
        services.AddScoped<UserAnonymizationService>();
        return services;
    }

    public static RouteGroupBuilder MapPrivacyEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/legal", (LegalInformation legal) => Results.Ok(legal)).AllowAnonymous();

        api.MapGet("/me/data-export", async (
            UserContext user, PersonalDataExportService service, TimeProvider clock, IOptions<JsonOptions> json, CancellationToken ct) =>
        {
            var export = await service.ExportAsync(user, ct);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(export, new JsonSerializerOptions(json.Value.SerializerOptions) { WriteIndented = true });
            return Results.File(bytes, "application/json", $"projecthub-meine-daten-{clock.GetUtcNow():yyyy-MM-dd}.json");
        });

        api.MapPost("/admin/users/{id:guid}/anonymize", async (Guid id, UserContext user, UserAnonymizationService service, CancellationToken ct) =>
            ApiResults.NoContent(await service.AnonymizeAsync(user, id, ct)));

        return api;
    }
}
