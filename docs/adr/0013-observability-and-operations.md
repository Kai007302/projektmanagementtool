# ADR 0013 — Beobachtbarkeit und Betrieb

## Status
Proposed

## Kontext

Für den Pilotbetrieb (Phase 10b) muss sich nachvollziehen lassen, was die API tut: welche Anfragen langsam sind, welche Abfragen dahinterstehen, ob Benachrichtigungen und Webhooks ankommen. Hosting (DEC-002) ist offen; App Service und Container Apps sammeln Telemetrie über einen OpenTelemetry-Collector bzw. Azure Monitor, beide verstehen OTLP. Bisher schreibt die API Textlogs auf die Konsole.

Außerdem wendet jede Instanz beim Start die Migrationen an (nur außerhalb von Produktion, `PROJECTHUB_APPLY_MIGRATIONS`). Starten zwei Instanzen gleichzeitig, können beide dieselbe Migration ausführen.

## Entscheidung

### Telemetrie über OpenTelemetry und OTLP

- Traces, Metriken und Logs werden mit OpenTelemetry erfasst und per **OTLP** exportiert, **nur wenn `OTEL_EXPORTER_OTLP_ENDPOINT` gesetzt ist**. Ohne die Variable wird nichts gesammelt; es gibt keinen Hersteller-Exporter im Code. Die Standardvariablen (`OTEL_SERVICE_NAME`, `OTEL_EXPORTER_OTLP_HEADERS`, `OTEL_EXPORTER_OTLP_PROTOCOL` …) gelten; der Dienstname ist sonst `projecthub-api`.
- Traces: ASP.NET Core (ohne `/health`), HttpClient (Graph, Webex) und Npgsql (SQL-Anweisungen ohne Parameterwerte).
- Metriken: ASP.NET Core, HttpClient, .NET-Laufzeit, Npgsql und eigene Zähler im Meter `ProjectHub`:
  - `projecthub.notifications.delivered` mit `channel` (`email`, `webex`) und `outcome` (`sent`, `retry`, `failed`)
  - `projecthub.webhooks.received` mit `provider` und `outcome`
- Die Attribute eigener Metriken enthalten keine Personen, Projekte oder Inhalte, nur Kanäle, Anbieter und Ergebnisse.
- **Query-Strings verlassen den Prozess nie:** Hub-Verbindungen tragen das Zugriffstoken im Query-String (`access_token`). Das Attribut `url.query` wird deshalb aus jedem Server-Span entfernt.

### Logs

- Außerhalb von Development schreibt die API JSON auf die Konsole (UTC-Zeitstempel, Scopes). Die Scopes enthalten Trace- und Span-ID der Anfrage, damit Log und Trace zusammen auffindbar sind.
- Mit OTLP gehen die Logs zusätzlich an den Collector.
- Die bestehenden Regeln bleiben: keine Tokens, Secrets oder Inhalte in Logs. SQL-Anweisungen von EF Core werden nur ab `Warning` geloggt; in Traces stehen sie ohne Parameter.

### Migrationen unter Sperre

- `DatabaseMigrator` nimmt vor DbUp eine PostgreSQL-Advisory-Sperre auf einer eigenen Verbindung (`pg_advisory_lock(hashtextextended('projecthub:migrations', 0))`). Weitere Instanzen warten, bis die erste fertig ist, und finden danach nichts mehr zu tun. Die Sperre endet mit der Verbindung, auch wenn der Prozess abbricht.
- Am Prüfschritt für Produktionsmigrationen ändert sich nichts: `PROJECTHUB_APPLY_MIGRATIONS` bleibt in Produktion aus.

### Volltextsuche mit gespeichertem Suchvektor

Der Lasttest hat gezeigt, dass die Wissenssuche bei 50 gleichzeitigen Personen das langsamste Lesemuster ist: PostgreSQL berechnete `to_tsvector` für jede gefundene Zeile zweimal (Nachprüfung des Index und Ranking). Migration 011 speichert den Vektor als generierte Spalte `knowledge_article.search_vector` mit GIN-Index; der Ausdruck ist derselbe wie bisher (ADR 0006), Treffer und Reihenfolge ändern sich nicht.

## Alternativen

- **Application Insights SDK direkt:** bindet an Azure Monitor und an eine Hosting-Entscheidung, die noch offen ist. Azure Monitor nimmt OTLP über den Collector bzw. die Azure-Monitor-Distribution ebenfalls an.
- **Serilog für JSON-Logs:** zusätzliche Abhängigkeit für etwas, das der eingebaute JSON-Formatter kann.
- **Migrationen nur in einem eigenen Job:** sauberer für Produktion und bleibt dort der Weg (manueller Prüfschritt); die Sperre schützt Test- und Pilotumgebungen, in denen die Instanzen selbst migrieren.

## Konsequenzen

- Ohne Collector gibt es nur JSON-Logs. Wer Traces will, setzt `OTEL_EXPORTER_OTLP_ENDPOINT` (z. B. auf einen Collector-Sidecar).
- Neue eigene Metriken brauchen Attribute ohne personenbezogene Daten.
- Neue Endpunkte mit Geheimnissen im Query-String sind ausgeschlossen bzw. durch die Entfernung von `url.query` abgedeckt; Pfade dürfen keine Geheimnisse enthalten.
- Die Spalte `search_vector` wird von PostgreSQL gepflegt; die API schreibt sie nie.
