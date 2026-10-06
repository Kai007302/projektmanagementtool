# ADR 0020 — Kalender je Projekt, Projektsymbol und Logo

## Status
Proposed

## Kontext

Kai hat am 2026-10-06 gewünscht, dass „Kalender abonnieren“ und „Daten herunterladen“ nicht mehr unten auf der Übersichtsseite stehen, sondern in einem kleinen Menü am jeweiligen Projekt, und dass man Projektsymbole ändern und Logos einbinden kann. Auf seine Wahl „Pro Projekt“ hin bekommt jedes Projekt einen eigenen Kalender. Die persönlichen Dinge (eigene Termine, DSGVO-Export) wandern ins Menü am Avatar.

## Entscheidung

### Kalender je Projekt

- `calendar_feed.project_id` (Migration 014): ohne Projekt wie bisher die eigenen Termine der Person (ADR 0018), mit Projekt alle datierten Aufgaben und Meilensteine dieses Projekts. Je Person und Projekt höchstens eine Adresse (`unique nulls not distinct (user_id, project_id)`); jede Adresse hat ihr eigenes Token, damit „Neue Adresse erstellen“ bei einem Projekt die anderen Abos nicht beendet.
- `GET|POST|DELETE /api/v1/projects/{id}/calendar-feed` mit Recht View. Abruf über dieselbe Adresse `GET /api/v1/calendar-feed.ics?token=…`; bei jedem Abruf wird das Leserecht der Person auf das Projekt neu geprüft. Wer nicht mehr Mitglied ist oder deren Projekt gelöscht wurde, bekommt 404. Hartes Löschen des Projekts löscht die Adresse mit.
- Gleiche Regeln wie ADR 0018: Token nur einmal angezeigt, nur der Hash gespeichert, Audit-Eintrag mit Projekt-ID, ab 90 Tagen zurück, höchstens je 1.000.
- DSGVO: Der Export nennt zusätzlich die Projektkalender (Projekt, erstellt, letzter Abruf); die Anonymisierung löscht alle Adressen der Person.

### Symbol und Logo

- `project.icon` (Migration 015): ein kurzes Emoji (höchstens 16 Zeichen, keine Buchstaben, Ziffern oder Leerzeichen), gesetzt über `PATCH /projects/{id}` mit `icon` (Recht Edit). Ohne Symbol bleibt das stabile Emoji aus der Projekt-ID (`ui/personality.ts`).
- Logo als Bild in eigener Tabelle `project_logo` (höchstens 256 KB, PNG, JPEG oder WebP). Der Typ wird aus den ersten Bytes erkannt, nicht aus Dateiname oder Header; **SVG wird abgelehnt**, weil es Skripte enthalten kann. `GET /projects/{id}/logo` (View, `X-Content-Type-Options: nosniff`), `PUT` als Datei `file` (Edit, Upload-Rate-Limit), `DELETE` (Edit).
- `project.logo_version` ändert sich mit jedem Logo und steht in Projektliste und -details; der Browser lädt das Bild nur bei neuer Version neu. Das Logo ändert die `version` des Projekts nicht, damit offene Bearbeitungen keinen Konflikt bekommen.
- Die Oberfläche lädt das Logo mit Bearer-Token als Blob, weil ein `<img src>` keinen Token senden kann.

### Oberfläche

- Jede Projektkachel und die Projektansicht haben ein „…“-Menü: „Kalender abonnieren“, „Daten herunterladen (Excel)“ (Export aus ADR 0018) und für Personen mit Schreibrecht „Symbol und Logo ändern“.
- Das Menü am Avatar enthält „Meine Termine abonnieren“ und „Meine Daten herunterladen“. Unten auf der Seite stehen nur noch Datenschutz und Impressum.

## Alternativen

- **Ein Token für alle Kalender einer Person** mit Projekt als Parameter: Erneuern beträfe alle Abos auf einmal, und die Adresse eines Projekts gäbe Zugriff auf alle.
- **Logo als Anhang im Dateispeicher** (ADR 0007): Anhänge gehören zu Aufgaben und Artikeln und sind für größere Dateien gedacht; ein kleines Bild in PostgreSQL ist einfacher zu sichern (`scripts/backup.sh`) und wird mit dem Projekt gelöscht.
- **Logo als Spalte in `project`:** Jede Projektliste würde die Bytes mitladen.
- **SVG erlauben und bereinigen:** Bereinigen ist fehleranfällig; Logos gibt es in der Regel auch als PNG.

## Konsequenzen

- Wer die Adresse eines Projektkalenders kennt, sieht die Titel aller datierten Aufgaben und Meilensteine des Projekts, solange die Person, die sie erstellt hat, das Projekt sehen darf (vgl. ADR 0018).
- Logos erhöhen die Datenbankgröße um höchstens 256 KB je Projekt.
- Neue Migrationen 014 und 015; ältere Versionen der Oberfläche funktionieren weiter, die neuen Felder sind optional.
