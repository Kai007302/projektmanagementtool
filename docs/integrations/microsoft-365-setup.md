# Microsoft 365 einrichten (für die IT)

ProjectHub verschickt Benachrichtigungsmails über Microsoft Graph aus einem Funktionspostfach (ADR 0010). Bis die Einrichtung abgeschlossen ist, läuft die API mit `PROJECTHUB_MAIL_TRANSPORT=fake` und verschickt nichts.

Kalendereinträge brauchen **keine** Graph-Berechtigung: Die Person lädt eine `.ics`-Datei oder öffnet Outlook im Web mit vorausgefülltem Termin (Vorschlag DEC-028).

## 1. Funktionspostfach (DEC-003)

- Ein Shared Mailbox ohne Lizenz genügt, z. B. `projecthub@firma.de`.
- Antworten auf Benachrichtigungen landen dort. Wer das Postfach liest, legt die Fachabteilung fest.

## 2. App-Registrierung

Eine eigene App-Registrierung nur für den Mailversand, getrennt von der Registrierung, mit der sich Personen anmelden (`ENTRA_CLIENT_ID`).

- Name z. B. „ProjectHub Mailversand“, nur dieser Mandant
- keine Redirect-URI, keine delegierten Berechtigungen
- **Keine** `Mail.Send`-Berechtigung in Entra ID vergeben, wenn RBAC for Applications genutzt wird (Schritt 3). Eine in Entra erteilte `Mail.Send`-Anwendungsberechtigung gilt für alle Postfächer des Mandanten.

## 3. Versand nur aus dem Funktionspostfach erlauben

Empfohlen: **RBAC for Applications in Exchange Online.** Die App erhält die Rolle „Application Mail.Send“ nur für einen Management Scope, der genau das Funktionspostfach enthält.

```powershell
Connect-ExchangeOnline

# Service Principal der App-Registrierung in Exchange bekannt machen
New-ServicePrincipal -AppId <Application (client) ID> -ObjectId <Object ID des Enterprise-App-Service-Principals> -DisplayName "ProjectHub Mailversand"

# Scope: nur das Funktionspostfach
New-ManagementScope -Name "ProjectHub Funktionspostfach" -RecipientRestrictionFilter "PrimarySmtpAddress -eq 'projecthub@firma.de'"

New-ManagementRoleAssignment -App <Application (client) ID> -Role "Application Mail.Send" -CustomResourceScope "ProjectHub Funktionspostfach"

# Prüfen: True für das Funktionspostfach, False für jedes andere
Test-ServicePrincipalAuthorization -Identity <Application (client) ID> -Resource projecthub@firma.de
Test-ServicePrincipalAuthorization -Identity <Application (client) ID> -Resource jemand@firma.de
```

Alternative für ältere Umgebungen: `Mail.Send` (Anwendung) in Entra mit Admin Consent erteilen und mit einer Application Access Policy (`New-ApplicationAccessPolicy -AccessRight RestrictAccess`) auf eine mail-aktivierte Sicherheitsgruppe beschränken, die nur das Funktionspostfach enthält. Microsoft ersetzt dieses Verfahren durch RBAC for Applications.

## 4. Anmeldedaten

Bevorzugt, sobald ProjectHub in Azure läuft: **Managed Identity.** Dann gibt es kein Secret; `MICROSOFT_GRAPH_CLIENT_ID` ist die Client-ID der benutzerzugewiesenen Managed Identity, und Schritt 3 wird für deren Service Principal ausgeführt.

Bis dahin: ein Client Secret der App-Registrierung, abgelegt im Key Vault bzw. Secret Store, nie im Repository. Laufzeit kurz halten und Rotation planen. Läuft das Secret ab, antwortet Graph mit 401; ProjectHub wiederholt die Mails eine Stunde lang und markiert sie danach als fehlgeschlagen. Organisations-Admins stoßen sie über `POST /api/v1/admin/mail-outbox/{id}/retry` neu an.

## 5. Konfiguration der API

| Variable | Wert |
|---|---|
| `PROJECTHUB_MAIL_TRANSPORT` | `graph` |
| `MICROSOFT_GRAPH_TENANT_ID` | Verzeichnis-ID (Mandant) |
| `MICROSOFT_GRAPH_CLIENT_ID` | Client-ID der App-Registrierung bzw. Managed Identity |
| `MICROSOFT_GRAPH_CLIENT_SECRET` | nur aus dem Secret Store; leer lassen bei Managed Identity |
| `MICROSOFT_GRAPH_SENDER_MAILBOX` | Adresse des Funktionspostfachs |
| `PROJECTHUB_APP_URL` | Adresse der Web-App, steht als Link in jeder Mail |

Fehlt bei `graph` ein Pflichtwert, startet die API nicht.

## 6. Prüfen

1. API starten, im Log darf kein Startfehler zu `PROJECTHUB_MAIL_TRANSPORT` stehen.
2. Einer Testperson eine Aufgabe zuweisen. Die Mail kommt aus dem Funktionspostfach, enthält nur Titel und Link und liegt nicht in „Gesendete Elemente“.
3. `GET /api/v1/admin/mail-outbox` als Organisations-Admin: `failed` ist 0. Bei `403 ErrorAccessDenied` fehlt Schritt 3 oder er gilt noch nicht (Exchange braucht bis zu einer Stunde).

## Was ProjectHub nicht tut

- keine Mails im Namen von Personen (DEC-012)
- kein Lesen von Postfächern oder Kalendern
- keine Graph-Aufrufe aus dem Browser
