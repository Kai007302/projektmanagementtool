# ADR 0018 — Standardformate: Aufgaben als Excel/CSV, Gantt als PDF, Kalender-Abo

## Status
Proposed

## Kontext

Bisher gab es außer einzelnen `.ics`-Dateien (DEC-028) und dem DSGVO-Export keinen Weg, Projektdaten in gängigen Formaten aus ProjectHub heraus- oder hineinzubringen. Kai hat drei Dinge gewünscht (2026-10-05): Aufgaben als CSV/Excel importieren und exportieren, das Gantt-Diagramm als PDF und einen abonnierbaren Kalender.

## Entscheidung

### Aufgaben als Excel und CSV

- `GET /api/v1/projects/{id}/tasks/export?format=xlsx|csv` (Recht: View). Alle aktiven Aufgaben inklusive Unteraufgaben, Eltern vor ihren Unteraufgaben, höchstens 10.000.
- Spalten mit den Begriffen der Oberfläche: Titel, Beschreibung, Status, Priorität, Zuständig (E-Mail), Zuständig, Start, Fällig, Fortschritt (%), Aufwand (h), Übergeordnete Aufgabe, ID.
- CSV so, wie Excel in Deutschland es erwartet: Semikolon, UTF-8 mit BOM, CRLF, Zahlen mit Komma. Freitext, der mit `=`, `+`, `-`, `@`, Tab oder CR beginnt, bekommt ein vorangestelltes `'` (CSV-Injection); der Import entfernt es wieder.
- Excel über das **Open XML SDK** (`DocumentFormat.OpenXml`, MIT, von Microsoft): Text als Inline-Strings (nie Formeln), Datumswerte als echte Datumszellen, Kopfzeile fett und fixiert, Filter.
- `POST /api/v1/projects/{id}/tasks/import?dryRun=true|false` mit Datei `file` (Recht: Contribute, Upload-Rate-Limit, höchstens 2 MB, 1.000 Zeilen, 50 Spalten). `.xlsx` (erstes Blatt) oder `.csv`/`.txt`; CSV in UTF-8 oder Windows-1252, Trennzeichen `;`, `,` oder Tab wird aus der Kopfzeile erkannt.
- Spaltennamen deutsch oder englisch (auch die des Exports), Status und Priorität als Begriff der Oberfläche oder API-Wert, Datum als `2026-11-30`, `30.11.2026` oder Excel-Datum, Zahlen mit Punkt oder Komma. Zuständige Person über E-Mail oder eindeutigen Namen; sie muss in diesem Projekt zuweisbar sein (wie `PATCH /tasks/{id}`).
- **Alles oder nichts:** Jede Zeile wird mit denselben Regeln wie beim Anlegen geprüft. Gibt es einen Fehler, wird nichts angelegt; die Antwort listet alle Fehler mit Zeile, Spalte und Code. `dryRun=true` prüft nur (Vorschau in der Oberfläche).
- Der Import legt immer neue Aufgaben der obersten Ebene an. „Übergeordnete Aufgabe“ und „ID“ werden ignoriert (Vorschlag DEC-042). Ein Eintrag `TasksImported` im Aktivitätsfeed; keine Benachrichtigung je zugewiesener Aufgabe (Vorschlag DEC-040).
- Oberfläche: Ansicht „Liste“, Export für alle, Import für Personen mit Schreibrecht.

### Gantt als PDF

- `GET /api/v1/projects/{id}/gantt/export.pdf` (Recht: View). Der Server zeichnet A4 quer: ganze Zeitspanne auf Seitenbreite, Monate und darunter Tage oder Kalenderwochen, Wochenenden, Heute-Linie, Balken mit Fortschritt nach Status, Meilensteine, Unteraufgaben eingerückt, Seitenumbruch mit Seitenzahlen.
- Eigener kleiner PDF-Writer (`Infrastructure/Documents/PdfDocument.cs`): Rechtecke, Linien, Text in den Standardschriften Helvetica/Helvetica-Bold (WinAnsi, deckt Deutsch ab), komprimierte Inhalte. Zeichen außerhalb von WinAnsi werden zu `?`.
- Abhängigkeitspfeile fehlen im PDF; verletzte Abhängigkeiten stehen weiterhin in der App.

