# ADR 0006 — Zugriff auf Wissen und Suche

## Status
Accepted

## Entscheidung

- Veröffentlichtes Wissen ist organisationsweit lesbar; einzelne Artikel können auf `restricted` gestellt werden und sind dann nur über Freigaben (`knowledge_permission`) sichtbar (DEC-020, entschieden von Kai). Entwürfe und Artikel in Prüfung sind nie organisationsweit sichtbar.
- Jede Abfrage auf Wissen nimmt einen `KnowledgeReader` (Benutzer, Organisation, Org-Admin, Teams) und filtert in der Datenbankabfrage (`KnowledgeAccess.Visible`). Das gilt für Liste, Suche, Beziehungen, Verweise und alle späteren AI-Schnittstellen (umgesetzt als `IKnowledgeRetrieval` und die Werkzeuge in ADR 0015).
- Die erste Suche ist PostgreSQL-Volltextsuche (`german`) über Titel, Zusammenfassung und `knowledge_article.search_text`, den Klartext der aktuellen Version. Keine externe Suchmaschine, keine Vektordatenbank.
- Inhalte sind typisierte Blöcke als JSON, nie HTML. Das Backend prüft und normalisiert sie (`BlockContent`); Links und Bilder akzeptieren nur `http(s)` oder Pfade derselben Anwendung.

## Begründung

Wissen soll gefunden werden, ohne dass jede Person einzeln freigegeben werden muss; vertrauliche Inhalte brauchen trotzdem einen Weg, eingeschränkt zu bleiben. Wenn die Berechtigung Teil jeder Abfrage ist, können weder Suche noch eine spätere KI-Antwort Inhalte zeigen, die die Person nicht lesen darf. Volltextsuche in PostgreSQL reicht für den Start und braucht keine zusätzliche Infrastruktur.

## Konsequenz

- Migration 006 ergänzt `visibility`, `search_text` und den Suchindex. `search_text` wird bei jeder Speicherung und Wiederherstellung gesetzt.
- Semantische Suche und KI-Antworten kommen hinter den vorhandenen Schnittstellen, sobald der AI-Provider entschieden ist.
- Rechte im Detail: `docs/PERMISSIONS.md`.
