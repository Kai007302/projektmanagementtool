using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ProjectHub.Api.Modules.Notifications;
using static ProjectHub.Api.Modules.Identity.Development.DevelopmentSeedData;

namespace ProjectHub.Api.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class MailOutboxTests(InfrastructureFixture infrastructure) : ApiTestBase(infrastructure)
{
    private readonly ScriptedEmailSender sender = new();

    protected override ProjectHubApiFactory CreateFactory() =>
        new(Infrastructure.Postgres.GetConnectionString(), Infrastructure.Redis.GetConnectionString(), configure: builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IEmailSender>(sender)));

    [Fact]
    public async Task Mails_are_stored_with_the_notification_and_sent_afterwards()
    {
        var title = Unique("Zuweisung");
        var project = await CreateTeamProjectAsync();

        await CreateTaskAsync(Ben, project.Id, NewTask(title, assigneeId: David.Id));

        Assert.Equal(1, await ScalarAsync("select count(*) from mail_outbox where subject like $1 and status = 'pending' and recipient_id = $2", $"%{title}%", David.Id));
        Assert.Empty(sender.SentWith(title));

        await DeliverMailsAsync();

        var mail = Assert.Single(sender.SentWith(title));
        Assert.Equal((David.Id, David.Email), (mail.RecipientId, mail.To));
        Assert.Equal(1, await ScalarAsync("select count(*) from mail_outbox where subject like $1 and status = 'sent' and sent_at is not null and attempts = 1", $"%{title}%"));
        await DeliverMailsAsync();
        Assert.Single(sender.SentWith(title));
    }

    [Fact]
    public async Task Transient_failures_are_retried_later()
    {
        var title = Unique("Vorübergehend");
        sender.Fail(title, new MessageDeliveryException("503", transient: true));
        var project = await CreateTeamProjectAsync();
        await CreateTaskAsync(Ben, project.Id, NewTask(title, assigneeId: David.Id));

        await DeliverMailsAsync();

        Assert.Equal(1, await ScalarAsync(
            "select count(*) from mail_outbox where subject like $1 and status = 'pending' and attempts = 1 and last_error = '503' and next_attempt_at > now() + interval '20 seconds'",
            $"%{title}%"));
        await DeliverMailsAsync();
        Assert.Empty(sender.SentWith(title));

        sender.Succeed(title);
        await MakeDueAsync(title);
        await DeliverMailsAsync();

        Assert.Single(sender.SentWith(title));
        Assert.Equal(1, await ScalarAsync("select count(*) from mail_outbox where subject like $1 and status = 'sent' and attempts = 2 and last_error is null", $"%{title}%"));
    }

    [Fact]
    public async Task Permanent_failures_stop_and_admins_can_retry()
    {
        var title = Unique("Dauerhaft");
        sender.Fail(title, new MessageDeliveryException("403 ErrorAccessDenied", transient: false));
        var project = await CreateTeamProjectAsync();
        await CreateTaskAsync(Ben, project.Id, NewTask(title, assigneeId: David.Id));

        await DeliverMailsAsync();

        var id = await IdOfAsync(title);
        Assert.Equal(1, await ScalarAsync("select count(*) from mail_outbox where id = $1 and status = 'failed' and attempts = 1 and last_error = '403 ErrorAccessDenied'", id));
        var status = (await As(Ada).GetFromJsonAsync<MailOutboxStatusResponse>("/api/v1/admin/mail-outbox"))!;
        Assert.Contains(status.Problems, p => p.Id == id && p.Status == MailOutboxStatus.Failed && p.LastError == "403 ErrorAccessDenied" && p.RecipientId == David.Id);
        Assert.True(status.Failed >= 1);

        Assert.Equal(HttpStatusCode.Forbidden, (await As(Ben).PostAsync($"/api/v1/admin/mail-outbox/{id}/retry", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await As(Fritz).PostAsync($"/api/v1/admin/mail-outbox/{id}/retry", null)).StatusCode);
        sender.Succeed(title);
        Assert.Equal(HttpStatusCode.NoContent, (await As(Ada).PostAsync($"/api/v1/admin/mail-outbox/{id}/retry", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await As(Ada).PostAsync($"/api/v1/admin/mail-outbox/{id}/retry", null)).StatusCode);
        Assert.Equal(1, await ScalarAsync("select count(*) from audit_log where action = 'MailRetried' and resource_id = $1 and actor_id = $2", id, Ada.Id));

        await DeliverMailsAsync();

        Assert.Single(sender.SentWith(title));
        Assert.Equal(1, await ScalarAsync("select count(*) from mail_outbox where id = $1 and status = 'sent' and attempts = 1", id));
    }

    [Fact]
    public async Task Transient_failures_end_after_the_last_attempt()
    {
        var title = Unique("Letzter Versuch");
        sender.Fail(title, new MessageDeliveryException("network", transient: true));
        var project = await CreateTeamProjectAsync();
        await CreateTaskAsync(Ben, project.Id, NewTask(title, assigneeId: David.Id));
        await ScalarAsync("update mail_outbox set attempts = $2 where subject like $1", $"%{title}%", MailOutbox.MaxAttempts - 1);

        await DeliverMailsAsync();

        Assert.Equal(1, await ScalarAsync("select count(*) from mail_outbox where subject like $1 and status = 'failed' and attempts = $2", $"%{title}%", MailOutbox.MaxAttempts));
    }

    [Fact]
    public async Task A_reserved_mail_waits_for_its_lease_and_is_sent_again_after_a_crash()
    {
        var title = Unique("Absturz");
        var project = await CreateTeamProjectAsync();
        await CreateTaskAsync(Ben, project.Id, NewTask(title, assigneeId: David.Id));

        // Another instance reserved the mail and died while sending.
        await ScalarAsync("update mail_outbox set attempts = 1, next_attempt_at = now() + interval '2 minutes' where subject like $1", $"%{title}%");
        await DeliverMailsAsync();
        Assert.Empty(sender.SentWith(title));

        await MakeDueAsync(title);
        await DeliverMailsAsync();
        Assert.Single(sender.SentWith(title));
        Assert.Equal(1, await ScalarAsync("select count(*) from mail_outbox where subject like $1 and status = 'sent' and attempts = 2", $"%{title}%"));
    }

    [Fact]
    public async Task A_lease_that_ran_out_on_the_last_attempt_marks_the_mail_failed()
    {
        var title = Unique("Abgelaufen");
        var project = await CreateTeamProjectAsync();
        await CreateTaskAsync(Ben, project.Id, NewTask(title, assigneeId: David.Id));
        await ScalarAsync("update mail_outbox set attempts = $2, next_attempt_at = now() - interval '1 second' where subject like $1", $"%{title}%", MailOutbox.MaxAttempts);

        await DeliverMailsAsync();

        Assert.Empty(sender.SentWith(title));
        Assert.Equal(1, await ScalarAsync("select count(*) from mail_outbox where subject like $1 and status = 'failed' and last_error = 'lease expired'", $"%{title}%"));
    }

    [Fact]
    public async Task Parallel_workers_send_every_mail_once()
    {
        var title = Unique("Parallel");
        for (var i = 0; i < 45; i++)
        {
            await ScalarAsync(
                "insert into mail_outbox (organization_id, recipient_id, to_address, subject, body) values ($1, $2, $3, $4, 'x')",
                Contoso.Id, David.Id, David.Email, $"{title} {i}");
        }

        sender.Delay = TimeSpan.FromMilliseconds(5);
        var outbox = Factory.Services.GetRequiredService<MailOutbox>();
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            while (await outbox.SendDueAsync(CancellationToken.None) > 0)
            {
            }
        })));

        var sent = sender.SentWith(title);
        Assert.Equal(45, sent.Count);
        Assert.Equal(45, sent.Select(m => m.Subject).Distinct().Count());
        Assert.Equal(45, await ScalarAsync("select count(*) from mail_outbox where subject like $1 and status = 'sent' and attempts = 1", $"%{title}%"));
    }

    [Fact]
    public async Task Old_mails_are_cleaned_up()
    {
        var title = Unique("Alt");
        async Task InsertAsync(string suffix, string status, string age) =>
            await ScalarAsync(
                $"insert into mail_outbox (organization_id, recipient_id, to_address, subject, body, status, created_at, sent_at) values ($1, $2, $3, $4, 'x', '{status}', now() - interval '{age}', case when '{status}' = 'sent' then now() - interval '{age}' end)",
                Contoso.Id, David.Id, David.Email, $"{title} {suffix}");
        await InsertAsync("sent-old", MailOutboxStatus.Sent, "8 days");
        await InsertAsync("sent-new", MailOutboxStatus.Sent, "1 day");
        await InsertAsync("failed-old", MailOutboxStatus.Failed, "31 days");
        await InsertAsync("failed-new", MailOutboxStatus.Failed, "8 days");

        await Factory.Services.GetRequiredService<MailOutbox>().CleanupAsync(CancellationToken.None);

        Assert.Equal(0, await ScalarAsync("select count(*) from mail_outbox where subject like $1 and subject like '%-old'", $"%{title}%"));
        Assert.Equal(2, await ScalarAsync("select count(*) from mail_outbox where subject like $1 and subject like '%-new'", $"%{title}%"));
    }

    [Fact]
    public async Task Only_organization_admins_see_the_outbox_and_never_addresses_or_subjects()
    {
        var title = Unique("Geheim");
        sender.Fail(title, new MessageDeliveryException("404 ErrorInvalidUser", transient: false));
        var project = await CreateTeamProjectAsync();
        await CreateTaskAsync(Ben, project.Id, NewTask(title, assigneeId: David.Id));
        await DeliverMailsAsync();

        var response = await As(Ada).GetAsync("/api/v1/admin/mail-outbox");
        var text = await response.Content.ReadAsStringAsync();
        var fabrikam = (await As(Fritz).GetFromJsonAsync<MailOutboxStatusResponse>("/api/v1/admin/mail-outbox"))!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(title, text, StringComparison.Ordinal);
        Assert.DoesNotContain("@", text, StringComparison.Ordinal);
        Assert.DoesNotContain(fabrikam.Problems, p => p.RecipientId == David.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await As(Ben).GetAsync("/api/v1/admin/mail-outbox")).StatusCode);
    }

    private static string Unique(string prefix) => $"{prefix} {Guid.NewGuid():N}";

    private Task<long> MakeDueAsync(string title) =>
        ScalarAsync("update mail_outbox set next_attempt_at = now() - interval '1 second' where subject like $1", $"%{title}%");

    private async Task<Guid> IdOfAsync(string title)
    {
        await using var dataSource = Npgsql.NpgsqlDataSource.Create(Infrastructure.Postgres.GetConnectionString());
        await using var command = dataSource.CreateCommand("select id from mail_outbox where subject like $1");
        command.Parameters.Add(new Npgsql.NpgsqlParameter { Value = $"%{title}%" });
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>Records mails and fails those whose subject contains a marker.</summary>
    private sealed class ScriptedEmailSender : IEmailSender
    {
        private readonly ConcurrentBag<EmailMessage> sent = [];
        private readonly ConcurrentDictionary<string, Exception> failures = new();

        public TimeSpan Delay { get; set; }

        public void Fail(string marker, Exception error) => failures[marker] = error;

        public void Succeed(string marker) => failures.TryRemove(marker, out _);

        public IReadOnlyList<EmailMessage> SentWith(string marker) =>
            sent.Where(m => m.Subject.Contains(marker, StringComparison.Ordinal)).ToList();

        public async Task SendAsync(EmailMessage message, CancellationToken ct)
        {
            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, ct);
            }

            if (failures.FirstOrDefault(f => message.Subject.Contains(f.Key, StringComparison.Ordinal)).Value is { } error)
            {
                throw error;
            }

            sent.Add(message);
        }
    }
}
