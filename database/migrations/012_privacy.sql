-- Data protection (ADR 0015).

-- Art. 17 GDPR: an admin can anonymize a person. The row stays so that tasks, comments and audit entries keep
-- a (neutral) author; name, e-mail, department and Entra object id are overwritten.
alter table app_user add column anonymized_at timestamptz;

-- Retention (DEC-005): the purge deletes by age across all organizations.
create index ix_notification_created on notification (created_at);
create index ix_activity_log_created on activity_log (created_at);
create index ix_audit_log_created on audit_log (created_at);
