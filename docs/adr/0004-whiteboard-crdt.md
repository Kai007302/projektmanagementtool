# ADR 0004 — CRDT-basiertes Whiteboard

## Status
Accepted

## Entscheidung

Whiteboard-Realtime wird CRDT-basiert umgesetzt, bevorzugt Yjs.

## Konsequenz

Kein kompletter Canvas-State-Broadcast pro Änderung. Presence ist vom persistenten Dokumentzustand getrennt. Snapshots/Compaction sind Pflicht.
