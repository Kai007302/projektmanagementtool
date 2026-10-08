# ADR 0021 — Abteilungen statt Teams

## Status
Proposed

## Kontext

Teams dienten nur dazu, Wissensartikel freizugeben; nur Admins legten sie an, alle sahen die Liste. Kai hat am 2026-10-07 gewünscht, dass jede Abteilung ProjectHub für sich nutzen kann, und den Vorschlag „Rechtemodell mit Abteilungen“ (Claude Doc) mit „Alles so umsetzen; wie im Dokument. Aus Entra übernehmen bitte direkt mit einbauen“ freigegeben. Damit ist DEC-011 entschieden: eine Organisation je Mandant, darin Abteilungen.

## Entscheidung

### Abteilungen und Rollen

- Tabellen `department` und `department_member` (Migration 016) ersetzen `team` und `team_member`. Bestehende Teams werden zu Abteilungen mit derselben ID, Team-Owner zu Leitungen. Freigaben und Verweise auf ein Team zeigen danach auf die Abteilung.
- Rollen in der Abteilung: `lead` (Abteilungsleitung), `member`, `guest`. Eine Person kann in mehreren Abteilungen sein.
  - **Leitung** verwaltet die Abteilung (Name, Beschreibung, Personen) und hat alle Rechte auf allen Projekten und Artikeln der Abteilung, auch ohne Projektmitgliedschaft.
  - **Mitglied** legt Projekte und Artikel in der Abteilung an und liest alles, was für die Abteilung sichtbar ist.
  - **Gast** sieht nur Projekte, zu denen er eingeladen ist, und kein Wissen der Abteilung.
- Organisations-Admins legen Abteilungen an, löschen leere und verbinden sie mit Entra-Gruppen. Sie sehen alles; öffnen sie ein Projekt oder einen Artikel, den sie nur als Admin sehen, steht das als `OrganizationAdminAccess` im `audit_log`.
- Projektrollen (`admin`, `editor`, `member`, `viewer`, `guest`) bleiben unverändert.

### Sichtbarkeit

- Jedes Projekt und jeder Artikel gehört zu genau einer Abteilung. Wissensbereiche gehören ebenfalls zu einer Abteilung, Artikel in einem Bereich zu dessen Abteilung.
- Projekte: `private` (nur Mitglieder), `department` (Standard; Mitglieder der Abteilung lesen mit), `organization` (alle der Organisation lesen mit). Lesen heißt Recht View; mitarbeiten braucht weiter eine Projektrolle.
- Veröffentlichte Artikel: `department` (Standard), `organization` oder `restricted` (nur über Freigaben). Entwürfe sieht weiter nur, wer ein Recht am Artikel hat.
- Für die ganze Organisation freigeben dürfen nur Organisations-Admins und die Leitung der Abteilung, bei Projekten und bei Artikeln. Die API meldet das als `canShareWithOrganization` in den Rechten.
- Sichtbarkeit und Abteilung eines Projekts ändert, wer es verwaltet (Manage). Verschoben werden kann es nur in eine Abteilung, in der die Person arbeitet (Leitung oder Mitglied). Beides steht im `audit_log` (`ProjectVisibilityChanged`, `ProjectMoved`).
- Alle Prüfungen laufen serverseitig in `IProjectHubAuthorization` und `KnowledgeAccess`, Listen und Suche filtern in der Datenbankabfrage.

### Übergang

- Migration 016 legt in jeder Organisation „Allgemein“ an und nimmt alle aktiven Personen als Mitglieder auf. Bestehende Projekte, Bereiche und Artikel kommen nach „Allgemein“.
- Bestehende Projekte werden `private`: Nach dem Update sieht niemand mehr als vorher. Bestehende Artikel behalten ihre Sichtbarkeit (`organization` oder `restricted`).
- Neue Projekte und Artikel ohne Angabe kommen in die erste Abteilung der Person, in der sie Leitung oder Mitglied ist (nach Beitrittsdatum).
- Die erste Person einer neuen Organisation (Provisionierung, ADR 0014) wird Admin und Leitung von „Allgemein“.

### Abteilungen aus Entra ID

- Mit `PROJECTHUB_DEPARTMENTS_FROM_ENTRA_GROUPS=true` übernimmt die API Mitgliedschaften aus dem `groups`-Claim des Tokens. Admins tragen an der Abteilung die Objekt-ID einer Sicherheitsgruppe ein; wer in der Gruppe ist, kommt bei der Anmeldung als Mitglied in die Abteilung und verlässt sie wieder, wenn er die Gruppe verlässt.
- Von Hand hinzugefügte Mitgliedschaften (`source = 'manual'`) fasst der Abgleich nie an. Von Hand geänderte Rollen bleiben erhalten.
- Der Abgleich braucht keine Microsoft-Graph-Berechtigung. Er läuft nur, wenn sich die Gruppen im Token seit dem letzten Mal geändert haben (Hash in `app_user.entra_groups_hash`); eine geänderte Gruppen-ID an einer Abteilung setzt die Hashes zurück.
- Hat jemand zu viele Gruppen für das Token (Group Overage, Claim `_claim_names` oder `hasgroups`), bleiben seine Abteilungen unverändert, und die API schreibt eine Warnung ins Log. Abhilfe: in der App-Registrierung nur „Gruppen, die der Anwendung zugewiesen sind“ ausgeben.
- Standard ist aus; dann gelten nur die Mitgliedschaften aus der Verwaltung.

### Neue Personen

- Wer beim ersten Login in keine Abteilung kommt, bekommt einen Hinweis in der Oberfläche. Organisations-Admins und alle Abteilungsleitungen werden benachrichtigt (`person_without_department`) und sehen die Person unter „Verwaltung → Ohne Abteilung“.

### Oberfläche

- Der Bereich „Verwaltung“ ersetzt „Teams“ und ist nur für Admins und Leitungen sichtbar. Leitungen sehen dort nur ihre eigenen Abteilungen. Dort stehen auch „Person anonymisieren“ (nur Admins, ADR 0017) und die Personen ohne Abteilung.
- Oben im Kopf wählt man die Abteilung, deren Projekte, Wissen und Galaxie gezeigt werden. Der Start ist „Alle Abteilungen“, damit niemand nach dem Update weniger sieht; die Wahl merkt sich der Browser je Person.
- „Sichtbarkeit und Abteilung“ im „…“-Menü eines Projekts, drei Stufen der Sichtbarkeit im Artikel-Editor.

## Konsequenzen

- Ein Teil des Projekts ist für die ganze Abteilung lesbar, wenn es nicht privat gestellt wird. Das ist gewollt, ändert aber, wer was sieht; deshalb starten bestehende Projekte privat.
- Abteilungsleitungen haben weitreichende Rechte in ihrer Abteilung, ohne Mitglied der Projekte zu sein. Das steht nicht im Audit, weil es ihre Rolle ist; nur der Zugriff von Admins auf Fremdes wird protokolliert.
- Die KI-Werkzeuge (`list_departments` statt `list_teams`) und der MCP-Server sehen dasselbe wie die Person.
- Die Entra-Anbindung hängt an der App-Registrierung: Ohne „Gruppenanspruch“ im Token kommt nichts an (docs/SELF_HOSTING.md).
- Offen für Kai: diese ADR freigeben.
