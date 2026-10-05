# Rollen und Berechtigungen

Zentral definiert in `src/ProjectHub.Api/Modules/Identity/Authorization/`. Geprüft wird immer serverseitig über `IProjectHubAuthorization`, und immer zuerst innerhalb der Organisation des aufrufenden Benutzers. Ressourcen anderer Organisationen erscheinen als „nicht gefunden“ (404), nie als „verboten“.

## Organisationsrollen (`app_user.organization_role`)

| Rolle | Bedeutung |
|---|---|
| `admin` (OrganizationAdmin) | verwaltet die Organisation und Teams, hat alle Projektrechte auf allen Projekten der eigenen Organisation |
| `member` | Standard; Rechte ergeben sich aus Team- und Projektmitgliedschaften |

## Projektrollen (`project_member.role`)

| Rolle | View | Contribute | Edit | Manage |
|---|---|---|---|---|
| `admin` (ProjectAdmin) | ✓ | ✓ | ✓ | ✓ |
| `editor` (ProjectEditor) | ✓ | ✓ | ✓ | |
| `member` (ProjectMember) | ✓ | ✓ | | |
| `viewer` (ProjectViewer) | ✓ | | | |
| `guest` (Guest) | ✓ | | | |

- **View**: Projekt und Inhalte lesen
- **Contribute**: an Inhalten arbeiten (Tasks, Kommentare)
- **Edit**: Projektdaten und Struktur ändern
- **Manage**: Einstellungen, Mitglieder, Löschen

Ohne Mitgliedschaft (und ohne Organisations-Admin-Rolle) gibt es keinen Zugriff auf ein Projekt. Die Abgrenzung `member`/`editor` ist ein Default-Vorschlag (DEC-014).

## Projekte, Aufgaben, Kommentare, Dateien

| Aktion | benötigtes Recht |
|---|---|
| Projekt anlegen | jeder aktive Benutzer der Organisation; wird dabei Projekt-`admin` (DEC-016) |
| Projekt, Aufgaben, Kommentare, Dateien, Aktivität lesen | View |
| Projektdaten ändern (Name, Beschreibung, Status, Termine) | Edit |
| Mitglieder verwalten, Projekt löschen | Manage |
| Aufgabe anlegen und ändern (inkl. Status, Zuständigkeit, Unteraufgaben) | Contribute |
| Aufgabe löschen (inkl. Unteraufgaben) | Edit |
| Zuständig sein | die Person braucht selbst Contribute im Projekt |
| Kommentieren, Datei hochladen | Contribute |
| Kommentar bearbeiten | nur die Autorin/der Autor |
| Kommentar löschen | Autorin/Autor oder Manage |
| Datei löschen | wer sie hochgeladen hat oder Manage |
| Erwähnen (`@`) | nur Mitglieder des Projekts |

| Kanban-Board ansehen | View |
| Karten verschieben (ändert den Status der Aufgabe) | Contribute |
| Spalten anlegen, ändern, sortieren, löschen | Edit |
| Gantt ansehen | View |
| Termine im Gantt verschieben (ändert Start/Ende der Aufgabe), Abhängigkeiten anlegen und löschen | Contribute (DEC-024) |
| Meilensteine anlegen, ändern, löschen | Edit (DEC-024) |
| Whiteboards ansehen, live mitverfolgen, Mauszeiger der anderen sehen und eigenen zeigen | View (DEC-027) |
| Auf Whiteboards zeichnen (Updates schicken) | Contribute (DEC-027, bei jedem Update geprüft) |
| Whiteboards anlegen, umbenennen, löschen | Edit (DEC-027) |
| Realtime-Benachrichtigungen eines Projekts empfangen | View (geprüft beim Beitritt zur Projektgruppe) |

- Ein Projekt behält immer mindestens einen `admin`; die letzte Admin-Rolle kann nicht entzogen werden (409).
- Gelöschte Projekte und Aufgaben (Soft Delete) sind für alle „nicht gefunden“ (404).
- Änderungen per `PATCH` brauchen die aktuelle `version`; ist sie veraltet, antwortet die API mit 409 und ändert nichts.
- Protokolliert werden Anlage/Änderung/Löschung im `audit_log`, sichtbare Änderungen zusätzlich im Aktivitätsfeed des Projekts (`activity_log`).

## Teams (`team_member.role`)

| Aktion | Wer |
|---|---|
| Teams und Mitglieder ansehen | alle Benutzer der Organisation |
| Team anlegen | OrganizationAdmin |
| Mitglieder hinzufügen/entfernen | OrganizationAdmin, Team-`owner` |

Team-Anlage und Mitgliederänderungen werden im `audit_log` protokolliert (`TeamCreated`, `TeamMemberAdded`, `TeamMemberRemoved`).

## Wissen (Knowledge Hub)

Rechte je Artikel: Lesen, Bearbeiten, Verwalten. Sie ergeben sich aus (das höchste gilt):

| Quelle | Recht |
|---|---|
| Organisations-Admin | Verwalten (alle Artikel der Organisation) |
| verantwortliche Person (`owner_id`, beim Anlegen die anlegende Person) | Verwalten |
| Freigabe für die Person oder eines ihrer Teams (`knowledge_permission`: `view`/`edit`/`admin`) | Lesen/Bearbeiten/Verwalten |
| Artikel ist veröffentlicht oder archiviert und hat die Sichtbarkeit `organization` | Lesen (DEC-020) |

Entwürfe und Artikel in Prüfung sieht nur, wer eines der ersten drei Rechte hat. Artikel mit Sichtbarkeit `restricted` sind ausschließlich über Freigaben sichtbar.

