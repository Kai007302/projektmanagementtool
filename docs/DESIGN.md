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
- **Wissensgalaxie:** im hellen Design die einzige dunkle Fläche der App, im Dunkelmodus unverändert. Ein violett-pinker Raum mit Sternen, einem langsam treibenden Netz und gelegentlichen Sternschnuppen (reine Dekoration, `galaxy/space.ts`). Artikel sind Planeten in gedämpften Farben ihrer Art (`galaxy/planets.ts`) mit beleuchteter Kugel, Atmosphäre, Wolkenbändern und einem vorbeiziehenden Sturm. Artikel mit mindestens 3 Beziehungen haben einen Ring, ab 4 einen Mond. Der Titel steht im Planeten, Beziehungen sind gebogene Lichtfäden. Die Galaxie hat einen Vollbildmodus, Esc beendet ihn. Auf dem Handy ordnet sich die Galaxie hochkant an, zwei Finger zoomen. Mit `prefers-reduced-motion` steht alles still.
- **Prioritäten:** jede Priorität hat ein Emoji und eine Farbe (🌿 Niedrig grün, 📌 Normal blau, 🔥 Hoch orange, 🚨 Dringend rot), als Chip und als farbige linke Kante der Kanban-Karte. Das Label bleibt als Text sichtbar, das Emoji ist für Screenreader ausgeblendet.
- **Kanban-Karten:** Fälligkeit als Chip (überfällig rot mit ⏰), Unteraufgaben, Fortschrittsbalken und Avatar der zuständigen Person. Spalten zeigen einen Statuspunkt und einen Hinweis, wenn sie leer sind.
- **Startseite:** Begrüßung nach Tageszeit mit meinen offenen und überfälligen Aufgaben und den nächsten drei Fälligkeiten. Projekte sind Kacheln mit eigener Farbe und eigenem Emoji (stabil aus der Projekt-ID, `ui/personality.ts`), Fortschrittsring und den Avataren der Personen, denen Aufgaben zugewiesen sind. Die Zahlen werden im Browser aus den Aufgaben der ersten 12 Projekte berechnet, ein Request pro Projekt, damit das Rate-Limit nicht greift.
- **Konfetti:** kurzer Konfettiregen, wenn eine Aufgabe auf „Erledigt“ wechselt (`ui/confetti.ts`), mit `prefers-reduced-motion` aus.
- **Leere Zustände:** immer `EmptyState` mit Emoji in einem Kreis, einem Satz und optional einem Hinweis, was als Nächstes zu tun ist.
- **Wissensartikel:** jede Art hat ein Emoji und links einen Farbstreifen in der Farbe ihres Planeten in der Galaxie.
- **Aktivität:** Avatar mit kleinem Ereignis-Icon und relativer Zeit („vor 5 Minuten“), die genaue Zeit steht im Tooltip.
- **Whiteboard:** Werkzeuge schweben auf der Fläche wie in Miro: links eine senkrechte Leiste mit Symbolen (Notizstapel mit Farbpunkt, Formen, Aufgabenkarte, Vorlagen), unten links Rückgängig/Wiederholen, unten rechts Zoom und „Alles zeigen“. Notizen haben keinen Rahmen, nur einen weichen Schatten; Rahmen (Rechtecke) zeigen ihre erste Textzeile fett als Überschrift. Die Auswahl ist ein durchgehender Akzentrahmen mit rundem Griff, fremde Auswahl gestrichelt in der Farbe der Person. Text bearbeitet man direkt im Objekt: Doppelklick oder Enter öffnet ihn, Esc oder ein Klick daneben schließt ihn; neue Notizen öffnen sich gleich zum Tippen. Farben wählt man über runde Farbfelder. Position und Größe gibt es nicht als Zahlenfelder, man zieht mit der Maus oder nutzt Pfeiltasten (verschieben) und Alt+Pfeiltasten (Größe).
- **Dunkelmodus:** folgt der Systemeinstellung, der Mond- bzw. Sonnen-Knopf im Kopf überschreibt sie und merkt sich die Wahl im Browser. Alle Farben kommen aus den Tokens in `index.css`, die dort für Dunkel ein zweites Mal definiert sind. Text in Akzentfarbe nutzt `--accent-text`, weil der Akzent auf dunklem Grund zu wenig Kontrast hat. Die axe-Prüfung läuft auch im Dunkelmodus.
