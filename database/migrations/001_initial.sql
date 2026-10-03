-- ProjectHub initial relational schema
-- Target: PostgreSQL 18
-- UUIDv7 is provided natively by PostgreSQL 18.

create table organization (
    id uuid primary key default uuidv7(),
    name text not null,
    slug text not null,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    version bigint not null default 1,
    constraint uq_organization_slug unique (slug)
);

create table app_user (
    id uuid primary key default uuidv7(),
    organization_id uuid not null references organization(id),
    entra_object_id text not null,
    email text not null,
    display_name text not null,
    department text,
    status text not null default 'active' check (status in ('active','inactive','invited')),
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    last_login_at timestamptz,
    version bigint not null default 1,
    constraint uq_user_org_entra unique (organization_id, entra_object_id),
    constraint uq_user_org_id unique (organization_id, id)
);
create unique index uq_user_org_email on app_user (organization_id, lower(email));
create index ix_user_org on app_user (organization_id);

create table team (
    id uuid primary key default uuidv7(),
    organization_id uuid not null references organization(id),
    name text not null,
    description text,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    version bigint not null default 1,
    constraint uq_team_org_id unique (organization_id, id)
);
create unique index uq_team_org_name on team (organization_id, lower(name));

create table team_member (
    organization_id uuid not null references organization(id),
    team_id uuid not null,
    user_id uuid not null,
    role text not null default 'member' check (role in ('member','owner')),
    created_at timestamptz not null default now(),
    primary key (team_id, user_id),
    constraint fk_team_member_team_org foreign key (organization_id, team_id)
        references team(organization_id, id) on delete cascade,
    constraint fk_team_member_user_org foreign key (organization_id, user_id)
        references app_user(organization_id, id) on delete cascade
);
create index ix_team_member_user on team_member (organization_id, user_id);

create table project (
    id uuid primary key default uuidv7(),
    organization_id uuid not null references organization(id),
    name text not null,
    description text,
    status text not null default 'active' check (status in ('planned','active','on_hold','completed','archived')),
    owner_id uuid not null,
    start_date date,
    end_date date,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    version bigint not null default 1,
    constraint uq_project_org_id unique (organization_id, id),
    constraint fk_project_owner_org foreign key (organization_id, owner_id)
        references app_user(organization_id, id),
    constraint ck_project_dates check (end_date is null or start_date is null or end_date >= start_date)
);
create index ix_project_org on project (organization_id);

create table project_member (
    organization_id uuid not null references organization(id),
    project_id uuid not null,
    user_id uuid not null,
    role text not null check (role in ('admin','editor','member','viewer','guest')),
    created_at timestamptz not null default now(),
    primary key (project_id, user_id),
    constraint fk_project_member_project_org foreign key (organization_id, project_id)
        references project(organization_id, id) on delete cascade,
    constraint fk_project_member_user_org foreign key (organization_id, user_id)
        references app_user(organization_id, id) on delete cascade
);
create index ix_project_member_user on project_member (organization_id, user_id);

create table kanban_board (
    id uuid primary key default uuidv7(),
    organization_id uuid not null references organization(id),
    project_id uuid not null,
    name text not null,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    version bigint not null default 1,
    constraint uq_kanban_board_project unique (project_id),
    constraint uq_kanban_board_org_id unique (organization_id, id),
    constraint fk_kanban_board_project_org foreign key (organization_id, project_id)
        references project(organization_id, id) on delete cascade
);

create table kanban_column (
    id uuid primary key default uuidv7(),
    organization_id uuid not null references organization(id),
    board_id uuid not null,
    name text not null,
    position numeric(20,10) not null,
    wip_limit integer,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    version bigint not null default 1,
    constraint uq_kanban_column_org_id unique (organization_id, id),
    constraint fk_kanban_column_board_org foreign key (organization_id, board_id)
        references kanban_board(organization_id, id) on delete cascade,
    constraint ck_wip_limit check (wip_limit is null or wip_limit > 0)
);
create index ix_kanban_column_board_position on kanban_column (board_id, position);

