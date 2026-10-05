# Rechtliche Anforderungen

> **Keine Rechtsberatung.** Dieser Überblick ordnet ein, welche Regeln für ProjectHub typischerweise gelten und was die Software dafür mitbringt. Er ersetzt weder Datenschutzbeauftragte noch eine Anwältin oder einen Anwalt. Stand: Oktober 2026, Betrieb in Deutschland/EU, Nutzung durch Beschäftigte einer Organisation.

## Kurz gesagt

- ProjectHub verarbeitet **personenbezogene Daten von Beschäftigten** (Name, E-Mail, Abteilung, wer was wann getan hat, Kommentare, Zuweisungen). Damit gelten **DSGVO und BDSG** vollständig. Das ist der mit Abstand wichtigste Teil.
- **Verantwortlich** ist, wer ProjectHub betreibt (in der Regel der Arbeitgeber), nicht die Software. Sie kann die Pflichten nur leichter erfüllbar machen (Art. 25 DSGVO, Datenschutz durch Technikgestaltung). Dokumente, Verträge und Abstimmungen bleiben beim Betreiber.
- Gibt es einen **Betriebsrat**, muss er vor der Einführung zustimmen (§ 87 Abs. 1 Nr. 6 BetrVG): Aktivitätsprotokoll und Audit-Log sind objektiv geeignet, Verhalten und Leistung zu überwachen.
- **Cookie-Banner** braucht ProjectHub nicht, solange nichts außer der Anmeldung im Browser gespeichert wird (§ 25 Abs. 2 Nr. 2 TDDDG).
- Ein **Impressum** ist für ein internes Werkzeug hinter einer Anmeldung in der Regel nicht nötig (§ 5 DDG gilt für geschäftsmäßige, meist entgeltliche Angebote). Ein Link lässt sich trotzdem einblenden.
- **KI-Funktionen** fallen unter die EU-KI-Verordnung (AI Act). Heute enthält ProjectHub keine; was beim Einbau gilt, steht unten.
- Nutzt jemand ProjectHub **rein privat oder familiär**, gilt die DSGVO nicht (Art. 2 Abs. 2 lit. c). Sobald eine Firma, ein Verein oder Kunden dabei sind, gilt sie.

## Was ProjectHub dafür mitbringt

