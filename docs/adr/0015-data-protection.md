# ADR 0015 — Datenschutzfunktionen: Export, Anonymisierung, Löschfristen, Rechtstexte

## Status
Proposed

## Kontext

ProjectHub verarbeitet Daten von Beschäftigten; DSGVO und BDSG gelten (Überblick: `docs/LEGAL.md`). Bis hierhin fehlten Funktionen, mit denen ein Betreiber die Rechte der Betroffenen erfüllen kann: keine Auskunft außer per SQL, keine Löschung einer Person (Fremdschlüssel aus Aufgaben, Kommentaren und Protokollen verhindern das Löschen der Zeile), keine Fristen für Benachrichtigungen und Protokolle (DEC-005), keine Stelle für Datenschutzhinweise. Außerdem schrieb der Web-Container ein Zugriffslog mit IP-Adresse und vollem Query-String; bei den Live-Verbindungen steht dort das Anmelde-Token (`access_token`, ADR 0014).

## Entscheidung

### Rechtstexte

- `PROJECTHUB_PRIVACY_NOTICE_URL` und `PROJECTHUB_IMPRINT_URL` (https-Adresse oder Pfad auf demselben Host, sonst startet die API nicht). `GET /api/v1/legal` liefert sie ohne Anmeldung; die Oberfläche zeigt sie unten auf jeder Seite.
- Auf dem eigenen Server liefert Caddy den Ordner `./legal` unter `/rechtliches/` aus (eigene, strenge CSP, ohne Anmeldung). Der Ordner ist nicht im Repository; die Vorlage liegt in `deploy/legal/`.

### Auskunft (Art. 15, 20)

- `GET /api/v1/me/data-export` liefert alles, was zur angemeldeten Person gespeichert ist, als JSON-Datei: Profil, Mitgliedschaften, Benachrichtigungen und Einstellungen, Mails in der Warteschlange, zugewiesene und angelegte Aufgaben (Titel, keine Beschreibung), eigene Kommentare, eigene Artikel und Versionen (ohne Inhalt), hochgeladene Anhänge (Metadaten), Anzahl Whiteboard-Änderungen, eigene Aktivitäts- und Audit-Einträge (ohne Metadaten).
- Jeder Export wird im Audit-Log vermerkt (`PersonalDataExported`).

### Löschung (Art. 17) als Anonymisierung

- `POST /api/v1/admin/users/{id}/anonymize`, nur Organisations-Admins, nie die eigene Person (so bleibt immer ein Admin). Wiederholen ändert nichts.
- Die Zeile in `app_user` bleibt, damit Aufgaben, Kommentare, Versionen und Protokolle gültige Autoren behalten. Überschrieben werden Name („Ehemalige Person“), E-Mail (`anonymized-<id>@invalid`), Entra-Kennung, Abteilung, letzter Login; Status `inactive`, Rolle `member`, `anonymized_at` gesetzt (Migration 012).
- Gelöscht werden Benachrichtigungen, Einstellungen, Mails in der Warteschlange, Team- und Projektmitgliedschaften und persönliche Freigaben für Wissensartikel. Zugewiesene Aufgaben werden frei; Artikel und Wissensbereiche verlieren ihre verantwortliche Person.
- Inhalte, die die Person geschrieben hat, und was andere über sie geschrieben haben, bleiben unverändert (DEC-034).
- `/users` liefert anonymisierte Personen nicht mehr. Meldet sich das Microsoft-Konto später wieder an, entsteht bei `first-sign-in` ein neues, leeres Konto.
- Oberfläche: Teams → „Person anonymisieren“ mit Suche und Bestätigung.

### Richtigkeit: Name und E-Mail aus Entra ID (DEC-036)

- Bei jeder Anfrage vergleicht die API Name und E-Mail im Token mit dem gespeicherten Benutzer. Weichen sie ab, übernimmt sie die neuen Werte (Art. 5 Abs. 1 lit. d DSGVO). Das ergänzt ADR 0014, nach der bestehende Benutzer nie verändert wurden; Rolle und Status bleiben weiterhin unangetastet.
- Gehört die neue E-Mail schon einem anderen Benutzer der Organisation, bleibt die alte (Warnung im Log, ohne Adresse).
- Die Abteilung steht nicht im Token und wird nicht abgeglichen.

### Löschfristen (DEC-005)

- `PROJECTHUB_RETENTION_NOTIFICATION_DAYS`, `PROJECTHUB_RETENTION_ACTIVITY_DAYS`, `PROJECTHUB_RETENTION_AUDIT_DAYS`, je 1095 Tage (3 Jahre, Kai 2026-10-05); `0` bewahrt unbegrenzt auf.
- Ein Hintergrunddienst löscht eine Minute nach dem Start und dann alle sechs Stunden, was älter ist. Er läuft in jeder Instanz; Löschen ist idempotent. Das Audit-Log bleibt für Benutzer unveränderbar; nur diese Frist entfernt Einträge.
- Bestehende Fristen bleiben: Mail-Warteschlange 7/30 Tage (ADR 0010), Webhook-Ereignisse 30 Tage (ADR 0011), Sicherungen 14 Tage (ADR 0014).

### Protokolle auf dem Server

- Nginx protokolliert nur Zeit, Methode, Pfad ohne Query, Status, Größe und Dauer; keine IP-Adresse, kein Token.
- `docker-compose.prod.yml` begrenzt die Container-Logs auf 5 × 10 MB je Dienst.

## Alternativen

- **Person wirklich löschen:** bräuchte für jede Referenz eine Regel (Kommentare löschen? Aufgaben umhängen?) und zerstört die Nachvollziehbarkeit der Projekte. Die anonymisierte Zeile identifiziert niemanden mehr und ist für den Zweck der Löschung gleichwertig.
- **Kommentare der Person mitlöschen:** Kommentare sind Teil der Projektdokumentation; der Betreiber kann einzelne Kommentare weiterhin löschen.
- **Fristen als Datenbank-Job (pg_cron):** zusätzliche Erweiterung im Datenbank-Image; der Hintergrunddienst passt zum Muster von Mail-Warteschlange und Webhooks.
- **Datenschutzhinweise in der App pflegen:** braucht Editor, Versionierung und Freigabe; eine statische Seite des Betreibers genügt.

## Konsequenzen

- Betreiber können Auskunfts- und Löschanfragen ohne SQL erfüllen; die Rechtstexte selbst bleiben ihre Aufgabe (`docs/LEGAL.md`, Checkliste).
- Nach dem Update löscht die API beim ersten Lauf alles, was älter als die Fristen ist. Wer das nicht will, setzt die Werte vorher auf `0`.
- Sicherungen enthalten anonymisierte Personen noch bis zum Ablauf ihrer Aufbewahrung; eine Wiederherstellung holt sie zurück und muss die Anonymisierung wiederholen.
- Namen, die in Kommentartexten stehen, bleiben stehen.