create table task (
    id uuid primary key default uuidv7(),
    organization_id uuid not null references organization(id),
    project_id uuid not null,
    parent_task_id uuid,
    kanban_column_id uuid,
    title text not null,
    description text,
    status text not null default 'todo',
    priority text not null default 'normal' check (priority in ('low','normal','high','urgent')),
    assignee_id uuid,
    creator_id uuid not null,
    start_date date,
    due_date date,
    progress smallint not null default 0 check (progress between 0 and 100),
    estimated_hours numeric(10,2),
    board_position numeric(20,10),
    deleted_at timestamptz,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    version bigint not null default 1,
    constraint uq_task_org_id unique (organization_id, id),
    constraint fk_task_project_org foreign key (organization_id, project_id)
        references project(organization_id, id) on delete cascade,
    constraint fk_task_parent_org foreign key (organization_id, parent_task_id)
        references task(organization_id, id),
    constraint fk_task_column_org foreign key (organization_id, kanban_column_id)
        references kanban_column(organization_id, id),
    constraint fk_task_assignee_org foreign key (organization_id, assignee_id)
        references app_user(organization_id, id),
    constraint fk_task_creator_org foreign key (organization_id, creator_id)
        references app_user(organization_id, id),
    constraint ck_task_dates check (due_date is null or start_date is null or due_date >= start_date),
    constraint ck_task_estimated_hours check (estimated_hours is null or estimated_hours >= 0)
);
create index ix_task_project on task (project_id);
create index ix_task_org_project on task (organization_id, project_id);
create index ix_task_assignee on task (organization_id, assignee_id);
create index ix_task_due_date on task (organization_id, due_date);
create index ix_task_project_status on task (project_id, status);
create index ix_task_parent on task (organization_id, parent_task_id);
create index ix_task_board_order on task (kanban_column_id, board_position);

create table task_comment (
    id uuid primary key default uuidv7(),
    organization_id uuid not null references organization(id),
    task_id uuid not null,
    author_id uuid not null,
    content text not null,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    deleted_at timestamptz,
    version bigint not null default 1,
    constraint uq_task_comment_org_id unique (organization_id, id),
    constraint fk_comment_task_org foreign key (organization_id, task_id)
        references task(organization_id, id) on delete cascade,
    constraint fk_comment_author_org foreign key (organization_id, author_id)
        references app_user(organization_id, id)
);
create index ix_task_comment_task_created on task_comment (task_id, created_at);

create table task_attachment (
    id uuid primary key default uuidv7(),
    organization_id uuid not null references organization(id),
    task_id uuid not null,
    file_name text not null,
    content_type text not null,
    size_bytes bigint not null,
    storage_key text not null,
    uploaded_by uuid not null,
    created_at timestamptz not null default now(),
    constraint uq_task_attachment_org_id unique (organization_id, id),
    constraint fk_attachment_task_org foreign key (organization_id, task_id)
        references task(organization_id, id) on delete cascade,
    constraint fk_attachment_uploader_org foreign key (organization_id, uploaded_by)
        references app_user(organization_id, id),
    constraint ck_attachment_size check (size_bytes >= 0)
);
create index ix_attachment_task on task_attachment (task_id);

create table task_dependency (
    id uuid primary key default uuidv7(),
    organization_id uuid not null references organization(id),
    source_task_id uuid not null,
    target_task_id uuid not null,
    dependency_type text not null check (dependency_type in ('finish_to_start','start_to_start','finish_to_finish','start_to_finish')),
    created_at timestamptz not null default now(),
    constraint uq_task_dependency_org_id unique (organization_id, id),
    constraint fk_dependency_source_org foreign key (organization_id, source_task_id)
        references task(organization_id, id) on delete cascade,
    constraint fk_dependency_target_org foreign key (organization_id, target_task_id)
        references task(organization_id, id) on delete cascade,
    constraint ck_no_self_dependency check (source_task_id <> target_task_id),
    constraint uq_task_dependency unique (source_task_id, target_task_id, dependency_type)
);
create index ix_task_dependency_target on task_dependency (target_task_id);

create table gantt_milestone (
    id uuid primary key default uuidv7(),
    organization_id uuid not null references organization(id),
    project_id uuid not null,
    name text not null,
    milestone_date date not null,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    version bigint not null default 1,
    constraint uq_gantt_milestone_org_id unique (organization_id, id),
    constraint fk_milestone_project_org foreign key (organization_id, project_id)
        references project(organization_id, id) on delete cascade
);
create index ix_milestone_project_date on gantt_milestone (project_id, milestone_date);

create table whiteboard (
    id uuid primary key default uuidv7(),
    organization_id uuid not null references organization(id),
    project_id uuid not null,
    name text not null,
    collaboration_document_id text not null unique,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    version bigint not null default 1,
    constraint uq_whiteboard_org_id unique (organization_id, id),
    constraint fk_whiteboard_project_org foreign key (organization_id, project_id)
        references project(organization_id, id) on delete cascade
);

