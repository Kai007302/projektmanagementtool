# AI PROMPT — Phase 5 Gantt

Du arbeitest an ProjectHub.

## Pflichtlektüre

- `AGENTS.md`
- `docs/DATA_MODEL.md` (Tabellen `task`, `task_dependency`, `gantt_milestone`)
- `docs/PERMISSIONS.md`
- `docs/adr/0003-realtime-boundaries.md`
- Ergebnis von Phase 2 und Phase 4

## Ziel

Ein Gantt-Diagramm je Projekt als **Ansicht auf Tasks**, genau wie das Kanban-Board. Balken sind Tasks; ihre Lage ergibt sich aus `start_date` und `due_date`. Es gibt keine zweite Terminquelle.

## API

```text
GET    /api/v1/projects/{projectId}/gantt
POST   /api/v1/projects/{projectId}/gantt/dependencies   { sourceTaskId, targetTaskId, dependencyType }
DELETE /api/v1/task-dependencies/{id}
POST   /api/v1/projects/{projectId}/gantt/milestones     { name, date }
PATCH  /api/v1/gantt-milestones/{id}                     { version, name?, date? }
DELETE /api/v1/gantt-milestones/{id}
```

- `GET .../gantt` liefert alle aktiven Tasks des Projekts (mit Hierarchie, Termin, Fortschritt, Version), die Abhängigkeiten zwischen aktiven Tasks und die Meilensteine.
- Termine verschieben und Dauer ändern läuft über das bestehende `PATCH /api/v1/tasks/{id}` mit `version` (`startDate`, `dueDate`). Ein veralteter Stand ergibt `409`.
- Ein Task mit nur einem Datum wird als eintägiger Balken gezeigt; Tasks ohne Datum stehen als Zeile „ohne Termin“ an ihrem Platz in der Gliederung und lassen sich über das Terminformular einplanen.

## Abhängigkeiten

- Typen: `finish_to_start` (Standard), `start_to_start`, `finish_to_finish`, `start_to_finish`.
- Beide Tasks gehören zum selben Projekt. Projektübergreifende Abhängigkeiten sind nicht Teil dieser Phase.
- Zwischen zwei Tasks gibt es höchstens eine Abhängigkeit (sonst `409`).
- Keine Zyklen (`409`), keine Abhängigkeit zwischen einem Task und seinen eigenen Unteraufgaben (`400`).
- **Annahme (DEC-023):** Abhängigkeiten planen nicht automatisch um. Die API markiert verletzte Abhängigkeiten (`violated`), das Diagramm zeigt sie als Warnung. Automatisches Verschieben von Nachfolgern kann später kommen.
- Tage zählen inklusiv: ein Task belegt die Tage `start` bis `due`. `finish_to_start` ist verletzt, wenn der Nachfolger am oder vor dem Endtag des Vorgängers beginnt.

## Meilensteine

- Name (1–200 Zeichen) und Datum, versioniert, gehören zum Projekt.
- Darstellung als Raute auf der Zeitachse.

## Testdaten

- Die Testdaten bekommen Termine relativ zum Tag des Seedings, zwei Abhängigkeiten und den Meilenstein „Go-live Intranet“.

## Realtime

- Neuer Bereich `gantt` in `ProjectContentChanged` für Abhängigkeiten und Meilensteine; Terminänderungen an Tasks laufen weiter als `tasks`.
- Das Diagramm lädt bei jeder Änderung im Projekt über die API nach.

## Berechtigungen (Vorschlag DEC-024)

- Gantt ansehen: View
- Termine verschieben, Abhängigkeiten anlegen und löschen: Contribute (wie Tasks ändern)
- Meilensteine anlegen, ändern, löschen: Edit (Projektstruktur)

## Frontend

- Ansicht „Gantt“ neben „Board“ und „Liste“.
- Zeitachse mit Zoom Tag/Woche/Monat, Linie für heute, Wochenenden hervorgehoben.
- Balken mit Fortschritt, Unteraufgaben eingerückt unter ihrem Eltern-Task.
- Ziehen verschiebt einen Balken, Ziehen am rechten Rand ändert das Enddatum.
- Tastatur- und Screenreader-Alternative: Auswahl eines Tasks öffnet ein Formular mit Start- und Enddatum; die Pfeiltasten verschieben den gewählten Balken um einen Tag.
- Abhängigkeiten als Pfeile, verletzte rot; Liste der Abhängigkeiten mit Löschen.
- Viewer sehen alles, können aber nichts ändern.

## Acceptance Criteria

1. Gantt eines Projekts öffnen, Balken stehen auf den Terminen der Tasks
2. Balken ziehen ändert Start und Ende, Randziehen ändert nur das Ende
3. Dasselbe per Tastatur und Formular
4. Abhängigkeit anlegen, Zyklus wird abgelehnt, Verletzung wird angezeigt
5. Meilenstein anlegen, umbenennen, verschieben, löschen
6. Änderungen erscheinen bei anderen geöffneten Clients ohne Neuladen
7. Viewer sehen das Diagramm, können aber nichts ändern

Tests müssen Authorization, Zyklus- und Duplikatprüfung, Verletzungsregeln, Concurrency (Version) und den E2E-Ablauf abdecken.
