-- Phase 2: Projects & Tasks
-- Projects are soft-deleted like tasks, so their activity history stays intact.

alter table project add column deleted_at timestamptz;
create index ix_project_org_active on project (organization_id) where deleted_at is null;
