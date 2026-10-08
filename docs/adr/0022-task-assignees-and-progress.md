# ADR 0022 — Mehrere Zuständige und berechneter Fortschritt

## Status
Proposed

## Kontext

Kai hat am 2026-10-08 gewünscht, dass man in Aufgabenkarten den Fortschritt nicht mehr von Hand ändern kann und Aufgaben mehreren Personen zuordnen kann. Bisher hatte eine Aufgabe genau eine zuständige Person (`task.assignee_id`) und einen frei eingetragenen Fortschritt (`task.progress`, 0 bis 100).

## Entscheidung

### Mehrere Zuständige

- Tabelle `task_assignee` (Migration 017) mit `task_id`, `user_id` und `organization_id`; Primärschlüssel aus Aufgabe und Person. Sie ersetzt `task.assignee_id`. Die Migration übernimmt die bisherige zuständige Person jeder Aufgabe, danach entfällt die Spalte.
- Höchstens 20 Personen je Aufgabe. Wer neu hinzukommt, muss im Projekt mitarbeiten dürfen (`CanBeAssignedAsync`, wie bisher). Wer schon zuständig ist und sein Recht verloren hat, bleibt stehen, bis ihn jemand entfernt.
- API: `TaskResponse.assignees` (`id`, `displayName`, nach Name sortiert) statt `assigneeId`/`assigneeName`. Anlegen mit `assigneeIds`, ändern mit `PATCH … { assigneeIds: [...] }`; die Liste ersetzt alle Zuständigen. `assigneeId` lehnt die API mit 400 ab, damit ein alter Client nicht stillschweigend nichts ändert. Der Filter `?assigneeId=` findet Aufgaben, an denen die Person beteiligt ist.
- Kanban-Karten liefern ebenfalls `assignees`, Gantt und Whiteboard `assigneeNames`.
- Jede neu hinzugefügte Person bekommt die Benachrichtigung `task_assigned`; wer schon zuständig war, nicht noch einmal.
- Kalender-Abo, Datenauskunft und Anonymisierung (ADR 0017, 0018) gelten für jede beteiligte Person. Die Anonymisierung entfernt die Person aus allen Aufgaben; andere Zuständige bleiben.
- Export und Import (ADR 0018): mehrere Personen in einer Zelle, getrennt mit „;“.

### Berechneter Fortschritt

- Eine Aufgabe ohne Unteraufgaben steht nach Status: Offen 0 %, In Arbeit 50 %, Erledigt 100 %.
- Eine Aufgabe mit Unteraufgaben steht bei 100 %, wenn sie selbst erledigt ist, sonst beim gerundeten Mittel ihrer (nicht gelöschten) Unteraufgaben, rekursiv.
- Der Wert bleibt in `task.progress`, damit Gantt, Kanban, PDF, Export und Assistent ihn wie bisher lesen. Der Server rechnet ihn neu, wenn sich Status oder Hierarchie ändern: beim Anlegen, Ändern von Status oder übergeordneter Aufgabe, Löschen und Verschieben auf dem Board, in derselben Transaktion. Die `version` der Aufgabe ändert sich dadurch nicht, weil niemand sie bearbeitet hat; sonst bekäme jemand, der gerade die Oberaufgabe bearbeitet, einen Konflikt.
- `progress` beim Anlegen wird nicht mehr gelesen, `PATCH … { progress }` lehnt die API mit 400 ab. Der Import liest die Spalte „Fortschritt (%)“ nicht mehr und meldet sie als ignoriert; der Export schreibt sie weiter.
- Die Migration rechnet den Fortschritt aller bestehenden Aufgaben einmal nach dieser Regel; von Hand eingetragene Werte gehen dabei verloren.

## Konsequenzen

- Der Fortschritt einer Aufgabe in Arbeit ist grob (50 %), bis sie Unteraufgaben hat. Wer feiner planen will, teilt die Aufgabe auf. Die Regel steht als DEC-045 zur Entscheidung.
- Das Neuberechnen liest je Änderung die Aufgaben eines Projekts (Id, übergeordnete Aufgabe, Status, Fortschritt) und schreibt nur geänderte Werte. Bei bis zu einigen tausend Aufgaben je Projekt ist das unkritisch.
- Clients, die `assigneeId` oder `progress` senden, müssen umgestellt werden; MCP-Werkzeuge: `create_task` nimmt `assigneeIds`, `update_task` `addAssigneeIds`, `removeAssigneeIds` und `unassign`, `progress` entfällt.
- Offen für Kai: diese ADR freigeben.
