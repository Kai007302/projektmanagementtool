# Oberflächen-Design

Leitlinie: schlicht und modern. Helle, ruhige Flächen auf cremefarbenem Grund, viel Weißraum, genau eine Akzentfarbe, Systemschrift, keine UI-Bibliothek.

## Tokens

Alle Farben, Radien und Schatten stehen als CSS-Variablen in `src/ProjectHub.Web/src/index.css`. Komponenten verwenden nur diese Variablen; neue Farbwerte gehören dorthin, nicht in einzelne Regeln.

| Token | Zweck |
| --- | --- |
| `--accent` (Indigo `#4f46e5`) | einzige Akzentfarbe: primäre Buttons, aktive Navigation, Links, Fokus |
| `--text`, `--muted` | Text und Nebentext; `--muted` hält auch auf `--surface-muted` mindestens 4,5:1 Kontrast |
| `--bg`, `--surface`, `--surface-muted` | Seitenhintergrund (im hellen Modus Creme `#f7f2e8`, nicht Weiß), Karten/Panels, Spalten und Chips |
| `--danger`, `--success`, `--warning` (+ `-soft`) | nur für Status, nie als Dekoration |

## Weniger sichtbare Optionen

Seit Oktober 2026 gilt: Eine Ansicht zeigt ihren Inhalt und genau eine Hauptaktion, alles andere erscheint erst, wenn man es braucht. Vorbild sind Miro, Linear, Notion, Trello und Asana.

- **Direkt bearbeiten statt Formular:** Titel (Projekt, Spalte, Whiteboard, Aufgabe) sind Text, den man anklickt (`ui/InlineEdit.tsx`); Enter oder Verlassen speichert, Esc bricht ab.
- **Seltenes ins „…“-Menü:** Löschen, Spalten verschieben, Statuswechsel außer dem nächsten Schritt, Karten verschieben per Tastatur (`ui/Menu.tsx`).
- **Hinzufügen auf Klick:** „+ Aufgabe“ unten in jeder Kanban-Spalte öffnet ein Feld direkt in der Spalte (`ui/QuickCreate.tsx`). Alle anderen seltenen Formulare („+ Projekt“, „+ Team“, „+ Spalte“, „+ Person hinzufügen“, „+ Webex-Link“, „+ Bereich“, Tags, Beziehungen, Freigaben, Import, Person anonymisieren) öffnen sich in einem kleinen Fenster über der Seite (`ui/Reveal.tsx`, `ui/Dialog.tsx`) und schließen sich nach dem Speichern; die Seite darunter verschiebt sich nicht.
- **Eine Hauptaktion je Seite:** oben rechts neben der Überschrift, gefüllt in der Akzentfarbe („+ Projekt“, „+ Artikel“, „+ Team“, Klasse `primary-button`).
- **Aufgaben im eigenen Fenster:** Ein Klick irgendwo auf eine Board-Karte (oder auf eine Zeile in der Liste) öffnet die ganze Aufgabe in einem Fenster über der Seite, wie bei Trello. Dort lassen sich alle Felder ändern, auch Start und Fälligkeit (Zeitraum); jedes Feld speichert sofort, es gibt keinen Speichern-Knopf. Esc, ✕ oder ein Klick daneben schließt das Fenster.
- **Suchen statt Navigieren:** Strg+K (Cmd+K) oder das breite Suchfeld in der Mitte der Kopfzeile (ohne Wort, Lupe links, Tastenkürzel grau in der Mitte) springt zu Bereichen, Projekten und Wissensartikeln (`ui/CommandPalette.tsx`). Die Wissenssuche sucht beim Tippen; die Filter erscheinen erst nach Klick auf „Filter“, aktive Filter stehen als Chips daneben und verschwinden mit einem Klick.
- **Projektmenü:** Jede Projektkachel und die Projektansicht haben ein „…“-Menü mit „Kalender abonnieren“, „Daten herunterladen (Excel)“ und „Symbol und Logo ändern“ (ADR 0020). Persönliches (eigene Termine, eigene Daten) steht im Menü am Avatar, nicht auf der Übersicht.
- **Barrierefreiheit:** Alles bleibt per Tastatur erreichbar, nur nicht ständig sichtbar; das Kartenmenü erscheint beim Überfahren oder bei Tastaturfokus.

## Muster

