-- A colour per board column, chosen from a small palette (null: the default look).
alter table kanban_column
    add column color text
        constraint ck_kanban_column_color check (color in ('gray', 'blue', 'green', 'yellow', 'orange', 'red', 'purple', 'pink'));
