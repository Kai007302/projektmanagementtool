# Betrieb

Betriebshandbuch für ProjectHub im Pilotbetrieb. Hosting (DEC-002) und Region (DEC-001) sind offen; alles hier gilt für App Service und Container Apps gleichermaßen. Auslieferung und HTTP-Härtung: ADR 0012. Telemetrie, Logs und Migrationssperre: ADR 0013.

## Aufbau

- **Web-Container** (Nginx, ohne Root, Port 8080): liefert die Oberfläche aus und leitet `/api` (einschließlich WebSockets) und `/health` an die API weiter. Einzige öffentlich erreichbare Komponente hinter dem Ingress (TLS).
- **API-Container** (ASP.NET Core, ohne Root, Port 8080): nur im internen Netz erreichbar. Startet die Whiteboard-Engine selbst als Kindprozess (ADR 0009).
- **PostgreSQL 18**: führende Datenquelle für alles.
- **Redis**: nur Realtime-Verteilung zwischen API-Instanzen (SignalR, Whiteboard), kein dauerhafter Zustand (ADR 0003).
- **Dateien**: Anhänge und Whiteboard-Snapshots liegen bis zu Blob Storage (DEC-017) unter `/data` im API-Container (Volume, von allen Instanzen geteilt).

## Konfiguration

Alle Werte kommen aus Umgebungsvariablen bzw. dem Secret Store, nie aus dem Repository. Vollständige Liste mit Beispielen: `.env.example`.

