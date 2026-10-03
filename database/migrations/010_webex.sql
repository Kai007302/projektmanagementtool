-- Phase 9 (ADR 0011): Webex meetings and spaces linked to projects, Webex as a notification channel,
-- and the record of received webhooks for deduplication.

create table project_webex_link (
    id uuid primary key default uuidv7(),
    organization_id uuid not null references organization(id),
    project_id uuid not null,
    kind text not null check (kind in ('meeting', 'space')),
    title text not null check (length(title) between 1 and 200),
    url text not null check (length(url) <= 2000),
    -- Only for spaces the bot created; webhooks about the room find the link through it.
    room_id text,
    status text not null default 'active' check (status in ('active', 'disconnected')),
    created_by uuid not null,
    created_at timestamptz not null default now(),
    constraint fk_project_webex_link_project foreign key (organization_id, project_id)
        references project(organization_id, id) on delete cascade,
    constraint fk_project_webex_link_user foreign key (organization_id, created_by)
        references app_user(organization_id, id)
);

create index ix_project_webex_link_project on project_webex_link (organization_id, project_id);
create index ix_project_webex_link_room on project_webex_link (room_id) where room_id is not null;
-- At most one active space created by the bot per project.
create unique index uq_project_webex_space on project_webex_link (project_id) where room_id is not null and status = 'active';

-- The outbox from ADR 0010 now carries notifications for mail and Webex.
alter table mail_outbox add column channel text not null default 'email' check (channel in ('email', 'webex'));

-- Webhook deliveries already handled. Webex sends no event id; event_key is resource:event:data.id.
create table webhook_event (
    id uuid primary key default uuidv7(),
    provider text not null check (provider in ('microsoft', 'webex')),
    event_key text not null,
    resource text not null,
    event text not null,
    correlation_id text,
    received_at timestamptz not null default now(),
    constraint uq_webhook_event unique (provider, event_key)
);
create index ix_webhook_event_received on webhook_event (received_at);
