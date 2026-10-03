using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProjectHub.Api.Infrastructure.Database;

namespace ProjectHub.Api.Modules.Notifications;

/// <summary>A notification mail waiting for (or done with) delivery (ADR 0010).</summary>
public sealed class MailOutboxEntry
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public Guid RecipientId { get; init; }
    public required string ToAddress { get; init; }
    public required string Subject { get; init; }
    public required string Body { get; init; }
    public string Channel { get; init; } = MailOutboxChannel.Email;
    public string Status { get; init; } = MailOutboxStatus.Pending;
    public int Attempts { get; init; }
    public DateTimeOffset NextAttemptAt { get; init; }
    public string? LastError { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? SentAt { get; init; }
}

public static class MailOutboxChannel
{
    public const string Email = "email";
    public const string Webex = "webex";
}

public static class MailOutboxStatus
{
    public const string Pending = "pending";
    public const string Sent = "sent";
    public const string Failed = "failed";
}

internal sealed class MailOutboxEntryConfiguration : IEntityTypeConfiguration<MailOutboxEntry>
{
    public void Configure(EntityTypeBuilder<MailOutboxEntry> builder) => builder.ToTable("mail_outbox");
}

public sealed record MailOutboxOptions(TimeSpan PollInterval)
{
    /// <summary>Seconds between looks at the outbox when nothing wakes the worker; 0 switches the worker off (tests).</summary>
    public const string PollSecondsKey = "PROJECTHUB_MAIL_POLL_SECONDS";

    public static MailOutboxOptions FromConfiguration(IConfiguration configuration) =>
        new(TimeSpan.FromSeconds(configuration.GetValue(PollSecondsKey, 15)));
}

/// <summary>Wakes the outbox worker of this instance as soon as new mails were stored.</summary>
public sealed class MailOutboxSignal
{
    private readonly SemaphoreSlim signal = new(0, 1);

    public void Notify()
    {
        try
        {
            if (signal.CurrentCount == 0)
            {
                signal.Release();
            }
        }
        catch (SemaphoreFullException)
        {
            // Someone else woke the worker in the meantime.
        }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct) => signal.WaitAsync(timeout, ct);
}

