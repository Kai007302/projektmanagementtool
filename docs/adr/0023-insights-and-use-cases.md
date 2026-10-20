# ADR 0023 — Erkenntnisse und Anwendungsfälle im Wissen

## Status
Proposed

## Kontext

Kai hat am 2026-10-20 gewünscht, Wissen und Erkenntnisse „cool und modern, schnell übersichtlich“ aufzubereiten und dabei PowerPoint- und Excel-Dateien zu vermeiden. Im Sparring hat er das geschärft: Im Mittelpunkt stehen Erkenntnisse aus Experimenten, nicht das Projektmanagement. Die KI soll ein Chatfenster für Fragen zur Dokumentation sein. Die Darstellung soll grafisch sein und sich je nach Anwendungsfall anpassen; ein zweiter Anwendungsfall sind Teams, die Software entwickeln. Der Entwurf (Experimentseite, Galaxie, Folien, Software) wurde als Design-Canvas abgestimmt.

Bisher kannte das Wissen nur allgemeine Artikelarten (Artikel, Anleitung, Prozess …) mit frei gestaltetem Blockinhalt. Messwerte landeten als Excel-Anhang, Ergebnisse als Foliensatz.

## Entscheidung

### Anwendungsfall je Kategorie

- `knowledge_space.use_case` (Migration 019): `general` (Standard, wie bisher), `experiment` oder `software`. Wer die Abteilung der Kategorie verwalten darf, legt ihn beim Anlegen fest und kann ihn ändern (`PATCH /knowledge/spaces/{id} { useCase }`).
- Der Katalog der Anwendungsfälle steht fest im Code (`KnowledgeUseCases`) und wird über `GET /knowledge/use-cases` ausgeliefert, damit Oberfläche, Prüfung und Assistent dieselben Felder und Ergebnisse kennen (DEC-046).
- Ein Anwendungsfall bringt **Erkenntnisarten** mit, jede ist eine neue Artikelart:
  - Experiment: `experiment` mit den Feldern Frage, Aufbau, Ergebnis, **Erkenntnis**; Ergebnisse Läuft, Bestätigt, Widerlegt, Unklar.
  - Softwareentwicklung: `spike` (Problem, Ansatz, Entscheidung, **Learning**), `decision` (Kontext, **Entscheidung**, Alternativen, Folgen), `retro_learning` (Beobachtung, Ursache, Maßnahme, **Learning**), `incident` (Auswirkung, Ursache, Behebung, **Learning**), `release` (Inhalt, Lief gut, Lief schlecht, **Learning**), jeweils mit eigenen Ergebnissen (z. B. Übernommen/Verworfen, Gilt/Abgelöst, Behoben).
  - Das hervorgehobene Feld ist die Kernaussage; es steht auf der Karte grün und auf den Folien zuletzt.
- Jedes Ergebnis hat einen Ton (`positive`, `negative`, `active`, `neutral`), der Farbe in Badge, Zeitstrahl und Galaxie bestimmt. Die Farbe wiederholt nur, was das Wort sagt.
- Die Artikelarten stehen allen Kategorien offen; die Kategorie ordnet nur, welche zuerst angeboten werden. So bleibt ein Artikel gültig, wenn er die Kategorie wechselt.

### Erkenntnis-Karte, Ergebnis und Sicherheit

- Neuer Block `insight` mit Feldern (`key`, `label`, `text`, höchstens 12). Die Beschriftung wird mitgespeichert, damit die Karte nach einem Wechsel der Art oder einer späteren Umbenennung im Katalog gleich lesbar bleibt.
- Ein neuer Artikel einer Erkenntnisart beginnt mit seiner Karte und dem ersten Ergebnis seiner Art (z. B. „Läuft“). Beim Wechsel der Art übernimmt die Karte Texte mit gleichem Schlüssel; Felder der alten Art, die die neue nicht kennt, bleiben am Ende stehen, wenn sie Text haben.
- `knowledge_article.outcome` (Schlüssel eines Ergebnisses der Art) und `knowledge_article.confidence` (1–4, „gering“ bis „sehr hoch“). Beides gibt es nur für Erkenntnisse; die API prüft, dass das Ergebnis zur Art passt. Wechselt ein Artikel zu einer allgemeinen Art, entfallen beide.
- Ergebnis und Sicherheit ändert man direkt im Kopf des Artikels, ohne den Editor zu öffnen. Während der Editor offen ist, zeigt der Kopf sie nur an, damit sich zwei Speichervorgänge nicht überholen.

