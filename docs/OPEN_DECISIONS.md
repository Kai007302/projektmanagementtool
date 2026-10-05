# Open Decisions

Diese Punkte müssen vor Produktivbetrieb mit der Unternehmens-IT/Security abgestimmt werden.

| ID | Thema | Status | Default-Vorschlag |
|---|---|---|---|
| DEC-001 | Azure Region | offen | Deutschland / Unternehmensvorgabe |
| DEC-002 | Azure Hosting | offen | App Service oder Container Apps |
| DEC-003 | System-Mailbox | offen | dediziertes Funktionspostfach |
| DEC-004 | Entra Gruppenmodell | offen | Teams/Abteilungen über Entra Groups, wo sinnvoll |
| DEC-005 | Datenretention | entschieden (Kai, 2026-10-05), vorerst | Benachrichtigungen, Projektaktivität und Audit-Log je 3 Jahre (1095 Tage), einstellbar über `PROJECTHUB_RETENTION_*` (ADR 0015); Mail-Warteschlange 7/30 Tage, Webhook-Ereignisse 30 Tage, Sicherungen 14 Tage |
| DEC-006 | erlaubte Dateitypen | offen | Security/IT Vorgabe |
| DEC-007 | maximale Dateigröße | offen | 100 MB als Startwert, bestätigen |
| DEC-008 | Whiteboard Storage | entschieden (Kai, 2026-10-03) | Synchronisation im API-Server, Updates in PostgreSQL, Snapshots im Blob-Speicher, Yjs-Dekodierung in einem isolierten Kindprozess (ADR 0009) |
| DEC-009 | Webex OAuth App Ownership | entschieden (Kai, 2026-10-03) | ein zentraler Bot der Organisation, kein OAuth je Person (ADR 0011) |
| DEC-010 | RPO/RTO | offen | RPO 15 min / RTO 2 h als Startziel; Sicherung, Wiederherstellung und Übung in `docs/OPERATIONS.md` |
| DEC-011 | Mandantenmodell | offen | eine Org, aber schema-/API-seitig org-aware |
| DEC-012 | Mail als Benutzer | offen | zunächst nein; nur System-Mailbox |
| DEC-013 | Benutzer-Provisionierung | entschieden (Kai, 2026-10-04) für den eigenen Server | `PROJECTHUB_USER_PROVISIONING=first-sign-in`: Personen des konfigurierten Mandanten werden beim ersten Login angelegt, die erste wird Admin; Einschränkung über „Zuweisung erforderlich“ in Entra (ADR 0014). Standard bleibt `off` (unbekannte Benutzer erhalten 403). SCIM/Gruppen-Abgleich bleibt Option für große Organisationen |
| DEC-014 | Rolle `member` vs. `editor` im Projekt | offen | `member` arbeitet an Inhalten (Tasks, Kommentare), `editor` ändert zusätzlich Projektdaten und Struktur; mit Fachbereich bestätigen |
| DEC-015 | Status-Workflow für Aufgaben | offen | feste Status `todo`/`in_progress`/`done`, frei wechselbar; Kanban-Spalten sind frei benennbar und gehören je zu einem Status (Migration 005). Eigene Status je Projekt erst bei Bedarf |
| DEC-016 | Wer darf Projekte anlegen | offen | jeder aktive Benutzer der Organisation, die anlegende Person wird Projekt-Admin; einschränkbar über `CanCreateProject` |
| DEC-017 | Ablage von Dateianhängen | offen | lokal hinter `IAttachmentStorage` (Development); produktiv Azure Blob Storage mit Virenscan, Umsetzung mit Azure-Anbindung |
| DEC-018 | WIP-Limit im Kanban | offen | nur Hinweis (Spalte wird rot markiert), kein Sperren beim Verschieben |
| DEC-019 | Was das Board zeigt | offen | Aufgaben der obersten Ebene; Unteraufgaben als Zähler auf der Karte |
| DEC-020 | Sichtbarkeit veröffentlichten Wissens | entschieden (Kai, 2026-10-03) | veröffentlichte und archivierte Artikel lesen alle in der Organisation; einzelne Artikel können auf „eingeschränkt“ gestellt werden und sind dann nur über Freigaben sichtbar. Entwürfe und Artikel in Prüfung sind nie organisationsweit sichtbar |
| DEC-021 | Wer Wissensartikel veröffentlicht | offen | Artikel-Admins: Organisations-Admins, die verantwortliche Person (Owner) und Personen/Teams mit Freigabe „Verwalten“. Zur Prüfung geben darf, wer bearbeiten darf |
| DEC-022 | Verweise von Wissen auf Whiteboards | umgesetzt (Phase 7) | Wissensartikel verweisen auf Whiteboards; sichtbar nur, wer das Whiteboard sehen darf |
| DEC-023 | Abhängigkeiten im Gantt | offen | nur Warnung: verletzte Abhängigkeiten werden markiert, Nachfolger werden nicht automatisch verschoben. Nur innerhalb eines Projekts, höchstens eine Abhängigkeit je Aufgabenpaar, keine Kreise |
| DEC-024 | Wer Gantt-Termine und Meilensteine pflegt | offen | Termine verschieben und Abhängigkeiten pflegen: Contribute (wie Aufgaben ändern); Meilensteine: Edit (Projektstruktur) |
| DEC-025 | Einstellungen für Benachrichtigungen | offen | je Kanal (In-App, Mail), nicht je Ereignistyp; Standard: beides an |
| DEC-026 | Worüber benachrichtigt wird | offen | Zuweisung einer Aufgabe, `@`-Erwähnung (Aufgaben- und Wissenskommentare), Aufnahme in ein Projekt; nie über eigene Aktionen. Fälligkeitserinnerungen folgen später (brauchen einen Hintergrundjob) |
| DEC-027 | Wer Whiteboards pflegt | offen | ansehen: View; zeichnen: Contribute (wie Aufgaben); anlegen, umbenennen, löschen: Edit |
| DEC-028 | Outlook-Termine aus Aufgaben und Meilensteinen | offen | ohne Graph-Berechtigung: `.ics`-Datei und Outlook-Deeplink im Browser. Delegiertes `Calendars.ReadWrite` (Termin ohne eigenen Klick in Outlook) erst bei konkretem Bedarf |
| DEC-029 | Mail-Warteschlange bis Azure | entschieden (Kai, 2026-10-03) | Tabelle `mail_outbox` in PostgreSQL mit Hintergrund-Worker (ADR 0010); Service Bus später dahinter |
| DEC-030 | Rate Limits und Größenlimits | offen | je Person und Minute: 600 Anfragen, 30 Uploads; Webex-Webhook 120 je Adresse; Anfragen höchstens 4 MB (Uploads eigenes Limit). Zähler je Instanz, verteilt erst bei Bedarf (ADR 0012) |
| DEC-031 | Auslieferung der Oberfläche | offen | eigener Web-Container (Nginx) vor der API, eine Origin für Browser, TLS am Ingress (ADR 0012) |
| DEC-032 | Performance-Ziele | offen | je API-Instanz: 95 % der Lesezugriffe unter 300 ms, unter 1 % Fehler; geprüft mit dem Lasttest `tests/load` (50 gleichzeitige Personen, 1000 Aufgaben, 300 Artikel). Ergebnis in `docs/OPERATIONS.md` |
| DEC-033 | Redis in der Readiness | offen | heute: Redis nicht erreichbar → Instanz nicht bereit, die App ist weg, obwohl nur Realtime betroffen ist. Vorschlag: Redis nur als „beeinträchtigt“ melden und per Alarm sichtbar machen (`docs/OPERATIONS.md`) |
| DEC-034 | Löschen einer Person | entschieden (Kai, 2026-10-05) | Anonymisieren statt Löschen: Name, E-Mail, Entra-Kennung und Abteilung werden überschrieben, Mitgliedschaften, Benachrichtigungen und Freigaben gelöscht, Aufgaben frei; eigene Kommentare und Inhalte bleiben mit „Ehemalige Person“. Nur Organisations-Admins (ADR 0015). Wann anonymisiert wird (z. B. beim Austritt), legt der Betreiber fest |
| DEC-035 | Rechtstexte | offen | Datenschutzhinweise und Impressum als Links des Betreibers (`PROJECTHUB_PRIVACY_NOTICE_URL`, `PROJECTHUB_IMPRINT_URL`); auf dem eigenen Server statische Seiten unter `/rechtliches/`. Vorlage in `deploy/legal/` (ADR 0015, `docs/LEGAL.md`) |
| DEC-036 | Name und E-Mail aus Entra ID aktuell halten | entschieden (Kai, 2026-10-05) | bei jeder Anmeldung: weichen Name oder E-Mail im Token vom gespeicherten Wert ab, übernimmt ProjectHub sie (Art. 5 Abs. 1 lit. d DSGVO). Gehört die neue E-Mail schon einem anderen Konto, bleibt die alte (Warnung im Log). Abteilung kommt nicht aus dem Token (ADR 0015) |


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
