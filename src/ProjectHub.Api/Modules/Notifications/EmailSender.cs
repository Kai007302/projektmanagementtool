using System.Collections.Concurrent;

namespace ProjectHub.Api.Modules.Notifications;

public sealed record EmailMessage(Guid OrganizationId, Guid RecipientId, string To, string Subject, string Body);

public sealed record SentEmail(Guid Id, EmailMessage Message, DateTimeOffset SentAt);

/// <summary>Sends notification mails. Phase 8 replaces the fake with Microsoft Graph behind this interface.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}

/// <summary>
/// Sends nothing: keeps the most recent mails in memory (for development and tests) and logs that a mail
/// was "sent", without address or content.
/// </summary>
public sealed class FakeEmailSender(TimeProvider clock, ILogger<FakeEmailSender> logger) : IEmailSender
{
    public const int Capacity = 500;

    private readonly ConcurrentQueue<SentEmail> outbox = new();

    public IReadOnlyList<SentEmail> Outbox => outbox.ToArray();

    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        outbox.Enqueue(new SentEmail(Guid.CreateVersion7(), message, clock.GetUtcNow()));
        while (outbox.Count > Capacity && outbox.TryDequeue(out _))
        {
        }

        logger.LogInformation("Fake mail for user {RecipientId} kept in the development outbox", message.RecipientId);
        return Task.CompletedTask;
    }
}
