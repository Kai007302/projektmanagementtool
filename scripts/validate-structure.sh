#!/usr/bin/env bash
set -euo pipefail

required=(
  README.md
  AGENTS.md
  CLAUDE.md
  docs/PRODUCT.md
  docs/ARCHITECTURE.md
  docs/SECURITY.md
  docs/DATA_MODEL.md
  docs/INTEGRATIONS.md
  docs/OPEN_DECISIONS.md
  docs/REFERENCES.md
  docs/diagrams/er.mmd
  database/migrations/001_initial.sql
  database/migrations/002_knowledge.sql
  docker-compose.yml
  .env.example
  ai/AI_WORKFLOW.md
  ai/prompts/00-foundation.md
  ai/prompts/01-identity-organization.md
  ai/prompts/02-projects-tasks.md
)

for file in "${required[@]}"; do
  test -f "$file" || { echo "Missing: $file"; exit 1; }
done

echo "ProjectHub AI starter structure OK"