### Messdaten statt Excel

- Neuer Block `data_table`: Titel, Spalten (1–20), Zeilen (höchstens 500), Zellen bis 200 Zeichen und die Darstellung `line`, `bar` oder `none` (DEC-047).
- Daten kommen als Text herein: aus Excel kopiert (Tabulator) oder als CSV mit Komma oder Semikolon, als Datei oder eingefügt. Die erste Zeile nennt die Spalten. Zu große Tabellen werden gekürzt, ein Hinweis sagt es.
- Das Diagramm zeichnet der Browser als SVG aus den Zahlenspalten über der ersten Spalte (höchstens 6 Reihen); deutsche und englische Zahlenschreibweisen werden erkannt. Ein Schalter wechselt zwischen Diagramm und Tabelle, ein Textauszug (Reihe von … bis …) beschreibt das Diagramm für Screenreader.
- In die Volltextsuche gehen nur Titel und Spaltennamen, nicht jede Zahl. Für den Assistenten wird die Tabelle als Markdown-Tabelle (höchstens 100 Zeilen) ausgegeben.

### Beziehungen zwischen Erkenntnissen

- Neue Beziehungsarten `BUILDS_ON` (baut auf), `CONFIRMS` (bestätigt) und `CONTRADICTS` (widerspricht). In der Galaxie sind Bestätigungen grüne, „baut auf“ blau gepunktete und Widersprüche orange gestrichelte Lichtfäden; Planeten von Erkenntnissen tragen die Farbe ihres Ergebnisses.

### Übersicht, Folien und Fragen zur Doku

- Eine Kategorie mit Erkenntnisarten zeigt über der Artikelliste „Was wissen wir inzwischen über …?“: Knöpfe zum Anlegen jeder Art, Filter nach Art und einen Zeitstrahl der Erkenntnisse mit ihrem Ergebnis.
- „Als Folien zeigen“ stellt einen Artikel als Präsentation im ganzen Fenster dar: Titel, je ein gefülltes Kartenfeld, je ein Abschnitt (Überschrift der Ebene 1 oder 2), je eine Messdatentabelle als großes Diagramm und die Kernaussage zuletzt. Die Folien werden jedes Mal aus dem Artikel gebaut und nicht gespeichert; Pfeiltasten, Bild auf/ab, Leertaste, Pos1/Ende und Esc bedienen sie, ein Knopf schaltet auf Vollbild.
- „Fragen zur Doku“ öffnet den Assistenten (ADR 0015) als Seitenleiste neben dem Wissen. Gefragt wird im offenen Artikel, in seiner Kategorie oder in der ganzen Galaxie. Der Chat sendet dazu `spaceId` und `articleId`; der Server sucht dann nur in dieser Kategorie (`search_knowledge` mit hartem Filter, DEC-048) und nennt den Artikel im Kontext nur, wenn die Person ihn lesen darf. Es gelten die Regeln von ADR 0015: nur lesbare Artikel, Quellen, kein gespeichertes Gespräch. Die KI läuft nur, wenn jemand fragt; es gibt keine automatischen Zusammenfassungen (Kai, 2026-10-08).

## Konsequenzen

- Neue Anwendungsfälle oder Felder brauchen eine Codeänderung, keine Konfiguration. Das hält Prüfung, Oberfläche und Assistent gleich und ist für die nächsten Fälle schnell erweitert; eigene Felder je Organisation wären ein späterer Schritt.
- Bestehende Artikel und Kategorien ändern sich nicht: alle Kategorien sind `general`, kein Artikel hat ein Ergebnis.
- xlsx-Dateien werden nicht eingelesen, nur eingefügter Text und CSV. Das vermeidet eine Bibliothek zum Lesen von Office-Dateien im Browser und damit verbundene Sicherheitsrisiken.
- Ohne eingerichteten KI-Anbieter (`PROJECTHUB_AI_PROVIDER`) fehlt nur der Knopf „Fragen zur Doku“; Karten, Daten, Zeitstrahl und Folien funktionieren ohne KI.
- Offen für Kai: diese ADR sowie DEC-046 bis DEC-048 freigeben.
