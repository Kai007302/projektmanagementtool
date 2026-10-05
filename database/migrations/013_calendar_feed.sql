-- Subscribable calendar (ADR 0018): one secret feed address per person. Calendar programs cannot sign in with
-- Entra ID, so the address itself is the credential; only its SHA-256 hash is stored.
create table calendar_feed (
    id uuid primary key default uuidv7(),
    organization_id uuid not null references organization(id),
    user_id uuid not null,
    token_hash bytea not null check (length(token_hash) = 32),
    created_at timestamptz not null default now(),
    last_used_at timestamptz,
    constraint uq_calendar_feed_user unique (user_id),
    constraint uq_calendar_feed_token unique (token_hash),
    constraint fk_calendar_feed_user foreign key (organization_id, user_id)
        references app_user(organization_id, id) on delete cascade
);
