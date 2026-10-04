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
- **Wissensgalaxie:** die einzige dunkle Fläche der App. Ein violett-pinker Raum mit Sternen, einem langsam treibenden Netz und gelegentlichen Sternschnuppen (reine Dekoration, `galaxy/space.ts`). Artikel sind Planeten in gedämpften Farben ihrer Art (`galaxy/planets.ts`) mit beleuchteter Kugel, Atmosphäre, Wolkenbändern und einem vorbeiziehenden Sturm. Artikel mit mindestens 3 Beziehungen haben einen Ring, ab 4 einen Mond. Der Titel steht im Planeten, Beziehungen sind gebogene Lichtfäden. Die Galaxie hat einen Vollbildmodus, Esc beendet ihn. Auf dem Handy ordnet sich die Galaxie hochkant an, zwei Finger zoomen. Mit `prefers-reduced-motion` steht alles still.
- **Prioritäten:** jede Priorität hat ein Emoji und eine Farbe (🌿 Niedrig grün, 📌 Normal blau, 🔥 Hoch orange, 🚨 Dringend rot), als Chip und als farbige linke Kante der Kanban-Karte. Das Label bleibt als Text sichtbar, das Emoji ist für Screenreader ausgeblendet.
- **Kanban-Karten:** Fälligkeit als Chip (überfällig rot mit ⏰), Unteraufgaben, Fortschrittsbalken und Avatar der zuständigen Person. Spalten zeigen einen Statuspunkt und einen Hinweis, wenn sie leer sind.
