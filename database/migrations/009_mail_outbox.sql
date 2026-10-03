-- Phase 8 (ADR 0010): notification mails wait here until the transport (Microsoft Graph) has accepted them.
-- Rows are written in the same transaction as the in-app notifications and deleted after a while
-- (sent: 7 days, failed: 30 days), so addresses and subjects are not kept longer than needed.
create table mail_outbox (
    id uuid primary key default uuidv7(),
    organization_id uuid not null references organization(id),
    recipient_id uuid not null,
    to_address text not null,
    subject text not null,
    body text not null,
    status text not null default 'pending' check (status in ('pending', 'sent', 'failed')),
    attempts integer not null default 0,
    -- When the row is due next. While a worker sends it, this is the end of its lease.
    next_attempt_at timestamptz not null default now(),
    -- HTTP status and Graph error code only, never addresses or content.
    last_error text,
    created_at timestamptz not null default now(),
    sent_at timestamptz,
    constraint fk_mail_outbox_user foreign key (organization_id, recipient_id)
        references app_user(organization_id, id) on delete cascade
);

create index ix_mail_outbox_due on mail_outbox (next_attempt_at) where status = 'pending';
create index ix_mail_outbox_finished on mail_outbox (organization_id, status, created_at) where status <> 'pending';
