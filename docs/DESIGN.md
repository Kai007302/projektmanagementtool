# Oberflächen-Design

Leitlinie: schlicht und modern. Helle, ruhige Flächen, viel Weißraum, genau eine Akzentfarbe, Systemschrift, keine UI-Bibliothek.

## Tokens

Alle Farben, Radien und Schatten stehen als CSS-Variablen in `src/ProjectHub.Web/src/index.css`. Komponenten verwenden nur diese Variablen; neue Farbwerte gehören dorthin, nicht in einzelne Regeln.

| Token | Zweck |
| --- | --- |
| `--accent` (Indigo `#4f46e5`) | einzige Akzentfarbe: primäre Buttons, aktive Navigation, Links, Fokus |
| `--text`, `--muted` | Text und Nebentext; `--muted` hält auch auf `--surface-muted` mindestens 4,5:1 Kontrast |
| `--bg`, `--surface`, `--surface-muted` | Seitenhintergrund, Karten/Panels, Spalten und Chips |
| `--danger`, `--success`, `--warning` (+ `-soft`) | nur für Status, nie als Dekoration |

## Muster

- **Kopfzeile:** feste, schmale Leiste mit Logo, Bereichsnavigation (Projekte, Wissen, Teams), Benachrichtigungen und Avatar. Die Dev-Anmeldung ist gestrichelt markiert, weil sie nur in Development erscheint.
- **Ansichtswechsel** innerhalb einer Seite (Board/Liste/Gantt/Whiteboard, Artikel/Galaxie) als Segment-Schalter (`.tabs`).
- **Karten und Panels:** weiß, 1 px Rahmen, großer Radius, sehr leichter Schatten. Überschriften von Seitenpanels klein in Versalien.
- **Buttons:** primär = Akzentfarbe (`type="submit"`), sonst weiß mit Rahmen; Löschen rot umrandet.
- **Barrierefreiheit:** Die Playwright-Prüfung `e2e/accessibility.spec.ts` (axe, WCAG 2.1 AA) muss grün bleiben, insbesondere der Farbkontrast.
- **Wissensgalaxie:** die einzige dunkle Fläche der App. Ein violett-pinker Raum mit Sternen und einem langsam treibenden Netz im Hintergrund (reine Dekoration, `galaxy/space.ts`). Artikel sind leuchtende Kugeln in der Farbe ihrer Art, mit dem Titel darin, die leicht schweben, Beziehungen sind leicht gebogene Lichtfäden. Mit `prefers-reduced-motion` steht alles still.
