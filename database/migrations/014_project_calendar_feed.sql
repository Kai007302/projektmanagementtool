-- Project calendars (ADR 0020): besides the personal feed, a person can subscribe to the dates of one project.
-- Each subscription has its own secret address, so renewing one does not break the others.
alter table calendar_feed add column project_id uuid;
alter table calendar_feed
    add constraint fk_calendar_feed_project foreign key (organization_id, project_id)
        references project(organization_id, id) on delete cascade;
alter table calendar_feed drop constraint uq_calendar_feed_user;
alter table calendar_feed add constraint uq_calendar_feed_user_project unique nulls not distinct (user_id, project_id);
