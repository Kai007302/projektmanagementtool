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
| DEC-013 | Benutzer-Provisionierung | offen | Benutzer werden nicht automatisch beim ersten Login angelegt; Abgleich über Entra Groups/SCIM oder Just-in-Time klären. Bis dahin: unbekannte Benutzer erhalten 403 |
| DEC-014 | Rolle `member` vs. `editor` im Projekt | offen | `member` arbeitet an Inhalten (Tasks, Kommentare), `editor` ändert zusätzlich Projektdaten und Struktur; mit Fachbereich bestätigen |
| DEC-015 | Status-Workflow für Aufgaben | offen | feste Status `todo`/`in_progress`/`done`, frei wechselbar; Kanban-Spalten sind frei benennbar und gehören je zu einem Status (Migration 005). Eigene Status je Projekt erst bei Bedarf |
| DEC-016 | Wer darf Projekte anlegen | offen | jeder aktive Benutzer der Organisation, die anlegende Person wird Projekt-Admin; einschränkbar über `CanCreateProject` |
| DEC-017 | Ablage von Dateianhängen | offen | lokal hinter `IAttachmentStorage` (Development); produktiv Azure Blob Storage mit Virenscan, Umsetzung mit Azure-Anbindung |
| DEC-018 | WIP-Limit im Kanban | offen | nur Hinweis (Spalte wird rot markiert), kein Sperren beim Verschieben |
| DEC-019 | Was das Board zeigt | offen | Aufgaben der obersten Ebene; Unteraufgaben als Zähler auf der Karte |
| DEC-020 | Sichtbarkeit veröffentlichten Wissens | entschieden (Kai, 2026-10-03) | veröffentlichte und archivierte Artikel lesen alle in der Organisation; einzelne Artikel können auf „eingeschränkt“ gestellt werden und sind dann nur über Freigaben sichtbar. Entwürfe und Artikel in Prüfung sind nie organisationsweit sichtbar |
| DEC-021 | Wer Wissensartikel veröffentlicht | offen | Artikel-Admins: Organisations-Admins, die verantwortliche Person (Owner) und Personen/Teams mit Freigabe „Verwalten“. Zur Prüfung geben darf, wer bearbeiten darf |
| DEC-022 | Verweise von Wissen auf Whiteboards | offen | erst mit dem Whiteboard-Modul (Phase 7); bis dahin lehnt die API `whiteboard` als Verweisziel ab |


## Knowledge / AI

### Knowledge Galaxy renderer
Status: decided (ADR 0007)

Canvas 2D with a d3-force layout in a Web Worker; measured against React Flow with 300, 800 and 2000 nodes.

### AI provider
Status: open

Choose between enterprise-approved Azure OpenAI / another approved provider. No external provider is assumed until company security/privacy approval.

### Knowledge visibility model
Status: decided (DEC-020)

Published knowledge is organization-wide by default; single articles can be restricted to explicit user/team permissions.