### Kalender-Abo

- Je Person höchstens eine geheime Adresse (`calendar_feed`, Migration 013). `POST /api/v1/me/calendar-feed` erzeugt 32 zufällige Bytes (Base64url) und gibt die Adresse **einmal** zurück; gespeichert wird nur der SHA-256-Hash. Erneutes Erzeugen ersetzt die alte Adresse sofort. `GET` zeigt Status und letzten Abruf, `DELETE` beendet das Abo. Erzeugen und Löschen stehen im Audit-Log (ohne Token).
- `GET /api/v1/calendar-feed.ics?token=…` ohne Anmeldung, weil Outlook, Google und Apple Kalender kein Entra-Token senden können. Das Token steht im **Query-String**: Der Web-Container protokolliert keine Query-Strings (ADR 0017), die Traces entfernen sie (ADR 0013). Unbekannte Tokens, inaktive und anonymisierte Personen: 404.
- Inhalt bei jedem Abruf neu und mit den aktuellen Rechten der Person: ihr zugewiesene Aufgaben mit Start- oder Fälligkeitsdatum in Projekten, die sie sehen darf, und Meilensteine der Projekte, in denen sie Mitglied ist; ab 90 Tagen zurück, höchstens je 1.000. Titel mit Projektname, Link zur App (Vorschlag DEC-041). Dieselben UIDs wie die einzelnen `.ics`-Dateien.
- `REFRESH-INTERVAL`/`X-PUBLISHED-TTL` eine Stunde; wie oft ein Programm wirklich abruft, bestimmt es selbst (Outlook im Web mehrere Stunden).
- DSGVO: Der Export der eigenen Daten nennt, ob ein Abo besteht; die Anonymisierung löscht es.
- Oberfläche: unten auf jeder Seite „Kalender abonnieren“ mit Kopieren, `webcal://`-Link und Hinweis für Outlook.

## Alternativen

- **PDF im Browser** (Druckansicht oder jsPDF/svg2pdf): Druckansichten hängen vom Browser ab, das Gantt nutzt CSS-Klassen und Variablen, die svg2pdf nicht kennt. **PDF-Bibliothek** (QuestPDF, PdfSharp): QuestPDF verlangt ab einer Umsatzgrenze eine kostenpflichtige Lizenz, beide sind für Balken und Text überdimensioniert.
- **Excel selbst schreiben/lesen:** Schreiben ginge mit wenig Code, Lesen fremder Dateien (Shared Strings, Datumsformate, Inline-Strings) nicht zuverlässig. Das Open XML SDK ist die Referenz von Microsoft.
- **Import mit Teilerfolg** (gültige Zeilen anlegen, fehlerhafte melden): Nach einer Korrektur entstünden beim zweiten Import Dubletten. Alles oder nichts lässt sich gefahrlos wiederholen.
- **Kalender über Microsoft Graph** in den Outlook-Kalender schreiben: braucht `Calendars.ReadWrite` und funktioniert nur mit Microsoft 365; das Abo funktioniert mit jedem Kalender ohne Graph-Recht.
- **Token im Pfad** statt im Query-String: wäre im Zugriffslog des Web-Containers gelandet.
- **Token verschlüsselt statt gehasht speichern**, um die Adresse später erneut anzuzeigen: Ein Hash genügt, „Neue Adresse erstellen“ ersetzt das Anzeigen.

## Konsequenzen

- Neue Abhängigkeit `DocumentFormat.OpenXml` (Abhängigkeits-Scan wie die übrigen Pakete).
- Wer die Kalender-Adresse kennt, sieht Titel und Projektnamen der Termine der Person, bis sie eine neue Adresse erstellt oder das Abo löscht. Die Daten liegen danach auch beim Kalenderanbieter (z. B. Microsoft, Google), der die Adresse abruft. Betreiber, die das nicht wollen, brauchen einen Schalter (heute nicht vorhanden).
- Kalenderprogramme rufen ohne Anmeldung ab; es gilt das allgemeine Rate Limit je Adresse (600/Minute). Die Token sind nicht erratbar (256 Bit).
- Ein Import mit vielen Zeilen benachrichtigt die zugewiesenen Personen nicht.
