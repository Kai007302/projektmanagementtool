# Open Decisions

Diese Punkte müssen vor Produktivbetrieb mit der Unternehmens-IT/Security abgestimmt werden.

| ID | Thema | Status | Default-Vorschlag |
|---|---|---|---|
| DEC-001 | Azure Region | offen | Deutschland / Unternehmensvorgabe |
| DEC-002 | Azure Hosting | offen | App Service oder Container Apps |
| DEC-003 | System-Mailbox | offen | dediziertes Funktionspostfach |
| DEC-004 | Entra Gruppenmodell | entschieden (Kai, 2026-10-07) | Abteilungen lassen sich mit je einer Entra-Sicherheitsgruppe verbinden; Mitgliedschaften kommen aus dem `groups`-Claim, wenn `PROJECTHUB_DEPARTMENTS_FROM_ENTRA_GROUPS=true` (ADR 0021) |
| DEC-005 | Datenretention | entschieden (Kai, 2026-10-05), vorerst | Benachrichtigungen, Projektaktivität und Audit-Log je 3 Jahre (1095 Tage), einstellbar über `PROJECTHUB_RETENTION_*` (ADR 0017); Mail-Warteschlange 7/30 Tage, Webhook-Ereignisse 30 Tage, Sicherungen 14 Tage |
| DEC-006 | erlaubte Dateitypen | offen | Security/IT Vorgabe |
| DEC-007 | maximale Dateigröße | offen | 100 MB als Startwert, bestätigen |
| DEC-008 | Whiteboard Storage | entschieden (Kai, 2026-10-03) | Synchronisation im API-Server, Updates in PostgreSQL, Snapshots im Blob-Speicher, Yjs-Dekodierung in einem isolierten Kindprozess (ADR 0009) |
| DEC-009 | Webex OAuth App Ownership | entschieden (Kai, 2026-10-03) | ein zentraler Bot der Organisation, kein OAuth je Person (ADR 0011) |
| DEC-010 | RPO/RTO | offen | RPO 15 min / RTO 2 h als Startziel; Sicherung, Wiederherstellung und Übung in `docs/OPERATIONS.md` |
| DEC-011 | Mandantenmodell | entschieden (Kai, 2026-10-07) | eine Organisation je Mandant, darin Abteilungen mit Leitung, Mitgliedern und Gästen; Projekte und Wissen gehören zu einer Abteilung, Sichtbarkeit privat/Abteilung/Organisation; Mitgliedschaften optional aus Entra-Gruppen (ADR 0021). Ersetzt die Teams |
| DEC-012 | Mail als Benutzer | offen | zunächst nein; nur System-Mailbox |
| DEC-013 | Benutzer-Provisionierung | entschieden (Kai, 2026-10-04) für den eigenen Server | `PROJECTHUB_USER_PROVISIONING=first-sign-in`: Personen des konfigurierten Mandanten werden beim ersten Login angelegt, die erste wird Admin; Einschränkung über „Zuweisung erforderlich“ in Entra (ADR 0014). Standard bleibt `off` (unbekannte Benutzer erhalten 403). SCIM/Gruppen-Abgleich bleibt Option für große Organisationen |
| DEC-014 | Rolle `member` vs. `editor` im Projekt | offen | `member` arbeitet an Inhalten (Tasks, Kommentare), `editor` ändert zusätzlich Projektdaten und Struktur; mit Fachbereich bestätigen |
| DEC-015 | Status-Workflow für Aufgaben | offen | feste Status `todo`/`in_progress`/`done`, frei wechselbar; Kanban-Spalten sind frei benennbar und gehören je zu einem Status (Migration 005). Eigene Status je Projekt erst bei Bedarf |
| DEC-016 | Wer darf Projekte anlegen | entschieden mit DEC-011 (2026-10-07) | Leitung und Mitglieder einer Abteilung legen Projekte in ihr an, Gäste nicht; die anlegende Person wird Projekt-Admin (ADR 0021) |
| DEC-017 | Ablage von Dateianhängen | offen | lokal hinter `IAttachmentStorage` (Development); produktiv Azure Blob Storage mit Virenscan, Umsetzung mit Azure-Anbindung |
| DEC-018 | WIP-Limit im Kanban | offen | nur Hinweis (Spalte wird rot markiert), kein Sperren beim Verschieben |
| DEC-019 | Was das Board zeigt | offen | Aufgaben der obersten Ebene; Unteraufgaben als Zähler auf der Karte |
| DEC-020 | Sichtbarkeit veröffentlichten Wissens | entschieden (Kai, 2026-10-03), erweitert mit DEC-011 (2026-10-07) | veröffentlichte und archivierte Artikel lesen standardmäßig alle der Abteilung, auf Wunsch der Leitung oder eines Admins alle in der Organisation; einzelne Artikel können auf „eingeschränkt“ gestellt werden und sind dann nur über Freigaben sichtbar. Entwürfe und Artikel in Prüfung sind nie abteilungs- oder organisationsweit sichtbar. Bestehende Artikel behalten ihre Sichtbarkeit (ADR 0021) |
| DEC-021 | Wer Wissensartikel veröffentlicht | offen | Artikel-Admins: Organisations-Admins, die Leitung der Abteilung, die verantwortliche Person (Owner) und Personen/Abteilungen mit Freigabe „Verwalten“. Zur Prüfung geben darf, wer bearbeiten darf |
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
| DEC-034 | Was KI ändern darf | entschieden (Kai, 2026-10-05) | Alles, was die Person selbst darf, aber jede Änderung erst nach ihrer Freigabe: Der Assistent zeigt jede Änderung als Karte mit „Ausführen“ und „Ablehnen“; über MCP fragt der Client nach, Schreibwerkzeuge nur mit `PROJECTHUB_MCP_WRITE_TOOLS=true` (ADR 0016) |
| DEC-035 | Gespräche mit dem Assistenten | offen | werden nicht gespeichert: der Verlauf liegt nur im Browser-Tab, ein auf Freigabe wartender Schritt verschlüsselt im Browser (ADR 0016), Telemetrie ohne Prompts und Antworten (ADR 0015) |
| DEC-036 | Embeddings für die Wissenssuche | offen | vorerst nicht: Volltextsuche mit agentischem Nachsuchen; pgvector hinter `IKnowledgeRetrieval`, wenn Antworten Umschreibungen nicht finden (ADR 0015) |
| DEC-037 | Löschen einer Person | entschieden (Kai, 2026-10-05) | Anonymisieren statt Löschen: Name, E-Mail, Entra-Kennung und Abteilung werden überschrieben, Mitgliedschaften, Benachrichtigungen und Freigaben gelöscht, Aufgaben frei; eigene Kommentare und Inhalte bleiben mit „Ehemalige Person“. Nur Organisations-Admins (ADR 0017). Wann anonymisiert wird (z. B. beim Austritt), legt der Betreiber fest |
| DEC-038 | Rechtstexte | offen | Datenschutzhinweise und Impressum als Links des Betreibers (`PROJECTHUB_PRIVACY_NOTICE_URL`, `PROJECTHUB_IMPRINT_URL`); auf dem eigenen Server statische Seiten unter `/rechtliches/`. Vorlage in `deploy/legal/` (ADR 0017, `docs/LEGAL.md`) |
| DEC-039 | Name und E-Mail aus Entra ID aktuell halten | entschieden (Kai, 2026-10-05) | bei jeder Anmeldung: weichen Name oder E-Mail im Token vom gespeicherten Wert ab, übernimmt ProjectHub sie (Art. 5 Abs. 1 lit. d DSGVO). Gehört die neue E-Mail schon einem anderen Konto, bleibt die alte (Warnung im Log). Abteilung kommt nicht aus dem Token (ADR 0017) |
| DEC-040 | Benachrichtigungen beim Import von Aufgaben | offen | keine Benachrichtigung je zugewiesener Aufgabe, damit ein Import mit vielen Zeilen keine Mailflut auslöst; die Aufgaben erscheinen im Board und im Aktivitätsfeed (ADR 0018) |
| DEC-041 | Inhalt des Kalender-Abos | offen | der Person zugewiesene Aufgaben mit Termin und Meilensteine ihrer Projekte, ab 90 Tagen zurück; Titel mit Projektname, keine Beschreibung. Wer die Adresse kennt, sieht das; kein Schalter für Betreiber, der Abos verbietet (ADR 0018) |
| DEC-042 | Was der Import von Aufgaben anlegt | offen | immer neue Aufgaben der obersten Ebene, alle Zeilen oder keine. Kein Aktualisieren über die ID, keine Unteraufgaben aus „Übergeordnete Aufgabe“ (ADR 0018) |
| DEC-043 | Veröffentlichte Images | offen | öffentlich in GHCR (`ghcr.io/kai007302/projecthub-*`), wie das Repository selbst; enthalten keine Secrets und keine Konfiguration. Privat ginge auch, dann braucht der Server Zugangsdaten für GHCR (ADR 0019) |
| DEC-044 | Projektlogos | offen | PNG, JPEG oder WebP bis 256 KB in PostgreSQL, kein SVG; ändern darf, wer im Projekt Schreibrecht hat (ADR 0020) |
| DEC-045 | Regel für den berechneten Fortschritt | offen | Kai (2026-10-08): Fortschritt nicht mehr von Hand. Vorschlag: ohne Unteraufgaben Offen 0 %, In Arbeit 50 %, Erledigt 100 %; mit Unteraufgaben gerundetes Mittel der Unteraufgaben, erledigt immer 100 %. Alternative: Anteil erledigter Unteraufgaben, „In Arbeit“ dann 0 % (ADR 0022) |


## Knowledge / AI

### Knowledge Galaxy renderer
Status: decided (ADR 0007)

Canvas 2D with a d3-force layout in a Web Worker; measured against React Flow with 300, 800 and 2000 nodes.

### AI provider
Status: proposed (ADR 0015)

Provider-neutral behind `IChatClient` (Microsoft.Extensions.AI) and chosen per installation in `.env`: `off` by default, `anthropic` (Claude, default model `claude-opus-5-5`) or `openai` (any OpenAI-compatible endpoint, including a local model such as Ollama). An external provider is only switched on with the organization's approval; on Kai's own server Kai decides.

### Knowledge visibility model
Status: decided (DEC-020)

Published knowledge is visible to its department by default; leads and admins can share it with the whole organization, and single articles can be restricted to explicit user/department permissions (ADR 0021).