- **Kopfzeile:** feste, schmale Leiste mit Logo, Bereichsnavigation (Projekte, Wissen, Teams), Suchfeld, Benachrichtigungen und Avatar. Helles/dunkles Design und Abmelden stehen im Avatar-Menü. Die Dev-Anmeldung ist gestrichelt markiert, weil sie nur in Development erscheint.
- **Projektansicht:** Reiter Board, Liste, Gantt, Whiteboard, Wissen, Aktivität und – wenn Webex eingerichtet ist oder Links bestehen – Webex. Die Mitglieder stehen als Avatare oben rechts im Projektkopf und öffnen sich in einem Fenster.
- **Ansichtswechsel** innerhalb einer Seite (Board/Liste/Gantt/Whiteboard, Artikel/Galaxie) als Segment-Schalter (`.tabs`).
- **Karten und Panels:** weiß, 1 px Rahmen, großer Radius, sehr leichter Schatten. Überschriften von Seitenpanels klein in Versalien.
- **Buttons:** primär = Akzentfarbe (`type="submit"`), sonst weiß mit Rahmen; Löschen rot umrandet. Felder und normale Knöpfe sind 36 px hoch (`--control-height`).
- **Symbole:** Bedienelemente (Glocke, Schließen, Suche, Kalender) nutzen Linien-Symbole aus `ui/icons.tsx`, wie die Whiteboard-Werkzeuge. Emoji bleiben für Inhalte mit Persönlichkeit: Prioritäten, Projektsymbole, Artikelarten, leere Zustände.
- **Laden:** graue Platzhalter in Form der Inhalte (`ui/Skeleton.tsx`) statt „… wird geladen“. Verschieben auf dem Board wirkt sofort und wird bei einem Fehler zurückgedreht.
- **Erfolgsmeldungen:** kurze Einblendung unten rechts (`toast()` aus `ui/toast.ts`), die von selbst verschwindet und einen Seitenwechsel übersteht.
- **Datumsfelder:** `ui/DateField.tsx` nimmt „30.11.“, „30.11.26“, „morgen“, „Fr“, „+3“ oder „+2w“ und zeigt „30.11.2026“; der Kalender-Knopf öffnet die Auswahl des Browsers.
- **Neues Projekt:** Vorlage wählen (Leer, Kanban, Veranstaltung, Software). Die Vorlage legt Spalten und erste Aufgaben über die normalen Schnittstellen an (`projects/templates.ts`).
- **Fenster und Menüs:** Fenster (`ui/Dialog.tsx`) werden ans Ende der Seite gehängt, damit die unscharfe Kopfzeile sie nicht einschließt. Esc, ✕ oder ein Klick daneben schließt sie. Menüs klappen als schwebende Liste auf und verschieben nichts.
- **Stylesheets:** keine CSS-Verschachtelung. `src/styles.test.ts` prüft, dass keine Regel in einer anderen steckt; eine fehlende schließende Klammer hatte im Oktober 2026 alle späteren Regeln (Menüs, Fenster, Fußzeile) unbemerkt abgeschaltet.
- **Barrierefreiheit:** Die Playwright-Prüfung `e2e/accessibility.spec.ts` (axe, WCAG 2.1 AA) muss grün bleiben, insbesondere der Farbkontrast.
- **Wissensgalaxie:** im hellen Design die einzige dunkle Fläche der App, im Dunkelmodus unverändert. Ein violett-pinker Raum mit Sternen, einem langsam treibenden Netz und gelegentlichen Sternschnuppen (reine Dekoration, `galaxy/space.ts`). Artikel sind Planeten in gedämpften Farben ihrer Art (`galaxy/planets.ts`) mit beleuchteter Kugel, Atmosphäre, Wolkenbändern und einem vorbeiziehenden Sturm. Artikel mit mindestens 3 Beziehungen haben einen Ring, ab 4 einen Mond. Der Titel steht im Planeten, Beziehungen sind gebogene Lichtfäden. Die Galaxie hat einen Vollbildmodus, Esc beendet ihn. Auf dem Handy ordnet sich die Galaxie hochkant an, zwei Finger zoomen. Mit `prefers-reduced-motion` steht alles still.
- **Prioritäten:** jede Priorität hat ein Emoji und eine Farbe (🌿 Niedrig grün, 📌 Normal blau, 🔥 Hoch orange, 🚨 Dringend rot), als Chip und als farbige linke Kante der Kanban-Karte. Das Label bleibt als Text sichtbar, das Emoji ist für Screenreader ausgeblendet.
- **Kanban-Karten:** Fälligkeit als Chip (überfällig rot mit ⏰), Unteraufgaben, Fortschrittsbalken und Avatar der zuständigen Person. Spalten zeigen einen Statuspunkt und einen Hinweis, wenn sie leer sind. Auf dem Board werden Karten nur verschoben und angesehen: ein Klick öffnet die Aufgabe rechts als reine Leseansicht, geändert wird sie in der Ansicht „Liste“ oder im Gantt.
- **Startseite:** Begrüßung nach Tageszeit mit meinen offenen und überfälligen Aufgaben und den nächsten drei Fälligkeiten. Darunter „Weiter, wo du warst“ mit den zuletzt geöffneten Projekten, Artikeln und Whiteboards (pro Person im Browser gespeichert, `ui/recent.ts`) und dem Fortschritt zum nächsten Meilenstein des letzten Projekts. Projekte sind Kacheln mit eigener Farbe und eigenem Emoji (stabil aus der Projekt-ID, `ui/personality.ts`, oder selbst gewählt; ein hochgeladenes Logo ersetzt das Emoji), Fortschrittsring und den Avataren der Personen, denen Aufgaben zugewiesen sind. Die Zahlen werden im Browser aus den Aufgaben der ersten 12 Projekte berechnet, ein Request pro Projekt, damit das Rate-Limit nicht greift.
- **Konfetti:** kurzer Konfettiregen, wenn eine Aufgabe auf „Erledigt“ wechselt (`ui/confetti.ts`), mit `prefers-reduced-motion` aus.
- **Leere Zustände:** immer `EmptyState` mit Emoji in einem Kreis, einem Satz und optional einem Hinweis, was als Nächstes zu tun ist.
- **Wissensartikel:** jede Art hat ein Emoji und links einen Farbstreifen in der Farbe ihres Planeten in der Galaxie.
- **Aktivität:** Avatar mit kleinem Ereignis-Icon und relativer Zeit („vor 5 Minuten“), die genaue Zeit steht im Tooltip.
- **Whiteboard:** Werkzeuge schweben auf der Fläche wie in Miro: links eine senkrechte Leiste mit Symbolen (Notizstapel mit Farbpunkt, Formen, Aufgabenkarte, Vorlagen), unten links Rückgängig/Wiederholen, unten rechts Zoom und „Alles zeigen“. Rechts neben der Fläche steht eine Spalte mit der Objektliste und – sobald etwas gewählt ist – den Angaben zum gewählten Objekt (Text bzw. Aufgabe, Verbindungen, Farbe, Sperren, Entfernen); Objekte öffnen sich nicht in einem eigenen Feld. Ein Linksklick wählt und zieht nur, die Optionen eines Objekts kommen per Rechtsklick als Menü (Text bearbeiten, Farbe, Sperren, Entfernen); per Tastatur öffnet die Menütaste oder Shift+F10 dasselbe Menü. Verbindungen zieht man wie in draw.io: am gewählten Objekt liegen vier kleine Punkte, von denen aus ein Pfeil zu einem anderen Objekt oder auf eine freie Stelle gezogen wird; angehängte Pfeile folgen ihren Objekten und verschwinden mit ihnen. Gesperrte Objekte tragen ein kleines Schloss und lassen sich nicht verschieben, ändern oder löschen. Notizen haben keinen Rahmen, nur einen weichen Schatten; Rahmen (Rechtecke) zeigen ihre erste Textzeile fett als Überschrift. Die Auswahl ist ein durchgehender Akzentrahmen mit rundem Griff, fremde Auswahl gestrichelt in der Farbe der Person. Text bearbeitet man direkt im Objekt: Doppelklick oder Enter öffnet ihn, Esc oder ein Klick daneben schließt ihn; neue Notizen öffnen sich gleich zum Tippen. Position und Größe gibt es nicht als Zahlenfelder, man zieht mit der Maus oder nutzt Pfeiltasten (verschieben) und Alt+Pfeiltasten (Größe).
- **Dunkelmodus:** folgt der Systemeinstellung, „Dunkles Design“ bzw. „Helles Design“ im Avatar-Menü überschreibt sie und merkt sich die Wahl im Browser. Alle Farben kommen aus den Tokens in `index.css`, die dort für Dunkel ein zweites Mal definiert sind. Text in Akzentfarbe nutzt `--accent-text`, weil der Akzent auf dunklem Grund zu wenig Kontrast hat. Die axe-Prüfung läuft auch im Dunkelmodus.