| Variable | Zweck | Produktion |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` außerhalb der Entwicklung | `Production` |
| `PROJECTHUB_DB_CONNECTION` | PostgreSQL | Secret Store; `GSS Encryption Mode=Disable` spart Fehlversuche, wenn kein Kerberos im Spiel ist |
| `REDIS_CONNECTION` | Redis | Secret Store, TLS |
| `ENTRA_TENANT_ID`, `ENTRA_CLIENT_ID` | Anmeldung (Entra ID) | Pflicht |
| `PROJECTHUB_APPLY_MIGRATIONS` | Migrationen beim Start | **immer `false`**: Migrationen laufen nach Prüfung getrennt (siehe unten) |
| `PROJECTHUB_SEED_DEVELOPMENT_DATA` | synthetische Daten | `false` (außerhalb von Development ohne Wirkung) |
| `PROJECTHUB_APP_URL`, `PROJECTHUB_PUBLIC_API_URL` | Links in Mails, Kalender, Webex-Webhooks | öffentliche Adresse |
| `PROJECTHUB_TRUST_FORWARDED_HEADERS` | Client-Adresse und Schema vom Proxy | `true`, nur wenn die API ausschließlich über den Web-Container erreichbar ist |
| `PROJECTHUB_RATE_LIMIT_PER_MINUTE`, `…_UPLOAD_…`, `…_WEBHOOK_…` | Rate Limits je Instanz (DEC-030) | Startwerte 600 / 30 / 120 |
| `PROJECTHUB_MAX_REQUEST_BYTES`, `PROJECTHUB_ATTACHMENT_MAX_BYTES` | Größenlimits | 4 MB / 100 MB |
| `PROJECTHUB_ATTACHMENT_DIRECTORY`, `PROJECTHUB_WHITEBOARD_DIRECTORY` | Dateien | unter `/data` (Volume) |
| `PROJECTHUB_MAIL_TRANSPORT` und `MICROSOFT_GRAPH_*` | Mailversand über Graph (ADR 0010) | `graph`, Managed Identity |
| `PROJECTHUB_WEBEX_TRANSPORT`, `WEBEX_BOT_TOKEN`, `WEBEX_WEBHOOK_SECRET` | Webex-Bot (ADR 0011) | `bot` oder `off`; Secrets im Secret Store |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Traces, Metriken, Logs per OTLP (ADR 0013) | Adresse des Collectors; ohne die Variable nur JSON-Logs |
| `OTEL_SERVICE_NAME`, `OTEL_EXPORTER_OTLP_HEADERS` | Dienstname, Zugang zum Collector | optional |

## Probes

| Pfad | Bedeutung | Verwendung |
|---|---|---|
| `/health/live` | Prozess läuft, keine Abhängigkeiten geprüft | Liveness: Neustart nur, wenn dieser Pfad nicht antwortet |
| `/health/ready` | PostgreSQL und Redis erreichbar (je 3 s Zeitlimit) | Readiness: Instanz bekommt nur Anfragen, solange 200 |

Beide Pfade sind ohne Anmeldung erreichbar, zählen nicht für Rate Limits und erzeugen keine Traces. Der Web-Container leitet sie weiter; der Ingress prüft also den ganzen Weg. Readiness nicht als Liveness verwenden: ein kurzer Datenbankausfall soll Instanzen aus der Verteilung nehmen, nicht neu starten.

## Logs und Telemetrie

- Logs: JSON auf stdout (eine Zeile je Eintrag, UTC-Zeit), mit `TraceId` und `SpanId` im Scope der Anfrage. Die Plattform sammelt stdout ein.
- Mit `OTEL_EXPORTER_OTLP_ENDPOINT`: Traces (Anfragen, ausgehende HTTP-Aufrufe zu Graph/Webex, SQL ohne Parameter), Metriken und Logs an den Collector. Query-Strings werden nie exportiert (Zugriffstoken der Hubs).
- Eigene Metriken (Meter `ProjectHub`), die sich für Alarme eignen:
  - `projecthub.notifications.delivered{outcome="failed"}` steigt: Mails oder Webex-Nachrichten gehen endgültig nicht raus (Graph-Berechtigung, abgelaufenes Secret, Webex-Token).
  - `projecthub.notifications.delivered{outcome="retry"}` dauerhaft hoch: Graph oder Webex drosselt oder ist gestört.
  - `projecthub.webhooks.received{outcome="unauthorized"}` steigt: falsches `WEBEX_WEBHOOK_SECRET` oder fremde Aufrufe.
- Weitere sinnvolle Alarme: Readiness länger als 2 Minuten rot, 5xx-Anteil über 1 %, p95 von `http.server.request.duration` über 300 ms (DEC-032).

## Skalierung

- API-Instanzen sind zustandslos; geteilter Zustand liegt in PostgreSQL, Redis und den Dateien (ADR 0001, ARCHITECTURE.md). Mehrere Instanzen brauchen dasselbe Redis und dasselbe `/data` (bis Blob Storage kommt).
- WebSockets: Die Hubs handeln die Verbindung zuerst per HTTP aus und bauen sie dann auf. Beide Anfragen müssen dieselbe Instanz erreichen; bei mehr als einer Instanz deshalb **Session-Affinität** am Ingress einschalten (App Service: ARR Affinity, Container Apps: Sticky Sessions). Der Ingress muss WebSockets durchreichen.
- Rate Limits zählen je Instanz (DEC-030): Bei n Instanzen kommt eine Person auf bis zu n × Limit.
- Ausgangspunkt nach dem Lasttest: eine Instanz mit 1 bis 2 vCPU trägt die Lesepfade von 50 gleichzeitig aktiven Personen mit Reserve (siehe unten). Zwei Instanzen für Ausfallsicherheit, horizontal nach CPU skalieren.
- PostgreSQL-Verbindungen: Npgsql poolt je Instanz (Standard bis 100). Instanzen × Pool muss unter `max_connections` des Servers bleiben; sonst `Maximum Pool Size` in der Verbindungszeichenfolge senken.

## Migrationen

- Produktion: Migrationen werden **vor** dem Rollout geprüft (Code-Review der SQL-Datei unter `database/migrations/`) und dann bewusst angewendet, z. B. durch einen einmaligen Start der neuen API-Version mit `PROJECTHUB_APPLY_MIGRATIONS=true` gegen die Produktionsdatenbank als eigener Job. Die laufenden Instanzen haben die Variable nie gesetzt.
- Starten mehrere Instanzen mit `PROJECTHUB_APPLY_MIGRATIONS=true` gleichzeitig (Test, Pilot), wartet jede auf eine PostgreSQL-Advisory-Sperre; jede Migration wird genau einmal angewendet (ADR 0013).
- Migrationen sind nur vorwärts. Ein Rückweg ist eine neue Migration oder eine Wiederherstellung (siehe unten).

## Backup und Wiederherstellung

Vorschlag für DEC-010: **RPO 15 Minuten, RTO 2 Stunden.**

### Was gesichert wird

| Daten | Sicherung | erreichbares RPO |
|---|---|---|
| PostgreSQL | Point-in-Time-Restore der Plattform (Azure Database for PostgreSQL Flexible Server: tägliche Snapshots und fortlaufendes WAL-Archiv, Aufbewahrung 35 Tage), georedundante Sicherung je nach DEC-001 | Minuten |
| Anhänge und Whiteboard-Snapshots (`/data`, später Blob Storage) | Snapshot des Volumes bzw. Blob-Versionierung mit Soft Delete; mindestens alle 15 Minuten | 15 Minuten |
| Redis | keine Sicherung: nur flüchtige Realtime-Nachrichten | – |
| Konfiguration und Secrets | Key Vault mit Soft Delete und Löschschutz; Infrastruktur als Code | – |

### Reihenfolge bei einer Wiederherstellung

1. Ursache und Zeitpunkt festlegen (letzter guter Stand). Schreibzugriffe stoppen: Web-Container auf 0 Instanzen oder Wartungsseite am Ingress.
2. PostgreSQL auf einen **neuen** Server zum gewählten Zeitpunkt wiederherstellen (nicht den alten überschreiben; er bleibt für die Analyse).
3. Dateien wiederherstellen: **nicht älter als der Datenbankstand**. Neuere Dateien schaden nicht (verwaiste Anhänge; Whiteboard-Updates lassen sich mehrfach anwenden), ältere verlieren Whiteboard-Inhalte, die nach dem Snapshot nur noch in der Datei standen, und Anhänge, auf die die Datenbank verweist.
4. `PROJECTHUB_DB_CONNECTION` der API auf den neuen Server stellen und starten. `/health/ready` muss 200 liefern.
5. Stichprobe: ein Projekt mit Board, Gantt und Whiteboard öffnen, einen Anhang herunterladen, einen Wissensartikel lesen.
6. Ausgehende Nachrichten prüfen: Die Outbox (`mail_outbox`) enthält nach der Wiederherstellung Einträge, die vor dem Ausfall schon versendet waren. Bei einem Rücksprung von mehr als einigen Minuten die offenen Einträge vor dem Start der API in der Datenbank ansehen (`select channel, created_at from mail_outbox where status = 'pending'`) und bewusst entscheiden, ob sie noch rausgehen sollen; sonst bekommen Personen Benachrichtigungen doppelt. Dafür gibt es keinen Endpunkt.
7. Web-Container wieder hochfahren, Ausfall und Datenverlust (Zeitfenster) kommunizieren.

### Wiederherstellungsübung

Vor dem Pilotbetrieb und danach halbjährlich, in einer Testumgebung mit synthetischen Daten:

1. Zeitpunkt notieren, danach einige Änderungen machen (Aufgabe, Whiteboard-Strich, Anhang).
2. Schritte 2 bis 5 oben gegen eine Kopie ausführen, Zeit stoppen.
3. Erwartung: Stand des notierten Zeitpunkts, spätere Änderungen fehlen, keine Fehler in den Logs, Dauer unter dem RTO.
4. Ergebnis, Dauer und Abweichungen im Betriebsprotokoll festhalten; das Handbuch anpassen.

## Ausfälle von Abhängigkeiten

Grundregel: Kerntransaktionen hängen nicht von externen Diensten ab. Benachrichtigungen laufen über die Outbox und werden nach dem Speichern zugestellt.

| Ausfall | Wirkung | Was zu tun ist |
|---|---|---|
| **PostgreSQL** | Readiness rot, alle Instanzen bekommen keine Anfragen mehr. Nichts geht verloren, was bestätigt wurde. | Plattform-Failover bzw. Wiederherstellung. Instanzen werden von selbst wieder bereit. |
| **Redis** | Readiness rot: Die Plattform nimmt die Instanzen aus der Verteilung, die App ist nicht erreichbar, obwohl Speichern ohne Redis ginge (Realtime ist Best Effort). Live-Aktualisierungen und das gemeinsame Zeichnen über mehrere Instanzen fallen aus. | Redis wiederherstellen; SignalR verbindet sich neu, die Oberfläche lädt nach dem Wiederverbinden nach. Siehe offene Frage unten. |
| **Microsoft Graph** | Mails bleiben in der Outbox; Wiederholung mit wachsendem Abstand (30 s bis 1 h, `Retry-After` wird beachtet), nach 8 Versuchen „fehlgeschlagen“. Kalender-Feeds sind nicht betroffen. | Metrik `outcome="failed"` beobachten; nach der Störung fehlgeschlagene Mails unter `/api/v1/admin/mail-outbox` erneut anstoßen. |
| **Webex** | Nachrichten an Räume gehen wie Mails über die Outbox (Kanal `webex`) und werden wiederholt. Ein neuer Webex-Raum für ein Projekt lässt sich nicht anlegen (Meldung „nicht verfügbar“); Besprechungslinks, Projekte und Aufgaben sind nicht betroffen. Webhooks kommen erst wieder, wenn Webex sie zustellt. | Wie Graph. Fehlende Webhook-Ereignisse sind nur Hinweise im Projekt; nichts muss nachgeholt werden. |
| **Entra ID** | Neue Anmeldungen schlagen fehl; bestehende Token gelten bis zu ihrem Ablauf. | Abwarten; nichts in ProjectHub zu tun. |
| **Dateien (`/data`)** | Uploads und Downloads schlagen fehl, Whiteboards lassen sich nicht laden, wenn ihr Snapshot fehlt. | Volume wieder einhängen bzw. wiederherstellen. |

**Offene Frage für den Pilotbetrieb:** Readiness prüft Redis seit Phase 0. Fällt Redis aus, ist damit die ganze App weg, obwohl nur Realtime betroffen wäre. Alternative: Redis in der Readiness nur als „beeinträchtigt“ melden (200) und den Ausfall über Alarme sichtbar machen. Vorschlag DEC-033; bis dahin bleibt es, wie es ist.

## Lasttest

`tests/load/read-paths.js` (k6) misst die häufigsten Lesepfade gegen eine API in Development mit synthetischen Benutzern; nie gegen Produktionsdaten.

```bash
# API mit abgeschalteten Rate Limits und ohne SQL-Logs (Release, wie im Betrieb)
PROJECTHUB_RATE_LIMIT_PER_MINUTE=0 \
  env 'Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command=Warning' \
  dotnet run --project src/ProjectHub.Api --launch-profile http -c Release