/// <summary>
/// Delivers due outbox mails through <see cref="IEmailSender"/>. Rows are reserved with a lease
/// (<c>FOR UPDATE SKIP LOCKED</c>), so several instances never send the same row at the same time, and sent
/// outside the reserving transaction. A crash while sending means the lease runs out and the mail is tried again.
/// </summary>
public sealed class MailOutbox(
    IServiceScopeFactory scopes, IEmailSender sender, IWebexMessageSender webex, TimeProvider clock, ILogger<MailOutbox> logger)
{
    public const int BatchSize = 20;
    public const int MaxAttempts = 8;
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan KeepSent = TimeSpan.FromDays(7);
    public static readonly TimeSpan KeepFailed = TimeSpan.FromDays(30);
    private const int MaxErrorLength = 200;

    /// <summary>Wait before the next attempt: Retry-After if the mail system asked for it, otherwise 30 s doubling up to 1 h.</summary>
    public static TimeSpan RetryDelay(int attempts, TimeSpan? retryAfter)
    {
        if (retryAfter is { } wanted && wanted > TimeSpan.Zero)
        {
            return wanted < TimeSpan.FromHours(1) ? wanted : TimeSpan.FromHours(1);
        }

        var seconds = 30 * Math.Pow(2, Math.Clamp(attempts - 1, 0, 10));
        return TimeSpan.FromSeconds(Math.Min(seconds, 3600));
    }

    /// <summary>Reserves and sends up to <see cref="BatchSize"/> due mails. Returns how many were reserved.</summary>
    public async Task<int> SendDueAsync(CancellationToken ct)
    {
        List<MailOutboxEntry> claimed;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ProjectHubDbContext>();
            var now = clock.GetUtcNow();

            // Rows whose sender died on the last allowed attempt would otherwise stay pending forever.
            await db.Set<MailOutboxEntry>()
                .Where(m => m.Status == MailOutboxStatus.Pending && m.Attempts >= MaxAttempts && m.NextAttemptAt <= now)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, MailOutboxStatus.Failed).SetProperty(m => m.LastError, "lease expired"), ct);

            var leaseEnd = now + Lease;
            claimed = await db.Set<MailOutboxEntry>().FromSql($"""
                update mail_outbox set attempts = attempts + 1, next_attempt_at = {leaseEnd}
                where id in (
                    select id from mail_outbox
                    where status = 'pending' and next_attempt_at <= {now} and attempts < {MaxAttempts}
                    order by next_attempt_at
                    limit {BatchSize}
                    for update skip locked)
                returning *
                """).ToListAsync(ct);
        }

        foreach (var mail in claimed)
        {
            await SendAsync(mail, ct);
        }

        return claimed.Count;
    }

    /// <summary>Deletes sent mails after <see cref="KeepSent"/> and failed ones after <see cref="KeepFailed"/>.</summary>
    public async Task<int> CleanupAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ProjectHubDbContext>();
        var now = clock.GetUtcNow();
        var sentBefore = now - KeepSent;
        var failedBefore = now - KeepFailed;
        return await db.Set<MailOutboxEntry>()
            .Where(m => (m.Status == MailOutboxStatus.Sent && m.SentAt < sentBefore)
                || (m.Status == MailOutboxStatus.Failed && m.CreatedAt < failedBefore))
            .ExecuteDeleteAsync(ct);
    }

    private async Task SendAsync(MailOutboxEntry mail, CancellationToken ct)
    {
        string? error;
        var transient = false;
        TimeSpan? retryAfter = null;
        try
        {
            var message = new EmailMessage(mail.OrganizationId, mail.RecipientId, mail.ToAddress, mail.Subject, mail.Body);
            await (mail.Channel == MailOutboxChannel.Webex ? webex.SendAsync(message, ct) : sender.SendAsync(message, ct));
            error = null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutting down: the lease runs out and the mail is tried again.
            return;
        }
        catch (MessageDeliveryException ex)
        {
            (error, transient, retryAfter) = (ex.Code, ex.Transient, ex.RetryAfter);
        }
        catch (Exception ex)
        {
            // An unexpected error is treated as transient; it ends as failed after MaxAttempts.
            (error, transient) = (ex.GetType().Name, true);
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ProjectHubDbContext>();
        var now = clock.GetUtcNow();
        var rows = db.Set<MailOutboxEntry>().Where(m => m.Id == mail.Id && m.Status == MailOutboxStatus.Pending);
        if (error is null)
        {
            await rows.ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, MailOutboxStatus.Sent)
                .SetProperty(m => m.SentAt, now)
                .SetProperty(m => m.LastError, (string?)null), CancellationToken.None);
            logger.LogInformation("{Channel} message {MailId} for user {RecipientId} sent", mail.Channel, mail.Id, mail.RecipientId);
            return;
        }

        error = error.Length <= MaxErrorLength ? error : error[..MaxErrorLength];
        if (transient && mail.Attempts < MaxAttempts)
        {
            var next = now + RetryDelay(mail.Attempts, retryAfter);
            await rows.ExecuteUpdateAsync(s => s.SetProperty(m => m.NextAttemptAt, next).SetProperty(m => m.LastError, error), CancellationToken.None);
            logger.LogWarning("{Channel} message {MailId} not sent ({Error}), attempt {Attempt}; retrying at {Next}", mail.Channel, mail.Id, error, mail.Attempts, next);
        }
        else
        {
            await rows.ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, MailOutboxStatus.Failed).SetProperty(m => m.LastError, error), CancellationToken.None);
            logger.LogError("{Channel} message {MailId} for user {RecipientId} failed permanently ({Error}) after {Attempts} attempts", mail.Channel, mail.Id, mail.RecipientId, error, mail.Attempts);
        }
    }
}

/// <summary>Runs the outbox in every instance: right after new mails, otherwise every poll interval.</summary>
internal sealed class MailOutboxWorker(
    MailOutbox outbox,
    MailOutboxSignal signal,
    MailOutboxOptions options,
    TimeProvider clock,
    ILogger<MailOutboxWorker> logger) : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.PollInterval <= TimeSpan.Zero)
        {
            return;
        }

        var nextCleanup = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (await outbox.SendDueAsync(stoppingToken) == MailOutbox.BatchSize)
                {
                }

                if (clock.GetUtcNow() >= nextCleanup)
                {
                    await outbox.CleanupAsync(stoppingToken);
                    nextCleanup = clock.GetUtcNow() + CleanupInterval;
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Mail outbox run failed");
            }

            try
            {
                await signal.WaitAsync(options.PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
