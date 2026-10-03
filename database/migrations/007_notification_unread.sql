-- Phase 6: the unread counter of the notification bell is read on every page load and after every change.
create index ix_notification_user_unread on notification (organization_id, user_id) where read_at is null;
