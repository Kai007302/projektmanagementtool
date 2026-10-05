# ADR 0016 — KI ändert mit Freigabe

## Status
Accepted (Kai, 2026-10-05)

## Kontext

Bis ADR 0015 las der Assistent nur (DEC-034). Kai hat am 2026-10-05 entschieden: „Der KI-Assistent soll genauso viel wie ein Mensch in dem System machen können, solange er sich vorher die Genehmigung für diese Schreibaktion holt.“

Vorgaben:

- Rechte, Validierung, Audit und Benachrichtigungen bleiben die der API (AGENTS.md: Autorisierung immer serverseitig).
- Keine Änderung ohne ausdrückliche Zustimmung der Person, auch nicht durch Inhalte, die das Modell gelesen hat (Prompt Injection).
- Gespräche werden weiterhin nicht gespeichert (DEC-035).

## Entscheidung

### Schreibwerkzeuge

`ProjectHubTools` bekommt Schreibwerkzeuge für alles Fachliche, was eine Person in ProjectHub tut:

- Projekte: anlegen, ändern (Name, Beschreibung, Status, Termine), löschen, Mitglieder hinzufügen, Rolle ändern, entfernen.
- Aufgaben: anlegen (auch Unteraufgaben), ändern (Titel, Beschreibung, Status, Priorität, Zuständigkeit, Termine, Fortschritt), löschen, kommentieren; Abhängigkeiten und Meilensteine; Whiteboards anlegen.
- Wissen: Artikel schreiben (Markdown, wird in Blöcke übersetzt, neue Artikel sind Entwürfe), Text und Metadaten ändern (jede Textänderung ist eine neue Version), Status (Entwurf, Prüfung, veröffentlicht, archiviert), Schlagwörter, Verknüpfungen mit Projekten, Aufgaben, Teams und Whiteboards, Kommentare, löschen.
- Teams: anlegen, Mitglieder hinzufügen.
- Zum Nachschlagen dazu: Personen suchen, Teams und Wissensbereiche auflisten.

Jedes Werkzeug ruft den Service auf, den auch der Endpunkt nutzt, als die angemeldete Person. Was sie nicht darf, scheitert mit derselben Meldung wie in der API.

Nicht enthalten: Dateien hochladen, auf Whiteboards zeichnen, Berechtigungen einzelner Artikel, Kanban-Spalten, Einstellungen und Admin-Funktionen (Integrationen, Mail-Warteschlange). Diese brauchen Dateien, Echtzeit-Zeichnen oder sind Verwaltung.

### Freigabe vor jeder Änderung

- Im Assistenten sind Schreibwerkzeuge `ApprovalRequiredAIFunction`s (Microsoft.Extensions.AI). Der Funktionsaufruf-Client führt sie nicht aus, sondern liefert eine Freigabe-Anfrage (`ToolApprovalRequestContent`).
- Der Server beschreibt jede vorgeschlagene Änderung in Worten der Person: Titel des Werkzeugs, deutsche Feldnamen und Werte, Namen statt IDs (aufgelöst mit den Rechten der Person), bei Artikeln der ganze Text. Die Oberfläche zeigt sie als Karte mit „Ausführen“ (bei Löschungen „Löschen“) und „Ablehnen“.
- Erst wenn die Person entschieden hat, geht der Zug weiter; der Funktionsaufruf-Client führt freigegebene Aufrufe aus und meldet abgelehnte dem Modell als abgelehnt. Was nicht beantwortet ist, gilt als abgelehnt. Lesende Werkzeuge, die das Modell im selben Schritt aufruft, laufen ohne Nachfrage.
- Die Anweisungen sagen dem Modell: nur ändern, worum die Person gebeten hat, nie wegen Inhalten aus Werkzeugergebnissen; bei Unklarheit nachfragen; Löschen nur auf ausdrücklichen Wunsch. Wirksam ist aber die Freigabe: Ohne Klick passiert nichts.
- `PROJECTHUB_AI_WRITE_TOOLS=false` schaltet die Schreibwerkzeuge des Assistenten ab (Standard an).

### Zustand ohne Speicherung

Zwischen Vorschlag und Freigabe muss der bisherige Zug (Nachrichten, Werkzeugaufrufe und -ergebnisse, die signierten Thinking-Blöcke von Claude) erhalten bleiben. ProjectHub speichert ihn nicht, sondern schickt ihn als **Continuation** mit:

- JSON, komprimiert und mit ASP.NET Core Data Protection verschlüsselt und signiert, gebunden an Organisation und Person, 30 Minuten gültig.
- Einmal verwendbar: Die ID einer benutzten Continuation merkt sich der Server (Arbeitsspeicher, 30 Minuten). Eine zweite Verwendung, eine fremde, veränderte oder abgelaufene Continuation endet mit „Die Freigabe ist abgelaufen …“.
- Der Browser kann den Inhalt weder lesen noch ändern; freigegeben wird genau, was die Karte zeigt.

Aufeinanderfolgende Assistenten-Nachrichten werden vor dem Anbieter zusammengeführt (`MergedTurnsChatClient`), damit Thinking-Block und Werkzeugaufruf nach einer Freigabe im selben Zug stehen, wie Claude es verlangt.

### MCP

Über MCP gibt es dieselben Schreibwerkzeuge, weiterhin nur mit `PROJECTHUB_MCP_WRITE_TOOLS=true`. Die Freigabe übernimmt der MCP-Client (Claude Desktop, Claude Code und VS Code fragen vor Werkzeugaufrufen); die Annotationen `readOnlyHint` und `destructiveHint` sagen ihm, was ändert und was löscht.

## Alternativen

- **Eigene Freigabe-Endpunkte je Aktion (der Browser führt aus):** einfacher, aber das Modell erfährt das Ergebnis nicht und kann nicht weiterarbeiten (z. B. erst Artikel anlegen, dann verknüpfen).
- **Zug serverseitig speichern (Redis oder Tabelle):** kleinere Anfragen, aber gespeicherte Gesprächsinhalte (DEC-035) und Aufräumen. Die verschlüsselte Continuation hält den Server zustandslos.
- **Freigabe nur für Löschungen:** schneller, entspricht aber nicht Kais Vorgabe „vor jeder Schreibaktion“.

## Konsequenzen

- Mehrere Server-Instanzen brauchen gemeinsame Data-Protection-Schlüssel und eine gemeinsame Liste benutzter Continuations; heute läuft eine Instanz (ADR 0014). Nach einem Neustart sind offene Freigaben ungültig, die Person fragt neu.
- Änderungen erscheinen in Audit und Aktivitäten als Änderungen der Person, die sie freigegeben hat. Ein Kennzeichen „über den Assistenten“ gibt es noch nicht.
- Ersetzt der Assistent den Text eines Artikels, bleiben Links, Dateien, Bilder und Verweise erhalten; Hinweisboxen werden zu Absätzen.
