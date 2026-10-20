-- Insights and use cases of knowledge spaces (ADR 0023).
-- A space says what it is used for; insight articles (experiments, spikes, decisions …) carry a result and a confidence.

alter table knowledge_space
    add column use_case text not null default 'general'
        constraint ck_knowledge_space_use_case check (use_case in ('general', 'experiment', 'software'));

alter table knowledge_article drop constraint knowledge_article_article_type_check;
alter table knowledge_article add constraint knowledge_article_article_type_check
    check (article_type in ('article', 'how_to', 'best_practice', 'process', 'policy', 'faq', 'template', 'checklist', 'glossary',
                            'experiment', 'spike', 'decision', 'retro_learning', 'incident', 'release'));

-- Which outcomes fit which article type is decided by the application (KnowledgeUseCases); the database keeps the shape.
alter table knowledge_article
    add column outcome text
        constraint ck_knowledge_article_outcome check (outcome is null or outcome ~ '^[a-z_]{1,32}$'),
    add column confidence smallint
        constraint ck_knowledge_article_confidence check (confidence is null or confidence between 1 and 4);

alter table knowledge_relation drop constraint knowledge_relation_relation_type_check;
alter table knowledge_relation add constraint knowledge_relation_relation_type_check
    check (relation_type in ('RELATED', 'REQUIRES', 'PART_OF', 'SUPERSEDES', 'REFERENCES', 'BUILDS_ON', 'CONFIRMS', 'CONTRADICTS'));
