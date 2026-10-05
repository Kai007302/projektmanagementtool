# Vorlage: Eintrag im Verzeichnis von Verarbeitungstätigkeiten (Art. 30 DSGVO)

> Keine Rechtsberatung. Vorlage mit dem, was ProjectHub technisch tut (Stand ADR 0017). Stellen in `[eckigen Klammern]` füllt der Betreiber aus.

| Feld | Inhalt |
|---|---|
| Bezeichnung | ProjectHub – Projekt- und Wissensmanagement |
| Verantwortlicher | [Organisation, Anschrift, Vertretung] |
| Datenschutzbeauftragte/r | [Kontakt oder „nicht benannt“] |
| Zwecke | Planung und Steuerung von Projekten und Aufgaben, Zusammenarbeit (Kommentare, Whiteboards), Wissensdokumentation, Benachrichtigungen, Nachvollziehbarkeit von Änderungen, IT-Sicherheit |
| Rechtsgrundlagen | Art. 6 Abs. 1 lit. b und f DSGVO[, Art. 88 DSGVO mit Betriebsvereinbarung „[Titel]“] |
| Betroffene | Beschäftigte[, externe Projektbeteiligte mit Gastzugang] |
| Datenkategorien | Stammdaten (Name, E-Mail, Microsoft-Kontokennung, Abteilung, Rolle); Mitgliedschaften; erstellte Inhalte (Aufgaben, Kommentare, Anhänge, Whiteboards, Wissensartikel); Aktivitäts- und Audit-Protokolle (wer, was, wann); Benachrichtigungen; technische Protokolle ohne IP-Adresse. Keine besonderen Kategorien nach Art. 9 vorgesehen |
| Empfänger | Berechtigte Personen in ProjectHub; Auftragsverarbeiter: Microsoft (Entra ID, ggf. Mail über Microsoft Graph), [Cisco Webex], [Server-Hoster], [Ziel der Sicherungen] |
| Drittlandübermittlung | Microsoft, [Cisco]: USA möglich; EU-US Data Privacy Framework bzw. Standardvertragsklauseln; Microsoft EU Data Boundary [genutzt/nicht genutzt] |
| Löschfristen | Benachrichtigungen, Projektaktivität und Audit-Log je 3 Jahre (1095 Tage, einstellbar, `PROJECTHUB_RETENTION_*`); Mail-Warteschlange 7 Tage (versendet) bzw. 30 Tage (fehlgeschlagen); Webhook-Ereignisse 30 Tage; Container-Logs 5 × 10 MB je Dienst; Sicherungen 14 Tage; Personen: Anonymisierung [beim Ausscheiden / binnen Frist] |
| Technische und organisatorische Maßnahmen | siehe unten |

## Technische und organisatorische Maßnahmen (Art. 32 DSGVO)

Was ProjectHub mitbringt (Details in `docs/SECURITY.md`, ADR 0012–0015):

- **Zugang:** Anmeldung nur über Microsoft Entra ID (keine eigenen Passwörter); MFA und bedingter Zugriff in Entra [aktiviert]; optional „Zuweisung erforderlich“.
- **Zugriff:** Rollen je Organisation, Projekt, Team und Wissensartikel, immer serverseitig geprüft (`docs/PERMISSIONS.md`); Trennung der Organisationen in jeder Abfrage.
- **Übertragung:** TLS mit HSTS über Caddy; Datenbank und Redis nicht von außen erreichbar.
- **Eingabe/Nachvollziehbarkeit:** Aktivitätsprotokoll je Projekt; Audit-Log für Rollen-, Mitglieder-, Lösch- und Datenschutzaktionen (Export, Anonymisierung), für Benutzer nicht änderbar.
- **Härtung:** Content Security Policy ohne fremde Quellen, Rate Limits, Größenlimits, Container ohne Root, Abhängigkeits-, Secret-, Container- und Code-Scans in der CI.
- **Datenminimierung:** Mails und Webex-Nachrichten nur mit Titel und Link; Logs nur mit IDs, Zugriffslog ohne IP-Adresse und Query-String.
- **Verfügbarkeit:** Sicherung per `scripts/backup.sh` [stündlich], Wiederherstellung per `scripts/restore.sh`, Übung in `docs/OPERATIONS.md`.

Beim Betreiber: [Festplattenverschlüsselung des Servers], [verschlüsselte Sicherung außer Haus], [Updates von Betriebssystem und ProjectHub, Rhythmus], [wer Admin-Zugang zum Server hat], [Meldeweg für Datenpannen binnen 72 Stunden].
