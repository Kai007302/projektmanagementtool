# AI PROMPT — Phase 7 Whiteboard

Du arbeitest an ProjectHub.

## Pflichtlektüre

- `AGENTS.md` (Abschnitt Realtime/Whiteboard)
- `docs/adr/0003-realtime-boundaries.md`, `docs/adr/0004-whiteboard-crdt.md`
- `docs/PERMISSIONS.md`
- Tabellen `whiteboard`, `whiteboard_snapshot`, `whiteboard_reference` (Migration 001)
- DEC-008 (Whiteboard Storage), DEC-022 (Wissensverweise auf Whiteboards)

## Ziel

Jedes Projekt bekommt Whiteboards, auf denen mehrere Personen gleichzeitig zeichnen. Das Whiteboard ist ein **kollaborativer Dokument-Layer**: Es kann auf Aufgaben verweisen, kopiert aber nie deren Titel, Status oder Zuständigkeit.

## Architektur (Vorschlag ADR 0009, DEC-008)

- Dokumente sind **Yjs**-Dokumente. Der Browser schickt nur **Änderungen (Yjs-Updates)**, nie den ganzen Zustand.
- Synchronisation läuft **im API-Server** über einen eigenen SignalR-Hub `/api/v1/hubs/whiteboards`. Redis verteilt Updates nur zwischen Instanzen (ADR 0003), dauerhaft ist allein PostgreSQL.
- Der Server prüft jedes Update (Rechte, Größe, Binärformat in eigenem C#-Code, Dekodierung mit YDotNet), schreibt es **vor** dem Verteilen in die neue Tabelle `whiteboard_update` (Migration 008) und schickt es an die anderen Teilnehmenden.
- **YDotNet läuft in einem isolierten Kindprozess**, weil manche kaputten Updates den Prozess beenden (per Fuzzing gefunden). Ein Absturz trifft nur diesen Helfer; das Update wird abgelehnt. Bricht ein Update das Dokument erst zusammen mit anderen, wird es beim Laden gefunden, entfernt und protokolliert.
- **Compaction:** Ein Hintergrunddienst fasst ruhige Whiteboards (oder solche mit vielen offenen Updates) zu einem **Snapshot** zusammen, legt ihn hinter `IWhiteboardSnapshotStorage` ab (lokal in Development, später Azure Blob) und löscht die eingeschlossenen Updates. Mehrere Instanzen stimmen sich über eine PostgreSQL-Advisory-Lock je Whiteboard ab.
- **Presence** (Mauszeiger, wer ist da) läuft als eigene, flüchtige Nachricht und wird nie gespeichert.
- Instanzen bleiben zustandslos: kein Dokument im Speicher über einen Aufruf hinaus.

## Dokumentstruktur

- `Y.Map` `objects`: je Objekt eine `Y.Map` mit `type`, `x`, `y`, `w`, `h`, `color`, `text` (bzw. `x2`, `y2` bei Pfeilen, `taskId` bei Aufgabenkarten).
- Typen: `sticky` (Notiz), `rect`, `ellipse`, `text`, `arrow`, `task`.
- Aufgabenkarten speichern nur `taskId`. Titel, Status und Zuständigkeit lädt der Client live über die API. Gelöschte oder unsichtbare Aufgaben erscheinen als „nicht verfügbar“.
- Felder überschreiben sich je Feld (letzter gewinnt); gleichzeitige Änderungen an verschiedenen Feldern oder Objekten gehen nie verloren.

## API

```text
GET    /api/v1/projects/{projectId}/whiteboards
POST   /api/v1/projects/{projectId}/whiteboards     { name }
GET    /api/v1/whiteboards/{id}
PATCH  /api/v1/whiteboards/{id}                     { version, name }
DELETE /api/v1/whiteboards/{id}
GET    /api/v1/whiteboards/{id}/tasks?ids=…         (live Daten der Aufgabenkarten, höchstens 100)
GET    /api/v1/tasks/{taskId}/whiteboards           (Whiteboards, auf denen die Aufgabe liegt)
```

Hub `/api/v1/hubs/whiteboards`:

- `Join(whiteboardId)` → aktueller Zustand (ein Yjs-Update) und ob man zeichnen darf
- `PushUpdate(whiteboardId, update)` → speichern und an die anderen verteilen (`Update`)
- `UpdatePresence(whiteboardId, cursor)` → an die anderen (`Presence`, `PresenceLeft`)
- `Leave(whiteboardId)`
- Anlegen, Umbenennen und Löschen melden sich über den Projekt-Hub (Bereich `whiteboards`); offene Ansichten laden die Liste nach.

## Aufgabenverweise

- `whiteboard_reference` hält fest, welche Aufgabe auf welchem Whiteboard liegt. Der Server leitet das bei der Compaction aus dem Dokument ab (kurz verzögert). Verweise auf Aufgaben anderer Projekte werden nicht übernommen.

## Wissensverweise (DEC-022)

- Wissensartikel können jetzt auf Whiteboards verweisen. Sichtbar ist der Verweis nur, wer das Whiteboard sehen darf.
- Die Aufgabendetails zeigen, auf welchen Whiteboards die Aufgabe liegt.

## Berechtigungen (Vorschlag DEC-027)

- Whiteboards ansehen und Presence: View
- Zeichnen (Updates schicken): Contribute
- Anlegen, umbenennen, löschen: Edit

## Frontend

- Ansicht „Whiteboard“ neben Board, Liste und Gantt: Liste der Whiteboards, anlegen, umbenennen, löschen.
- Zeichenfläche (SVG) mit Werkzeugen Auswahl, Notiz, Rechteck, Ellipse, Text, Pfeil, Aufgabe.
- Verschieben, Größe ändern, Text bearbeiten, löschen, Rückgängig/Wiederholen (nur eigene Änderungen), Verschieben der Ansicht und Zoom.
- Mauszeiger und Namen der anderen, Liste „Gerade dabei“.
- **Barrierefreie Alternative:** Objektliste mit Auswahl, Formular für Text, Farbe, Lage und Größe; Pfeiltasten verschieben das gewählte Objekt.
- Viewer sehen alles live, können aber nichts ändern.

## Acceptance Criteria

1. Whiteboard anlegen, umbenennen, löschen
2. Zwei Personen zeichnen gleichzeitig, beide sehen die Änderungen ohne Neuladen
3. Nach Neuladen und nach Compaction ist der Inhalt vollständig da
4. Aufgabenkarte zeigt live Titel und Status der Aufgabe, ohne sie zu kopieren
5. Viewer können nicht zeichnen; der Server lehnt ihre Updates ab
6. Ungültige oder zu große Updates werden abgelehnt
7. Mauszeiger der anderen erscheinen und verschwinden beim Verlassen

Tests müssen Authorization, Persistenz, Compaction, Ablehnung ungültiger Updates, Verweise, Realtime zwischen zwei Clients und den E2E-Ablauf abdecken.
