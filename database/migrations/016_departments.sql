-- Departments (ADR 0021, DEC-011): every department works in ProjectHub on its own, with its own lead, projects,
-- knowledge spaces and articles. Departments replace teams; existing teams become departments with the same id,
-- so knowledge grants and references to a team keep working for the department.

create table department (
    id uuid primary key default uuidv7(),
    organization_id uuid not null references organization(id),
    name text not null,
    description text,
    -- Object id of an Entra ID security group whose members join the department at sign-in (ADR 0021).
    entra_group_id text,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    version bigint not null default 1,
    constraint uq_department_org_id unique (organization_id, id),
    constraint ck_department_entra_group check (entra_group_id is null or char_length(entra_group_id) between 1 and 64)
);
create unique index uq_department_org_name on department (organization_id, lower(name));
create unique index uq_department_org_entra_group on department (organization_id, lower(entra_group_id)) where entra_group_id is not null;

-- 'lead' manages the department and everything in it, 'member' works in it, 'guest' only sees the projects
-- they are invited to. 'source' says whether the membership comes from an Entra group ('entra') or was added
-- by hand ('manual'); the sign-in sync only ever removes 'entra' memberships.
create table department_member (
    organization_id uuid not null references organization(id),
    department_id uuid not null,
    user_id uuid not null,
    role text not null default 'member' check (role in ('lead','member','guest')),
    source text not null default 'manual' check (source in ('manual','entra')),
    created_at timestamptz not null default now(),
    primary key (department_id, user_id),
    constraint fk_department_member_department_org foreign key (organization_id, department_id)
        references department(organization_id, id) on delete cascade,
    constraint fk_department_member_user_org foreign key (organization_id, user_id)
        references app_user(organization_id, id) on delete cascade
);
create index ix_department_member_user on department_member (organization_id, user_id);

-- The Entra groups a person had at the last sync. Null forces the next sign-in to sync again.
alter table app_user add column entra_groups_hash text;

-- Teams become departments: owners become leads.
insert into department (id, organization_id, name, description, created_at, updated_at)
select id, organization_id, name, description, created_at, updated_at from team;

insert into department_member (organization_id, department_id, user_id, role, created_at)
select organization_id, team_id, user_id, case role when 'owner' then 'lead' else 'member' end, created_at from team_member;

-- Everything that exists today lands in "Allgemein", with every active person as a member,
-- so nobody sees less than before the update.
insert into department (organization_id, name, description)
select o.id, 'Allgemein', 'Alles, was es vor den Abteilungen schon gab'
from organization o
where not exists (select 1 from department d where d.organization_id = o.id and lower(d.name) = 'allgemein');

insert into department_member (organization_id, department_id, user_id, role)
select u.organization_id, d.id, u.id, 'member'
from app_user u
join department d on d.organization_id = u.organization_id and lower(d.name) = 'allgemein'
where u.status = 'active' and u.anonymized_at is null
on conflict do nothing;

-- Projects: each belongs to one department. Existing projects stay visible exactly as before (only members and
-- organization admins), so they start 'private'; new projects default to 'department'.
alter table project add column department_id uuid;
alter table project add column visibility text not null default 'department'
    constraint ck_project_visibility check (visibility in ('private','department','organization'));

update project p set department_id = d.id, visibility = 'private'
from department d
where d.organization_id = p.organization_id and lower(d.name) = 'allgemein';

alter table project alter column department_id set not null;
alter table project add constraint fk_project_department_org foreign key (organization_id, department_id)
    references department(organization_id, id);
create index ix_project_department on project (organization_id, department_id);

-- Knowledge spaces and articles belong to a department.
alter table knowledge_space add column department_id uuid;
update knowledge_space s set department_id = d.id
from department d
where d.organization_id = s.organization_id and lower(d.name) = 'allgemein';
alter table knowledge_space alter column department_id set not null;
alter table knowledge_space add constraint fk_knowledge_space_department_org foreign key (organization_id, department_id)
    references department(organization_id, id);

alter table knowledge_article add column department_id uuid;
update knowledge_article a set department_id = coalesce(
    (select s.department_id from knowledge_space s where s.id = a.knowledge_space_id),
    (select d.id from department d where d.organization_id = a.organization_id and lower(d.name) = 'allgemein'));
alter table knowledge_article alter column department_id set not null;
alter table knowledge_article add constraint fk_knowledge_article_department_org foreign key (organization_id, department_id)
    references department(organization_id, id);
create index ix_knowledge_article_department on knowledge_article (organization_id, department_id);

-- Visibility of published articles gets a third level between the organization and explicit grants.
alter table knowledge_article drop constraint ck_knowledge_article_visibility;
alter table knowledge_article add constraint ck_knowledge_article_visibility
    check (visibility in ('organization', 'department', 'restricted'));
alter table knowledge_article alter column visibility set default 'department';

-- Grants and references to a team now point to the department with the same id.
alter table knowledge_permission drop constraint knowledge_permission_principal_type_check;
update knowledge_permission set principal_type = 'department' where principal_type = 'team';
alter table knowledge_permission add constraint knowledge_permission_principal_type_check
    check (principal_type in ('user','department'));

alter table knowledge_reference drop constraint knowledge_reference_resource_type_check;
update knowledge_reference set resource_type = 'department' where resource_type = 'team';
alter table knowledge_reference add constraint knowledge_reference_resource_type_check
    check (resource_type in ('project','task','department','whiteboard'));

drop table team_member;
drop table team;
