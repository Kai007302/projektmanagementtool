# Open Decisions

Diese Punkte müssen vor Produktivbetrieb mit der Unternehmens-IT/Security abgestimmt werden.

| ID | Thema | Status | Default-Vorschlag |
|---|---|---|---|
| DEC-001 | Azure Region | offen | Deutschland / Unternehmensvorgabe |
| DEC-002 | Azure Hosting | offen | App Service oder Container Apps |
| DEC-003 | System-Mailbox | offen | dediziertes Funktionspostfach |
| DEC-004 | Entra Gruppenmodell | offen | Teams/Abteilungen über Entra Groups, wo sinnvoll |
| DEC-005 | Datenretention | offen | Unternehmensrichtlinie |
| DEC-006 | erlaubte Dateitypen | offen | Security/IT Vorgabe |
| DEC-007 | maximale Dateigröße | offen | 100 MB als Startwert, bestätigen |
| DEC-008 | Whiteboard Storage | offen | Collaboration Layer + Snapshots |
| DEC-009 | Webex OAuth App Ownership | offen | zentrale Unternehmens-App-Registrierung |
| DEC-010 | RPO/RTO | offen | RPO 15 min / RTO 2 h als Startziel |
| DEC-011 | Mandantenmodell | offen | eine Org, aber schema-/API-seitig org-aware |
| DEC-012 | Mail als Benutzer | offen | zunächst nein; nur System-Mailbox |


## Knowledge / AI

### Knowledge Galaxy renderer
Status: open

Evaluate React Flow vs D3 Canvas/WebGL using a synthetic graph with at least several hundred nodes.

### AI provider
Status: open

Choose between enterprise-approved Azure OpenAI / another approved provider. No external provider is assumed until company security/privacy approval.

### Knowledge visibility model
Status: open

Confirm whether published Knowledge defaults to organization-wide visibility or requires explicit permissions.
