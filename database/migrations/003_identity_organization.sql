-- Phase 1: Identity & Organization
-- Depends on 001_initial.sql

-- Maps an Entra ID tenant (token claim "tid") to its organization.
alter table organization add column entra_tenant_id text;
create unique index uq_organization_entra_tenant on organization (entra_tenant_id) where entra_tenant_id is not null;

-- Organization-wide role. Project roles live in project_member.role.
alter table app_user add column organization_role text not null default 'member'
    check (organization_role in ('admin','member'));