| Aktion | benötigtes Recht |
|---|---|
| Artikel anlegen | jeder aktive Benutzer der Organisation; wird verantwortliche Person |
| Artikel, Versionen, Beziehungen, Verweise, Kommentare lesen | Lesen |
| Kommentieren | Lesen |
| Inhalt speichern (neue Version), Metadaten, Tags, Beziehungen, Verweise ändern, Version wiederherstellen | Bearbeiten |
| Entwurf ⇄ In Prüfung | Bearbeiten |
| Veröffentlichen, Archivieren, Veröffentlichtes zurück in Entwurf | Verwalten (DEC-021) |
| Sichtbarkeit ändern, Freigaben verwalten, Artikel löschen | Verwalten |
| Kommentar bearbeiten | nur die Autorin/der Autor |
| Kommentar löschen | Autorin/Autor oder Verwalten |
| Erwähnen (`@`) | nur Personen, die den Artikel lesen dürfen |
| Wissensbereiche anlegen und ändern | Organisations-Admin |
| Knowledge Galaxy | zeigt nur lesbare Artikel und nur Beziehungen zwischen ihnen |

- Nicht sichtbare Artikel sind „nicht gefunden“ (404), sichtbare ohne ausreichendes Recht „verboten“ (403).
- Beziehungen und Verweise zeigen nur Ziele, die die lesende Person selbst sehen darf. Neue Beziehungen und Verweise (auch Verweis-Blöcke im Inhalt) brauchen ein sichtbares Ziel.
- Suche und KI (`IKnowledgeSearch`, `IKnowledgeRetrieval`, die Werkzeuge des Assistenten und des MCP-Servers) bekommen den `KnowledgeReader` und filtern in der Datenbankabfrage; nicht lesbare Inhalte verlassen die Datenbank nicht (ADR 0015).
- Schreibwerkzeuge der KI rufen dieselben Services wie die Endpunkte auf, als die Person: Sie darf über die KI genau das, was sie selbst darf, und nur nach ihrer Freigabe (ADR 0016).
- Statuswechsel, Sichtbarkeit, Freigaben, Anlage und Löschung werden im `audit_log` protokolliert.

## Benachrichtigungen

- Jede Person sieht und ändert nur ihre eigenen Benachrichtigungen und Einstellungen. Fremde Benachrichtigungen sind „nicht gefunden“ (404).
- Benachrichtigt wird nur, wer die Ressource zum Zeitpunkt des Ereignisses sehen darf (zuständig sein und erwähnt werden setzen das bereits voraus). Öffnen prüft die Rechte erneut.
- Über eigene Aktionen gibt es keine Benachrichtigung.
- Mails enthalten nur Titel und Link, keine Kommentartexte (ADR 0008).
- Der Realtime-Hub `/api/v1/hubs/notifications` stellt nur an die angemeldete Person zu und überträgt nur den Zähler.
- Den Zustand der Mail-Warteschlange (`/admin/mail-outbox`) sehen nur Organisations-Admins, und nur für ihre Organisation. Antworten enthalten weder Adressen noch Betreff. Erneutes Versenden einer fehlgeschlagenen Mail steht im `audit_log`.

## Webex

- Webex-Links eines Projekts sieht, wer das Projekt sehen darf (View). Links hinzufügen, entfernen und einen Projektraum anlegen braucht Edit.
- Entfernen löscht nur den Link; ein Raum in Webex bleibt bestehen.
- Der Bot lädt nur aktive Mitglieder des Projekts aus derselben Organisation ein.
- Webex-Nachrichten enthalten wie Mails nur Titel und Link.
- Den Webex-Zustand (`/admin/webex`) sehen und den Webhook registrieren (`/admin/webex/webhook`) nur Organisations-Admins; die Registrierung steht im `audit_log`.
- Der Webhook-Endpunkt hat keinen Benutzer, nimmt aber nur Anfragen mit gültiger Signatur an (sonst 401).

## Kalender

- Kalenderdateien für Aufgaben und Meilensteine bekommt, wer das Projekt sehen darf (View). Sonst „nicht gefunden“ (404).
- Sie enthalten nur Titel, Datum und einen Link in die App.

## Identität

- Außerhalb von Development: Microsoft Entra ID. Die API validiert Bearer-Tokens gegen `https://login.microsoftonline.com/{ENTRA_TENANT_ID}/v2.0` mit Audience `ENTRA_CLIENT_ID`. Die App-Registrierung selbst ist ein Human Review Gate; ohne sie ist kein Login möglich (401). Der Login-Flow im Frontend (MSAL) folgt, sobald die Registrierung existiert.
- Development: ein gekapselter Development-Identity-Provider meldet einen der synthetischen Benutzer aus `DevelopmentSeedData` an (Header `X-Dev-User`, Standard `dev-ada`). Er stellt dieselben Claims wie Entra aus (`oid`, `tid`, `name`, `preferred_username`) und verweigert den Start außerhalb von Development.
- Realtime (SignalR, `/api/v1/hubs/projects`): Browser können bei WebSockets keine Header senden. Mit Entra kommt das Token deshalb als `access_token` in der Query, im Development-Modus der synthetische Benutzer als `devUser`. Beides gilt nur für Pfade unter `/api/v1/hubs`. Nachrichten enthalten nur die Projekt-ID; Inhalte lädt der Client über die autorisierte API.
- Benutzer werden nur angelegt, wenn `PROJECTHUB_USER_PROVISIONING=first-sign-in` gesetzt ist (DEC-013, ADR 0014): dann beim ersten Login, die erste Person einer Organisation als `admin`, alle weiteren als `member`. Sonst und für gesperrte (`inactive`) Benutzer gilt: Authentifizierte Konten ohne aktiven ProjectHub-Benutzer erhalten 403.
