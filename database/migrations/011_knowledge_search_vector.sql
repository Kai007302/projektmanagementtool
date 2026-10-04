-- Phase 10b load test: full-text search computed to_tsvector for every candidate row twice (recheck and rank).
-- The vector is now stored with the row; the index and the ranking read it instead of recomputing it.

alter table knowledge_article
    add column search_vector tsvector not null generated always as (
        to_tsvector('german', title || ' ' || coalesce(summary, '') || ' ' || search_text)
    ) stored;

create index ix_knowledge_article_search_vector on knowledge_article using gin (search_vector) where deleted_at is null;

drop index ix_knowledge_article_search;
