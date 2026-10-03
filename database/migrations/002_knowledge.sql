-- ProjectHub Knowledge Hub
-- Depends on 001_initial.sql

create table knowledge_space (
    id uuid primary key default uuidv7(),
    organization_id uuid not null references organization(id),
    name text not null,
    description text,
    owner_id uuid,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    version bigint not null default 1,
    constraint uq_knowledge_space_org_id unique (organization_id, id),
    constraint fk_knowledge_space_owner_org foreign key (organization_id, owner_id)
        references app_user(organization_id, id)
);
create index ix_knowledge_space_org on knowledge_space (organization_id);

create table knowledge_article (
    id uuid primary key default uuidv7(),
    organization_id uuid not null references organization(id),
    knowledge_space_id uuid,
    title text not null,
    slug text not null,
    article_type text not null check (article_type in ('article','how_to','best_practice','process','policy','faq','template','checklist','glossary')),
    summary text,
    owner_id uuid,
    status text not null default 'draft' check (status in ('draft','review','published','archived')),
    current_version_id uuid,
    published_at timestamptz,
    review_due_at date,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    deleted_at timestamptz,
    version bigint not null default 1,
    constraint uq_knowledge_article_org_id unique (organization_id, id),
    constraint fk_knowledge_article_space_org foreign key (organization_id, knowledge_space_id)
        references knowledge_space(organization_id, id),
    constraint fk_knowledge_article_owner_org foreign key (organization_id, owner_id)
        references app_user(organization_id, id)
);
create unique index uq_knowledge_article_slug on knowledge_article (organization_id, slug) where deleted_at is null;
create index ix_knowledge_article_org_status on knowledge_article (organization_id, status);
create index ix_knowledge_article_space on knowledge_article (knowledge_space_id);

create table knowledge_version (
    id uuid primary key default uuidv7(),
    organization_id uuid not null references organization(id),
    article_id uuid not null,
    version_number integer not null,
    content_json jsonb not null,
    summary text,
    created_by uuid not null,
    change_note text,
    created_at timestamptz not null default now(),
    constraint uq_knowledge_version_org_id unique (organization_id, id),
    constraint fk_knowledge_version_article_org foreign key (organization_id, article_id)
        references knowledge_article(organization_id, id) on delete cascade,
    constraint fk_knowledge_version_creator_org foreign key (organization_id, created_by)
        references app_user(organization_id, id),
    constraint uq_knowledge_version_number unique (article_id, version_number),
    constraint ck_knowledge_version_number check (version_number > 0)
);
create index ix_knowledge_version_article_created on knowledge_version (article_id, created_at desc);

alter table knowledge_article
    add constraint fk_knowledge_article_current_version_org
    foreign key (organization_id, current_version_id)
    references knowledge_version(organization_id, id);

create table knowledge_tag (
    id uuid primary key default uuidv7(),
    organization_id uuid not null references organization(id),
    name text not null,
    created_at timestamptz not null default now(),
    constraint uq_knowledge_tag_org_id unique (organization_id, id)
);
create unique index uq_knowledge_tag_name on knowledge_tag (organization_id, lower(name));

create table knowledge_article_tag (
    organization_id uuid not null references organization(id),
    article_id uuid not null,
    tag_id uuid not null,
    primary key (article_id, tag_id),
    constraint fk_knowledge_article_tag_article_org foreign key (organization_id, article_id)
        references knowledge_article(organization_id, id) on delete cascade,
    constraint fk_knowledge_article_tag_tag_org foreign key (organization_id, tag_id)
        references knowledge_tag(organization_id, id) on delete cascade
);

create table knowledge_relation (
    id uuid primary key default uuidv7(),
    organization_id uuid not null references organization(id),
    source_article_id uuid not null,
    target_article_id uuid not null,
    relation_type text not null check (relation_type in ('RELATED','REQUIRES','PART_OF','SUPERSEDES','REFERENCES')),
    created_by uuid not null,
    created_at timestamptz not null default now(),
    constraint uq_knowledge_relation_org_id unique (organization_id, id),
    constraint fk_knowledge_relation_source_org foreign key (organization_id, source_article_id)
        references knowledge_article(organization_id, id) on delete cascade,
    constraint fk_knowledge_relation_target_org foreign key (organization_id, target_article_id)
        references knowledge_article(organization_id, id) on delete cascade,
    constraint fk_knowledge_relation_creator_org foreign key (organization_id, created_by)
        references app_user(organization_id, id),
    constraint ck_knowledge_relation_not_self check (source_article_id <> target_article_id),
    constraint uq_knowledge_relation unique (source_article_id, target_article_id, relation_type)
);
create index ix_knowledge_relation_source on knowledge_relation (source_article_id);
create index ix_knowledge_relation_target on knowledge_relation (target_article_id);

create table knowledge_comment (
    id uuid primary key default uuidv7(),
    organization_id uuid not null references organization(id),
    article_id uuid not null,
    author_id uuid not null,
    content text not null,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    deleted_at timestamptz,
    version bigint not null default 1,
    constraint uq_knowledge_comment_org_id unique (organization_id, id),
    constraint fk_knowledge_comment_article_org foreign key (organization_id, article_id)
        references knowledge_article(organization_id, id) on delete cascade,
    constraint fk_knowledge_comment_author_org foreign key (organization_id, author_id)
        references app_user(organization_id, id)
);
create index ix_knowledge_comment_article_created on knowledge_comment (article_id, created_at);

create table knowledge_reference (
    id uuid primary key default uuidv7(),
    organization_id uuid not null references organization(id),
    article_id uuid not null,
    resource_type text not null check (resource_type in ('project','task','team','whiteboard')),
    resource_id uuid not null,
    created_by uuid not null,
    created_at timestamptz not null default now(),
    constraint uq_knowledge_reference_org_id unique (organization_id, id),
    constraint fk_knowledge_reference_article_org foreign key (organization_id, article_id)
        references knowledge_article(organization_id, id) on delete cascade,
    constraint fk_knowledge_reference_creator_org foreign key (organization_id, created_by)
        references app_user(organization_id, id),
    constraint uq_knowledge_reference unique (article_id, resource_type, resource_id)
);
create index ix_knowledge_reference_resource on knowledge_reference (organization_id, resource_type, resource_id);

create table knowledge_permission (
    id uuid primary key default uuidv7(),
    organization_id uuid not null references organization(id),
    article_id uuid not null,
    principal_type text not null check (principal_type in ('user','team')),
    principal_id uuid not null,
    permission text not null check (permission in ('view','edit','admin')),
    created_at timestamptz not null default now(),
    constraint uq_knowledge_permission_org_id unique (organization_id, id),
    constraint fk_knowledge_permission_article_org foreign key (organization_id, article_id)
        references knowledge_article(organization_id, id) on delete cascade,
    constraint uq_knowledge_permission unique (article_id, principal_type, principal_id)
);
create index ix_knowledge_permission_principal on knowledge_permission (organization_id, principal_type, principal_id);