# Im Repository-Wurzelverzeichnis
docker run --rm --network host -v "$PWD/tests/load:/load" grafana/k6 run /load/read-paths.js
```

Variablen: `API_URL` (Standard `http://localhost:5080`), `TASKS` (1000), `ARTICLES` (300), `VUS` (50), `DURATION` (1m). `setup()` legt ein Projekt mit 1000 Aufgaben (ein Viertel mit Terminen und übergeordneter Aufgabe) und 300 Wissensartikel an; jeder Lauf erzeugt neue Daten in der Entwicklungsdatenbank.

**Zielwerte (Vorschlag DEC-032), je API-Instanz:** p95 unter 300 ms je Lesepfad, unter 1 % Fehler.

**Ergebnis** (2026-10-04, API, PostgreSQL und k6 auf einer Maschine mit 4 vCPU, 50 gleichzeitige Personen ohne Pause, 1 Minute):

| Lesepfad | p95 vorher | p95 nachher |
|---|---|---|
| Projektliste | 72 ms | 72 ms |
| Kanban-Board (1000 Karten) | 235 ms | 190 ms |
| Gantt (1000 Aufgaben) | 178 ms | 154 ms |
| Aufgabenliste (100) | 118 ms | 105 ms |
| Aktivität | 88 ms | 86 ms |
| ungelesene Benachrichtigungen | 66 ms | 68 ms |
| Wissensartikel (50) | 121 ms | 116 ms |
| **Wissenssuche** | **451 ms** | **117 ms** |
| Wissensgraph | 108 ms | 117 ms |

Fehlerquote 0 %, Durchsatz von 515 auf 617 Anfragen/s. Behobener Engpass: Die Volltextsuche berechnete den Suchvektor für jeden Treffer zweimal; er ist jetzt als generierte Spalte gespeichert und indiziert (Migration 011, ADR 0013). Beim zweiten Lauf lagen doppelt so viele Artikel in der Datenbank (600), die Suche blieb trotzdem unter dem Ziel.