| Pflicht | Rechtsgrundlage | In ProjectHub | Beim Betreiber |
|---|---|---|---|
| Informieren | Art. 13 DSGVO | Links „Datenschutz“ und „Impressum“ unten auf jeder Seite (`PROJECTHUB_PRIVACY_NOTICE_URL`, `PROJECTHUB_IMPRINT_URL`). Auf dem eigenen Server liefert Caddy die Seiten unter `/rechtliches/` ohne Anmeldung aus. Vorlage: `deploy/legal/datenschutz.vorlage.html` | Vorlage ausfüllen und ablegen (siehe Checkliste) |
| Auskunft, Datenübertragbarkeit | Art. 15, 20 DSGVO | „Meine Daten herunterladen“: alles, was ProjectHub zur Person speichert, als JSON (`GET /api/v1/me/data-export`). Jeder Export steht im Audit-Log | Anfragen binnen eines Monats beantworten; Daten in Entra ID, Microsoft 365, Webex und Sicherungen gehören zusätzlich dazu |
| Berichtigung | Art. 16 DSGVO | Name und E-Mail kommen beim ersten Login aus Entra ID | Änderungen in Entra werden **nicht** nachgezogen (DEC-036); bis dahin per SQL korrigieren |
| Löschung | Art. 17 DSGVO | Organisations-Admins anonymisieren Personen (Teams → „Person anonymisieren“). Name, E-Mail, Abteilung und Entra-Kennung werden überschrieben, Mitgliedschaften, Benachrichtigungen, Mails und Freigaben gelöscht, zugewiesene Aufgaben frei. Inhalte bleiben mit „Ehemalige Person“ als Autor (DEC-034) | Löschkonzept: wann wird wer anonymisiert (z. B. beim Austritt). Sicherungen enthalten die Person noch bis zu 14 Tage |
| Speicherbegrenzung | Art. 5 Abs. 1 lit. e DSGVO | Löschfristen (DEC-005): Benachrichtigungen 180, Projektaktivität 365, Audit-Log 730 Tage, einstellbar; versendete Mails 7, fehlgeschlagene 30 Tage; Webhook-Ereignisse 30 Tage; Sicherungen 14 Tage; Container-Logs höchstens 5 × 10 MB je Dienst | Fristen bestätigen oder anpassen und ins Verarbeitungsverzeichnis übernehmen |
| Datenminimierung | Art. 5 Abs. 1 lit. c DSGVO | Mails und Webex-Nachrichten nur mit Titel und Link; Logs und Telemetrie nur mit IDs; Zugriffslog des Web-Containers ohne IP-Adresse und ohne Query-String (dort stünde sonst das Anmelde-Token der Live-Verbindungen) | – |
| Sicherheit | Art. 32 DSGVO | Anmeldung über Entra ID (MFA dort), Rechte serverseitig, TLS/HSTS/CSP, Rate Limits, Audit-Log, Abhängigkeits-, Container- und Code-Scans (`docs/SECURITY.md`, ADR 0012) | TOMs dokumentieren (`docs/legal/verarbeitungsverzeichnis.md`), Server aktuell halten, Platte verschlüsseln, Sicherungen verschlüsselt außer Haus |
| Verzeichnis der Verarbeitungstätigkeiten | Art. 30 DSGVO | Vorlage mit den Daten von ProjectHub: `docs/legal/verarbeitungsverzeichnis.md` | Eintrag anlegen |
| Auftragsverarbeitung | Art. 28 DSGVO | – | Verträge mit Microsoft (Entra ID, Mail über Graph; DPA ist Teil der Produktbedingungen), Cisco (Webex, falls genutzt), dem Server-Hoster und dem Ziel der Sicherungen |
| Drittländer | Art. 44 ff. DSGVO | ProjectHub selbst überträgt nichts außer an Microsoft und Webex | Microsoft und Cisco sind US-Unternehmen: Data Privacy Framework bzw. Standardvertragsklauseln prüfen; bei Microsoft die EU Data Boundary nutzen |
| Datenschutz-Folgenabschätzung | Art. 35 DSGVO | – | Schwellwertprüfung. Bei Auswertungen über Personen oder KI-Funktionen wahrscheinlich nötig |
| Datenpannen | Art. 33, 34 DSGVO | Audit-Log und Logs helfen bei der Aufklärung | Meldung an die Aufsicht binnen 72 Stunden; Ablauf festlegen |
| Datenschutzbeauftragte | § 38 BDSG | – | Pflicht ab in der Regel 20 Personen, die ständig mit automatisierter Verarbeitung personenbezogener Daten beschäftigt sind |
| Endgeräte-Zugriff | § 25 TDDDG | Im Browser liegt nur das Anmelde-Token (`sessionStorage`, endet mit dem Tab). Keine Tracker, keine Analyse, keine fremden Schriften oder CDNs | Wer Analyse-Werkzeuge einbaut, braucht vorher eine Einwilligung |
| Impressum | § 5 DDG | Link einblendbar | Nur bei geschäftsmäßigem Angebot nach außen nötig |
| Mitbestimmung | § 87 Abs. 1 Nr. 6 BetrVG (öffentlicher Dienst: Personalvertretungsrecht) | Keine Leistungsauswertungen oder Ranglisten; Aktivität sehen nur Projektmitglieder, Audit-Log nur Admins per Datenbank; Löschfristen | Betriebsvereinbarung vor der Einführung (Zweck, Zugriff, Auswertungsverbot, Fristen) |

### Rechtsgrundlage für Beschäftigtendaten

Lange galt § 26 Abs. 1 BDSG als Grundlage. Seit dem Urteil des EuGH vom 30.03.2023 (C-34/21) zur gleichlautenden hessischen Regel ist umstritten, ob er allein trägt. Üblich ist deshalb, sich direkt auf Art. 6 Abs. 1 lit. b DSGVO (Durchführung des Arbeitsverhältnisses) und lit. f (berechtigtes Interesse an Zusammenarbeit und Nachvollziehbarkeit) zu stützen, ergänzt um eine Betriebsvereinbarung (Art. 88 DSGVO). Welche Grundlage gilt, legt der Betreiber fest; die Vorlage der Datenschutzhinweise nennt diese Variante als Vorschlag.

