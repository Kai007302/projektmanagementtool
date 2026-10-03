# AI Development Workflow

## Grundregel

Der Agent arbeitet streng phasenweise. Jeder Schritt endet mit einem buildbaren und testbaren Repository.

## Prompt-Reihenfolge

1. `ai/prompts/00-foundation.md`
2. `ai/prompts/01-identity-organization.md`
3. `ai/prompts/02-projects-tasks.md`

Weitere Phasen werden analog ergänzt.

## Vor jeder Änderung

- `AGENTS.md` lesen
- relevante ADRs lesen
- bestehende Implementierung prüfen
- keinen Code blind überschreiben

## Nach jeder Änderung

```text
format/lint
build
unit tests
integration tests
E2E/smoke tests, falls betroffen
security review
documentation update
```

## Human Review Gates

Menschliche Freigabe vor:

- Entra App Registration und Permission Consent
- Microsoft Graph Application Permissions
- Webex OAuth App
- Azure Production Infrastructure
- Datenretention
- Datenschutz/Security-Freigabe
- Produktionsmigrationen


## Knowledge-specific AI rules

When implementing Knowledge Hub or AI/RAG features:

- authorization filtering happens before retrieval results reach an LLM
- every generated answer must be able to cite source article/version IDs
- never train on or persist company content in an external provider without explicit approval
- use synthetic data during local development
- keep LLM provider access behind an interface
- log model, prompt-template version and source IDs for auditable internal AI usage, but never log full confidential prompts or tokens
