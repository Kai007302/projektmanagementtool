# AI PROMPT — Phase 4 Kanban

Du arbeitest an ProjectHub.

## Pflichtlektüre

- `AGENTS.md`
- `docs/ARCHITECTURE.md` (Abschnitt „Kanban Realtime“)
- `docs/adr/0003-realtime-boundaries.md`
- `docs/PERMISSIONS.md`
- Ergebnis von Phase 2

## Ziel

Ein Kanban-Board je Projekt als **Ansicht auf Tasks**. Es gibt kein zweites Task-Modell: Karten sind Tasks, die Spalte einer Karte ergibt sich aus dem Task-Status.

## Board und Spalten

```text
GET    /api/v1/projects/{projectId}/board
POST   /api/v1/projects/{projectId}/board/columns
PATCH  /api/v1/board-columns/{id}
POST   /api/v1/board-columns/{id}/move
DELETE /api/v1/board-columns/{id}
```

- Jedes Projekt hat genau ein Board. Es entsteht beim ersten Aufruf mit den Spalten „Offen“, „In Arbeit“ und „Erledigt“.
- Jede Spalte gehört zu einem Task-Status (`todo`, `in_progress`, `done`). Mehrere Spalten dürfen denselben Status haben.
- Jeder Status braucht mindestens eine Spalte.
- WIP-Limit pro Spalte ist ein Hinweis, keine Sperre.

## Karten verschieben

```text
POST   /api/v1/tasks/{id}/move   { version, columnId, index }
```

- Verschieben in eine Spalte setzt den Task-Status auf den Status der Spalte.
- Ändert sich der Status anders (z. B. per `PATCH /tasks/{id}`), landet die Karte in der ersten Spalte dieses Status.
- Das Board zeigt Tasks der obersten Ebene; Unteraufgaben erscheinen als Zähler auf der Karte.
- Gleichzeitige Verschiebungen auf einem Board werden serialisiert; ein veralteter Task-Stand ergibt `409 Conflict`.

## Realtime

```text
API -> DB-Transaktion -> Domain Event -> SignalR (Redis-Backplane) -> Clients
```

- Hub `/api/v1/hubs/projects`, Gruppe je Projekt, Beitritt nur mit View-Recht.
- Nachrichten enthalten nur die Projekt-ID; Clients laden die Daten über die autorisierte API nach.
- Ein Ausfall von Redis/SignalR darf keine Änderung rückgängig machen.

## Berechtigungen

- Board ansehen: View
- Karten verschieben: Contribute
- Spalten anlegen, ändern, sortieren, löschen: Edit

## Acceptance Criteria

1. Board eines Projekts öffnen
2. Karte per Drag & Drop in eine andere Spalte ziehen, Status ändert sich
3. Reihenfolge innerhalb einer Spalte ändern
4. Spalten anlegen, umbenennen, sortieren, löschen
5. Änderung erscheint bei anderen geöffneten Clients ohne Neuladen
6. Viewer sehen das Board, können aber nichts verschieben

Tests müssen Authorization, Statusabgleich, Reihenfolge, Concurrency und Realtime-Zustellung abdecken.
