# ADR 0009 — Whiteboard-Synchronisation im API-Server

## Status
Accepted (Kai, 2026-10-03: „Im API-Server“)

## Kontext

ADR 0004 legt fest: Whiteboards sind Yjs-Dokumente, kein Broadcast ganzer Zustände, Presence getrennt vom Dokument, Snapshots/Compaction sind Pflicht. Offen war, **wo** die Synchronisation läuft (DEC-008) und wie die Daten in PostgreSQL und im Blob-Speicher liegen. Migration 001 enthält bereits `whiteboard`, `whiteboard_snapshot` (mit `storage_key`) und `whiteboard_reference`.

Betrachtete Optionen:

1. **Im API-Server** (.NET): eigener SignalR-Hub, Yjs-Updates werden mit [YDotNet](https://github.com/y-crdt/ydotnet) (Bindings an `yrs`, die Rust-Implementierung von Yjs) geprüft und zusammengeführt.
2. **Eigener Node-Dienst** mit Hocuspocus/y-websocket: das Original-Ökosystem, aber ein zweiter Dienst mit eigener Authentifizierung, eigenem Deployment und eigener Rechteprüfung gegen die API.

Ein Spike hat gezeigt, dass YDotNet 0.6 Updates aus dem Browser (Yjs 13) annimmt und einen zusammengeführten Zustand liefert, den der Browser wieder lesen kann. **Fuzzing hat aber gezeigt, dass yrs bei manchen kaputten Updates den ganzen Prozess beendet** (Rust-Panic über die C-Schnittstelle, teils Segmentation Fault), auch bei Updates, die formal korrekt aussehen und erst zusammen mit dem vorhandenen Dokument scheitern.

## Entscheidung

Option 1, die Synchronisation läuft im API-Server.

- **Transport:** Hub `/api/v1/hubs/whiteboards` (gleiche Anmeldung wie die übrigen Hubs). Updates werden als Base64 übertragen. Redis ist nur Backplane für das Verteilen zwischen Instanzen (ADR 0003).
- **Isolierte Engine:** Alles Dekodieren mit yrs läuft in einem Kindprozess (`WhiteboardEngine`, die API-Assembly selbst mit `--whiteboard-engine`, Kommunikation über stdin/stdout). Stürzt er ab, wird er beim nächsten Aufruf neu gestartet und die Eingabe gilt als ungültig. Die API selbst lädt yrs nie.
- **Formatprüfung vorab:** `YjsUpdateValidator` prüft das Binärformat in verwaltetem Code, bevor etwas an yrs geht. Er ist strenger als Yjs: nur die Wurzel `objects`, verschachtelte Maps, einfache Werte und Löschungen. Zufällige Bytes erreichen so nie nativen Code.
- **Schreiben:** `PushUpdate` prüft Contribute, Größe (höchstens 256 KB je Update), das Format und dass die Engine das Update dekodieren kann. Dann wird es mit fortlaufender Nummer je Whiteboard in `whiteboard_update` gespeichert (Migration 008, Advisory Lock je Whiteboard) und **erst danach** an die anderen verteilt. Ein Redis-Ausfall verliert also nichts, andere sehen die Änderung spätestens beim nächsten Laden.
- **Lesen:** `Join` nimmt die Verbindung zuerst in die Gruppe auf und lädt dann Snapshot plus alle späteren Updates. So geht nichts zwischen Laden und Gruppenbeitritt verloren; doppelt ankommende Updates sind für Yjs harmlos. Der Browser schickt anschließend nur, was ihm voraus ist (Offline-Änderungen nach einem Reconnect).
- **Compaction:** `WhiteboardCompactionService` (Hintergrunddienst in jeder Instanz) sucht Whiteboards mit offenen Updates, die seit einigen Sekunden ruhig sind oder sehr viele Updates haben. Unter `pg_try_advisory_xact_lock` je Whiteboard wird der Zustand zusammengeführt, als neuer Snapshot über `IWhiteboardSnapshotStorage` abgelegt (Development: lokales Verzeichnis, produktiv Azure Blob wie DEC-017), in `whiteboard_snapshot` eingetragen und die eingeschlossenen Updates werden gelöscht. Die letzten zwei Snapshots bleiben erhalten.
- **Giftige Updates:** Scheitert das Zusammenführen beim Laden oder bei der Compaction, sucht der Server per Bisektion das erste Update, das das Dokument bricht, löscht es und protokolliert Whiteboard, Nummer und Person. So kann ein einzelnes Update ein Whiteboard nie dauerhaft unbenutzbar machen.
- **Aufgabenverweise:** Bei der Compaction liest der Server die Objekte vom Typ `task` aus dem Dokument und gleicht `whiteboard_reference` ab. Nur aktive Aufgaben desselben Projekts zählen. Die Karte speichert nur `taskId`; Titel und Status lädt der Client über die API (keine Duplizierung, AGENTS.md).
- **Presence:** `UpdatePresence` geht nur an die Gruppe, mit Name und Farbe vom Server; nichts davon wird gespeichert. Beim Verlassen oder Trennen geht `PresenceLeft` an die Gruppe.
- **Zustandslos:** Keine Instanz hält Dokumente im Speicher. Zwischen Aufrufen merkt sich eine Verbindung nur, welchen Whiteboards sie beigetreten ist (für `PresenceLeft`).

## Konsequenzen

- Ein nativer Baustein (`yrs` über YDotNet.Native) liegt im API-Image, läuft aber nur im Kindprozess. Container brauchen die passende Native-Bibliothek (Linux, Windows und macOS sind im Paket) und müssen Kindprozesse starten dürfen.
- Ein Whiteboard-Aufruf wartet je Instanz auf die eine Engine. Bei viel Last kann daraus ein kleiner Pool werden.
- Ein Update, das yrs erst zusammen mit späteren Updates bricht, kann bis zum nächsten Laden in Browsern ankommen. Yjs im Browser lehnt solche Updates in der Regel ab oder hält sie zurück.
- Gleichzeitiges Tippen im selben Textfeld führt zu „letzter gewinnt“ je Feld, weil Texte als einfache Werte gespeichert sind. Andere Felder und Objekte gehen nie verloren. Echtes gemeinsames Tippen (Y.Text) kann später kommen.
- Aufgabenverweise sind wenige Sekunden verzögert (bis zur nächsten Compaction).
- Rechte werden bei jedem Update geprüft. Wer aus dem Projekt fliegt, kann sofort nicht mehr zeichnen; eine bestehende Verbindung empfängt bis zum nächsten Beitritt aber noch Updates.
- Ein späterer Wechsel zu einem eigenen Dienst bleibt möglich: Datenformat (Yjs-Updates und Snapshots) und Tabellen bleiben gleich.
