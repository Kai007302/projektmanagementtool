# ADR 0003 — Realtime vs. Durable Events

## Status
Accepted

## Entscheidung

- SignalR/WebSockets für Client-Realtime.
- Redis Pub/Sub nur für ephemeres Cross-Instance-Fan-out.
- Azure Service Bus für durable Jobs/Events.

## Konsequenz

Ein Redis-Ausfall darf keine persistenten Business Events vernichten.