### KI-Funktionen (EU-KI-Verordnung)

ProjectHub enthält heute keine KI-Funktion. Für den geplanten Ausbau gilt:

- **KI-Kompetenz** (Art. 4, seit 02.02.2025): Wer KI-Funktionen im Betrieb einsetzt, sorgt dafür, dass die Nutzenden ausreichend geschult sind.
- **Transparenz** (Art. 50): Erkennbar machen, dass eine Antwort von einer KI stammt. Die Regel aus `AGENTS.md`, Antworten mit Quellen zu versehen, hilft zusätzlich.
- **Hochrisiko** (Anhang III Nr. 4): KI, die Aufgaben nach individuellem Verhalten oder persönlichen Merkmalen zuweist oder Leistung und Verhalten von Beschäftigten überwacht oder bewertet, ist Hochrisiko-KI mit umfangreichen Pflichten. ProjectHub sollte solche Funktionen nicht anbieten (keine Bewertung von Personen, keine Vorschläge „wer ist am produktivsten“). Den Zeitplan der Hochrisiko-Pflichten passt die EU derzeit an; vor dem Einbau aktuellen Stand prüfen.
- **Datenschutz**: Der KI-Anbieter ist Auftragsverarbeiter (AV-Vertrag, Verarbeitung in der EU, kein Training mit den Daten). KI darf nur Inhalte sehen, die die fragende Person sehen darf (ADR 0006). Meist ist eine Datenschutz-Folgenabschätzung nötig, und der Betriebsrat ist erneut zu beteiligen.

### Weitere Punkte

- **Barrierefreiheit:** Das Barrierefreiheitsstärkungsgesetz gilt für Produkte und Dienste für Verbraucher, nicht für ein internes Werkzeug. Öffentliche Stellen müssen die BITV 2.0 einhalten. Unabhängig davon steht Barrierefreiheit in den Prioritäten von `AGENTS.md`.
- **Aufbewahrungspflichten (HGB, AO):** ProjectHub ist kein Archiv für Handelsbriefe oder Buchungsbelege. Was aufbewahrt werden muss, gehört in das dafür vorgesehene System.
- **NIS2:** Fällt das Unternehmen unter das NIS2-Umsetzungsgesetz, gehört ProjectHub in dessen Risikomanagement (Patchen, Sicherungen, Meldewege).
- **Open-Source-Lizenzen:** Die Abhängigkeiten stehen unter freien Lizenzen (überwiegend MIT und Apache 2.0). Beim Betrieb für die eigene Organisation wird nichts weitergegeben; wer ProjectHub an Dritte weitergibt oder verkauft, muss deren Lizenzhinweise mitliefern.

## Checkliste für den eigenen Server

1. **Datenschutzhinweise:** `mkdir -p legal && cp deploy/legal/datenschutz.vorlage.html legal/datenschutz.html`, alle Stellen in `[eckigen Klammern]` ausfüllen. `PROJECTHUB_PRIVACY_NOTICE_URL=/rechtliches/datenschutz.html` steht schon in `.env.prod.example`. Der Ordner `legal/` ist nicht im Repository und übersteht `git pull`.
2. **Impressum** nur, wenn nötig: eigene Seite als `legal/impressum.html` oder Link auf das bestehende, dann `PROJECTHUB_IMPRINT_URL` setzen.
3. **Löschfristen** bestätigen (DEC-005) oder in `.env` ändern; `0` bewahrt unbegrenzt auf.
4. **Verzeichnis der Verarbeitungstätigkeiten** aus `docs/legal/verarbeitungsverzeichnis.md` übernehmen.
5. **Verträge:** Microsoft (Entra ID, ggf. Mail), Webex (falls genutzt), Server-Hoster, Ziel der Sicherungen.
6. **Betriebsrat** beteiligen, falls vorhanden.
7. **Sicherungen** verschlüsselt an einen zweiten Ort (z. B. `restic`); Löschungen wirken dort erst, wenn alte Sicherungen herausfallen.
8. **Entra ID:** „Zuweisung erforderlich“ einschalten, damit nur vorgesehene Personen ProjectHub nutzen (`docs/SELF_HOSTING.md`, Schritt 1.7).

Technische Entscheidungen: ADR 0015.
