# ADR 0001 — Modularer Monolith

## Status
Accepted

## Entscheidung

Das Core-System startet als modularer Monolith.

## Begründung

Die Domäne ist eng gekoppelt. Eine 6.000-Nutzer-Organisation erfordert Skalierbarkeit, aber nicht automatisch Microservices. Klare Modulgrenzen ermöglichen spätere Extraktion bei messbarem Bedarf.

## Konsequenz

Ein gemeinsames Deployment, aber klare fachliche Modulgrenzen und keine direkten Zugriffe auf fremde Internals.
