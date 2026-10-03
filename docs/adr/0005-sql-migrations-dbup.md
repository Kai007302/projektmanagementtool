# ADR 0005 — Versionierte SQL-Migrationen mit DbUp

## Status
Accepted

## Entscheidung

Das Schema wird ausschließlich über die handgeschriebenen SQL-Skripte unter `database/migrations/` gepflegt. Die API bettet die Skripte ein und wendet sie mit DbUp in Namensreihenfolge an. Bereits angewendete Skripte stehen in der Tabelle `schemaversions` und laufen nie erneut.

Automatisches Anwenden beim Start ist nur aktiv, wenn `PROJECTHUB_APPLY_MIGRATIONS=true` gesetzt ist (Development, Tests). Produktionsmigrationen bleiben ein Human Review Gate.

## Begründung

Das Startschema existiert bereits als SQL mit PostgreSQL-18-spezifischen Features (`uuidv7()`, zusammengesetzte Org-Fremdschlüssel). Parallel gepflegte EF-Core-Migrationen würden eine zweite Quelle der Wahrheit schaffen.

## Konsequenz

- Neue Schemaänderungen sind neue, fortlaufend nummerierte SQL-Dateien. Bestehende Skripte werden nie verändert.
- `docker-compose.yml` lädt keine Skripte mehr per `docker-entrypoint-initdb.d`; das erledigt die API.
- Ein späterer Datenzugriff mit EF Core mappt auf dieses Schema, erzeugt es aber nicht.
