# ADR 0002 — PostgreSQL als Source of Truth

## Status
Accepted

## Entscheidung

PostgreSQL ist die autoritative relationale Datenbank für transaktionale Projekt-/Task-Daten.

## Konsequenz

Kanban/Gantt sind Views. Redis und externe Integrationen sind nicht authoritative.
