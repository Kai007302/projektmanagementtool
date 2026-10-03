-- Kanban is a view on tasks (AGENTS.md): every column stands for one task status,
-- so moving a card changes the task's status and a status change places the card.
alter table kanban_column
    add column task_status text not null default 'todo'
        constraint ck_kanban_column_task_status check (task_status in ('todo', 'in_progress', 'done'));

alter table kanban_column alter column task_status drop default;
