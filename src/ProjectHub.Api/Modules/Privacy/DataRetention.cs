using Microsoft.EntityFrameworkCore;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Modules.Audit;
using ProjectHub.Api.Modules.Notifications;

namespace ProjectHub.Api.Modules.Privacy;

/// <summary>
/// How long personal data in logs and notifications is kept (Art. 5 (1) e GDPR, DEC-005, ADR 0017), in days.
/// 0 keeps the data forever. Default: three years each (Kai, 2026-10-05). Mail outbox and webhook events have their own fixed cleanup.
/// </summary>
public sealed record RetentionOptions(int NotificationDays, int ActivityLogDays, int AuditLogDays)
{
    public const string NotificationDaysKey = "PROJECTHUB_RETENTION_NOTIFICATION_DAYS";
    public const string ActivityLogDaysKey = "PROJECTHUB_RETENTION_ACTIVITY_DAYS";
    public const string AuditLogDaysKey = "PROJECTHUB_RETENTION_AUDIT_DAYS";

    public static readonly RetentionOptions Default = new(1095, 1095, 1095);

    public static RetentionOptions FromConfiguration(IConfiguration configuration) =>
        new(
            Days(configuration, NotificationDaysKey, Default.NotificationDays),
            Days(configuration, ActivityLogDaysKey, Default.ActivityLogDays),
            Days(configuration, AuditLogDaysKey, Default.AuditLogDays));

    private static int Days(IConfiguration configuration, string key, int fallback)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return int.TryParse(value, out var days) && days >= 0
            ? days
            : throw new InvalidOperationException($"Configuration value '{key}' must be a number of days (0 keeps data forever).");
    }
}

public sealed record RetentionResult(int Notifications, int ActivityEntries, int AuditEntries);

/// <summary>Deletes notifications, activity and audit entries older than <see cref="RetentionOptions"/> allow.</summary>
public sealed class DataRetention(IServiceScopeFactory scopes, RetentionOptions options, TimeProvider clock)
{
    public async Task<RetentionResult> PurgeAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ProjectHubDbContext>();
        var now = clock.GetUtcNow();

        var notifications = 0;
        if (options.NotificationDays > 0)
        {
            var before = now.AddDays(-options.NotificationDays);
            notifications = await db.Set<Notification>().Where(n => n.CreatedAt < before).ExecuteDeleteAsync(ct);
        }

        var activity = 0;
        if (options.ActivityLogDays > 0)
        {
            var before = now.AddDays(-options.ActivityLogDays);
            activity = await db.Set<ActivityLogEntry>().Where(a => a.CreatedAt < before).ExecuteDeleteAsync(ct);
        }

        // Normal users can neither change nor delete audit entries; only this retention removes them.
        var audit = 0;
        if (options.AuditLogDays > 0)
        {
            var before = now.AddDays(-options.AuditLogDays);
            audit = await db.Set<AuditLogEntry>().Where(a => a.CreatedAt < before).ExecuteDeleteAsync(ct);
        }

        return new RetentionResult(notifications, activity, audit);
    }
}

/// <summary>Runs <see cref="DataRetention"/> shortly after start and then every six hours. Deleting is idempotent, so every instance may run it.</summary>
internal sealed class DataRetentionWorker(DataRetention retention, ILogger<DataRetentionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private static readonly TimeSpan StartDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                var result = await retention.PurgeAsync(stoppingToken);
                if (result is not { Notifications: 0, ActivityEntries: 0, AuditEntries: 0 })
                {
                    logger.LogInformation(
                        "Retention deleted {Notifications} notifications, {ActivityEntries} activity and {AuditEntries} audit entries",
                        result.Notifications, result.ActivityEntries, result.AuditEntries);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Retention run failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
