-- Phase 7 (ADR 0009): Yjs updates of a whiteboard since its latest snapshot, in the order the server accepted them.
-- Compaction merges them into a snapshot (whiteboard_snapshot.sequence_number = last included update) and deletes them.
create table whiteboard_update (
    whiteboard_id uuid not null,
    sequence_number bigint not null,
    organization_id uuid not null references organization(id),
    payload bytea not null,
    created_by uuid not null,
    created_at timestamptz not null default now(),
    primary key (whiteboard_id, sequence_number),
    constraint fk_whiteboard_update_user foreign key (organization_id, created_by)
        references app_user(organization_id, id),
    constraint fk_whiteboard_update_org foreign key (organization_id, whiteboard_id)
        references whiteboard(organization_id, id) on delete cascade
);

create index ix_whiteboard_project on whiteboard (organization_id, project_id);
create index ix_whiteboard_reference_task on whiteboard_reference (task_id);
