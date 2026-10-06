-- Project symbol and logo (ADR 0020). The symbol is a short emoji; the logo a small image kept in its own table, so
-- project lists never load image bytes. logo_version changes with every new logo and lets browsers cache it.
alter table project add column icon text check (icon is null or char_length(icon) between 1 and 16);
alter table project add column logo_version bigint;

create table project_logo (
    project_id uuid primary key,
    organization_id uuid not null,
    content bytea not null check (length(content) between 1 and 262144),
    content_type text not null check (content_type in ('image/png', 'image/jpeg', 'image/webp')),
    updated_at timestamptz not null default now(),
    constraint fk_project_logo_project foreign key (organization_id, project_id)
        references project(organization_id, id) on delete cascade
);
