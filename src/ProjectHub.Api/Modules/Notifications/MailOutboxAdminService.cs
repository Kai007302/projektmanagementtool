using Microsoft.EntityFrameworkCore;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Infrastructure.Outcomes;
using ProjectHub.Api.Modules.Audit;
using ProjectHub.Api.Modules.Identity;

namespace ProjectHub.Api.Modules.Notifications;

/// <summary>Mail outbox state for organization admins. Carries no addresses and no subjects.</summary>
public sealed record MailOutboxStatusResponse(
    int Pending, int Retrying, int Failed, int SentLast24Hours, DateTimeOffset? OldestPendingAt, IReadOnlyList<MailProblemResponse> Problems);

public sealed record MailProblemResponse(Guid Id, Guid RecipientId, string Channel, string Status, int Attempts, string? LastError, DateTimeOffset CreatedAt, DateTimeOffset NextAttemptAt);

public sealed class MailOutboxAdminService(ProjectHubDbContext db, IAuditLog audit, MailOutboxSignal signal, TimeProvider clock)
{
    public const int MaxProblems = 20;

    public async Task<ServiceResult<MailOutboxStatusResponse>> StatusAsync(UserContext user, CancellationToken ct)
    {
        if (!user.IsOrganizationAdmin)
        {
            return ServiceFailure.Forbidden("Only organization admins can see the mail outbox.");
        }

        var since = clock.GetUtcNow().AddDays(-1);
        var mails = db.Set<MailOutboxEntry>().Where(m => m.OrganizationId == user.OrganizationId);
        var counts = await mails
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Pending = g.Count(m => m.Status == MailOutboxStatus.Pending),
                Retrying = g.Count(m => m.Status == MailOutboxStatus.Pending && m.LastError != null),
                Failed = g.Count(m => m.Status == MailOutboxStatus.Failed),
                Sent = g.Count(m => m.Status == MailOutboxStatus.Sent && m.SentAt >= since),
                Oldest = g.Where(m => m.Status == MailOutboxStatus.Pending).Min(m => (DateTimeOffset?)m.CreatedAt),
            })
            .SingleOrDefaultAsync(ct);
        var problems = await mails
            .Where(m => m.Status == MailOutboxStatus.Failed || (m.Status == MailOutboxStatus.Pending && m.LastError != null))
            .OrderByDescending(m => m.CreatedAt)
            .Take(MaxProblems)
            .Select(m => new MailProblemResponse(m.Id, m.RecipientId, m.Channel, m.Status, m.Attempts, m.LastError, m.CreatedAt, m.NextAttemptAt))
            .ToListAsync(ct);

        return new MailOutboxStatusResponse(
            counts?.Pending ?? 0, counts?.Retrying ?? 0, counts?.Failed ?? 0, counts?.Sent ?? 0, counts?.Oldest, problems);
    }

    /// <summary>Puts a failed mail back in line, for example after the IT fixed the mailbox permissions.</summary>
    public async Task<ServiceResult<Done>> RetryAsync(UserContext user, Guid mailId, CancellationToken ct)
    {
        if (!user.IsOrganizationAdmin)
        {
            return ServiceFailure.Forbidden("Only organization admins can retry mails.");
        }

        var status = await db.Set<MailOutboxEntry>()
            .Where(m => m.Id == mailId && m.OrganizationId == user.OrganizationId)
            .Select(m => m.Status)
            .SingleOrDefaultAsync(ct);
        if (status is null)
        {
            return ServiceFailure.NotFound("Mail");
        }

        if (status != MailOutboxStatus.Failed)
        {
            return ServiceFailure.Conflict("Only failed mails can be retried.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = clock.GetUtcNow();
        var updated = await db.Set<MailOutboxEntry>()
            .Where(m => m.Id == mailId && m.Status == MailOutboxStatus.Failed)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, MailOutboxStatus.Pending)
                .SetProperty(m => m.Attempts, 0)
                .SetProperty(m => m.NextAttemptAt, now), ct);
        if (updated == 0)
        {
            return ServiceFailure.Conflict("Only failed mails can be retried.");
        }

        audit.Record(user, AuditActions.MailRetried, "mail_outbox", mailId);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        signal.Notify();
        return Done.Value;
    }
}
