-- Tasks with several assignees and a calculated progress (ADR 0022).

-- Everyone a task is assigned to. Replaces task.assignee_id; the single assignee of each task moves over.
create table task_assignee (
    organization_id uuid not null references organization(id),
    task_id uuid not null,
    user_id uuid not null,
    created_at timestamptz not null default now(),
    primary key (task_id, user_id),
    constraint fk_task_assignee_task_org foreign key (organization_id, task_id)
        references task(organization_id, id) on delete cascade,
    constraint fk_task_assignee_user_org foreign key (organization_id, user_id)
        references app_user(organization_id, id) on delete cascade
);
create index ix_task_assignee_user on task_assignee (organization_id, user_id);

insert into task_assignee (organization_id, task_id, user_id, created_at)
select organization_id, id, assignee_id, updated_at from task where assignee_id is not null;

drop index if exists ix_task_assignee;
alter table task drop constraint fk_task_assignee_org;
alter table task drop column assignee_id;

-- task.progress is calculated from now on: a task without subtasks by its status (open 0, in progress 50, done 100),
-- a task with subtasks is done at 100 or else the rounded mean of its subtasks. Deleted tasks do not count.
update task set progress = case status when 'done' then 100 when 'in_progress' then 50 else 0 end where deleted_at is null;

do $$
declare
    changed integer;
    rounds integer := 0;
begin
    -- One level of the hierarchy per round, from the deepest subtasks up; 100 rounds is far deeper than any plan.
    loop
        update task parent
        set progress = calculated.progress
        from (
            select child.parent_task_id as id, round(avg(child.progress))::smallint as progress
            from task child
            where child.parent_task_id is not null and child.deleted_at is null
            group by child.parent_task_id
        ) calculated
        where parent.id = calculated.id
          and parent.deleted_at is null
          and parent.status <> 'done'
          and parent.progress <> calculated.progress;
        get diagnostics changed = row_count;
        rounds := rounds + 1;
        exit when changed = 0 or rounds >= 100;
    end loop;
end $$;
