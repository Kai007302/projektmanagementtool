-- Knowledge visibility (docs/OPEN_DECISIONS.md, DEC-020) and full-text search.

-- 'organization': once published, every member of the organization can read the article.
-- 'restricted': only the owner, explicit grants (knowledge_permission) and organization admins.
-- Drafts and articles in review are never visible organization-wide.
alter table knowledge_article
    add column visibility text not null default 'organization'
        constraint ck_knowledge_article_visibility check (visibility in ('organization', 'restricted'));

-- Plain text of the current version, maintained by the API on every content change.
-- The block JSON stays the canonical content; this column only feeds search.
alter table knowledge_article
    add column search_text text not null default '';

create index ix_knowledge_article_search on knowledge_article using gin (
    to_tsvector('german', title || ' ' || coalesce(summary, '') || ' ' || search_text)
) where deleted_at is null;

create index ix_knowledge_permission_article on knowledge_permission (article_id);
