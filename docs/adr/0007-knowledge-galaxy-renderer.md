# ADR 0007 — Renderer der Knowledge Galaxy

## Status
Accepted

## Entscheidung

Die Knowledge Galaxy zeichnet mit **Canvas 2D**; die Positionen berechnet **d3-force** in einem Web Worker. React Flow und SVG/DOM-Knoten werden nicht verwendet.

Die Galaxie ist eine Projektion von `KnowledgeArticle` und `KnowledgeRelation` (`GET /api/v1/knowledge/graph`) und hat keine eigenen Daten. Sie zeigt nur Artikel, die die Person lesen darf, und nur Beziehungen zwischen solchen Artikeln. Die Grenze liegt bei 2000 Knoten; darüber hinaus erscheinen die zuletzt geänderten Artikel, und Filter nach Bereich und Art grenzen ein.

## Bewertung

Gemessen wurde ein synthetischer Graph (Knoten mit ca. 1,5 Kanten je Knoten) in headless Chromium ohne GPU im Entwicklungscontainer, also unter pessimistischen Bedingungen. Gleiches d3-force-Layout für alle Varianten.

| Knoten | Variante | erstes Zeichnen | Bildzeit beim Zoomen/Verschieben (Mittel / p95) |
|---|---|---|---|
| 300 | Canvas | 6 ms | 17 / 21 ms |
| 300 | React Flow | 342 ms | 23 / 37 ms |
| 300 | React Flow, nur sichtbare Elemente | 469 ms | 33 / 55 ms |
| 800 | Canvas | 7 ms | 32 / 45 ms |
| 800 | React Flow | 558 ms | 35 / 46 ms |
| 2000 | Canvas | 9 ms | 66 / 114 ms |
| 2000 | React Flow | 1424 ms | 74 / 97 ms |

Das Zeichnen selbst kostet im Canvas 1–3 ms je Bild (300–2000 Knoten). Die übrige Bildzeit ist die Software-Rasterung im Container; mit GPU fällt sie weg. React Flow braucht schon für das erste Bild ein Vielfaches, weil jeder Knoten ein DOM-Element ist.

Die Layoutberechnung (300 Schritte) dauerte im Container etwa 0,6 s bei 300 Knoten und 1,7 s bei 800 Knoten. Sie läuft deshalb im Worker, und die Ansicht zeigt, wie sich die Galaxie einpendelt.

## Konsequenz

- Interaktion (Verschieben, Zoomen, Hover, Auswahl, Fokus) ist selbst gebaut (`src/knowledge/galaxy/graph.ts`, unit-getestet).
- Canvas ist für Screenreader stumm. Deshalb gibt es eine gleichwertige Listendarstellung (nach Art gruppiert, mit Beziehungen zum Weiternavigieren), eine Auswahl „Artikel fokussieren“ und eine Tastatursteuerung des Canvas.
- `prefers-reduced-motion` schaltet Übergänge und das sichtbare Einpendeln ab.
- Für deutlich mehr Knoten (> 5000) wäre WebGL der nächste Schritt; das ist derzeit nicht nötig.
