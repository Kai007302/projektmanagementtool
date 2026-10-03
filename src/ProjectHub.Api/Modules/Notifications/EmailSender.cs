using System.Collections.Concurrent;

namespace ProjectHub.Api.Modules.Notifications;

public sealed record EmailMessage(Guid OrganizationId, Guid RecipientId, string To, string Subject, string Body);

public sealed record SentEmail(Guid Id, EmailMessage Message, DateTimeOffset SentAt);

/// <summary>
/// Hands a notification mail to the mail system (port, ADR 0010). Only the outbox worker calls this; modules write
/// to the outbox. Throws <see cref="EmailDeliveryException"/> when the mail was not accepted.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}

/// <summary>
/// The mail system did not accept a mail. <paramref name="Code"/> names the cause without addresses or content
/// (for example "429 ApplicationThrottled"); transient failures are retried, permanent ones are not.
/// </summary>
public sealed class EmailDeliveryException(string code, bool transient, TimeSpan? retryAfter = null, Exception? inner = null)
    : Exception($"Mail delivery failed: {code}", inner)
{
    public string Code { get; } = code;

    public bool Transient { get; } = transient;

    public TimeSpan? RetryAfter { get; } = retryAfter;
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