create table whiteboard_snapshot (
    id uuid primary key default uuidv7(),
    organization_id uuid not null references organization(id),
    whiteboard_id uuid not null,
    storage_key text not null,
    sequence_number bigint not null,
    created_at timestamptz not null default now(),
    constraint uq_whiteboard_snapshot_org_id unique (organization_id, id),
    constraint fk_whiteboard_snapshot_org foreign key (organization_id, whiteboard_id)
        references whiteboard(organization_id, id) on delete cascade,
    constraint uq_whiteboard_snapshot_seq unique (whiteboard_id, sequence_number)
);

create table whiteboard_reference (
    id uuid primary key default uuidv7(),
    organization_id uuid not null references organization(id),
    whiteboard_id uuid not null,
    task_id uuid not null,
    object_id text not null,
    created_at timestamptz not null default now(),
    constraint uq_whiteboard_reference_org_id unique (organization_id, id),
    constraint fk_reference_whiteboard_org foreign key (organization_id, whiteboard_id)
        references whiteboard(organization_id, id) on delete cascade,
    constraint fk_reference_task_org foreign key (organization_id, task_id)
        references task(organization_id, id) on delete cascade,
    constraint uq_whiteboard_task_object unique (whiteboard_id, object_id)
);

create table notification (
    id uuid primary key default uuidv7(),
    organization_id uuid not null references organization(id),
    user_id uuid not null,
    type text not null,
    title text not null,
    body text,
    resource_type text,
    resource_id uuid,
    read_at timestamptz,
    created_at timestamptz not null default now(),
    constraint uq_notification_org_id unique (organization_id, id),
    constraint fk_notification_user_org foreign key (organization_id, user_id)
        references app_user(organization_id, id) on delete cascade
);
create index ix_notification_user_created on notification (user_id, created_at desc);

create table notification_preference (
    organization_id uuid not null references organization(id),
    user_id uuid not null,
    in_app_enabled boolean not null default true,
    email_enabled boolean not null default true,
    webex_enabled boolean not null default false,
    updated_at timestamptz not null default now(),
    primary key (organization_id, user_id),
    constraint fk_notification_preference_user_org foreign key (organization_id, user_id)
        references app_user(organization_id, id) on delete cascade
);

create table activity_log (
    id uuid primary key default uuidv7(),
    organization_id uuid not null references organization(id),
    project_id uuid,
    actor_id uuid,
    resource_type text not null,
    resource_id uuid,
    action text not null,
    metadata jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    constraint fk_activity_project_org foreign key (organization_id, project_id)
        references project(organization_id, id) on delete cascade,
    constraint fk_activity_actor_org foreign key (organization_id, actor_id)
        references app_user(organization_id, id)
);
create index ix_activity_project_created on activity_log (project_id, created_at desc);
create index ix_activity_org_created on activity_log (organization_id, created_at desc);

create table audit_log (
    id uuid primary key default uuidv7(),
    organization_id uuid not null references organization(id),
    actor_id uuid,
    action text not null,
    resource_type text,
    resource_id uuid,
    metadata jsonb not null default '{}'::jsonb,
    correlation_id text,
    created_at timestamptz not null default now(),
    constraint fk_audit_actor_org foreign key (organization_id, actor_id)
        references app_user(organization_id, id)
);
create index ix_audit_org_created on audit_log (organization_id, created_at desc);
create index ix_audit_actor_created on audit_log (actor_id, created_at desc);

create table integration (
    id uuid primary key default uuidv7(),
    organization_id uuid not null references organization(id),
    provider text not null check (provider in ('microsoft','webex')),
    status text not null default 'active' check (status in ('active','disabled','error')),
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    version bigint not null default 1,
    constraint uq_integration_org_id unique (organization_id, id)
);
create unique index uq_integration_org_provider on integration (organization_id, provider);

create table integration_account (
    id uuid primary key default uuidv7(),
    organization_id uuid not null references organization(id),
    integration_id uuid not null,
    external_account_id text not null,
    display_name text,
    encrypted_credential_ref text not null,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint uq_integration_account_org_id unique (organization_id, id),
    constraint fk_integration_account_org foreign key (organization_id, integration_id)
        references integration(organization_id, id) on delete cascade,
    constraint uq_integration_external_account unique (integration_id, external_account_id)
);

create table webhook_subscription (
    id uuid primary key default uuidv7(),
    organization_id uuid not null references organization(id),
    integration_id uuid not null,
    external_subscription_id text not null,
    resource text not null,
    active boolean not null default true,
    last_event_at timestamptz,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint uq_webhook_subscription_org_id unique (organization_id, id),
    constraint fk_webhook_integration_org foreign key (organization_id, integration_id)
        references integration(organization_id, id) on delete cascade,
    constraint uq_webhook_external unique (integration_id, external_subscription_id)
);
