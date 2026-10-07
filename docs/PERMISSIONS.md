# Rollen und Berechtigungen

Zentral definiert in `src/ProjectHub.Api/Modules/Identity/Authorization/`. Geprüft wird immer serverseitig über `IProjectHubAuthorization`, und immer zuerst innerhalb der Organisation des aufrufenden Benutzers. Ressourcen anderer Organisationen erscheinen als „nicht gefunden“ (404), nie als „verboten“.

## Organisationsrollen (`app_user.organization_role`)

| Rolle | Bedeutung |
|---|---|
| `admin` (OrganizationAdmin) | verwaltet die Organisation und ihre Abteilungen, hat alle Rechte auf allen Projekten und Artikeln der eigenen Organisation. Zugriff auf Projekte und Artikel, die er nur als Admin sieht, steht im `audit_log` (`OrganizationAdminAccess`, ADR 0021) |
| `member` | Standard; Rechte ergeben sich aus Abteilungs- und Projektmitgliedschaften |

## Abteilungen (`department_member.role`, ADR 0021)

| Rolle | Bedeutung |
|---|---|
| `lead` (Abteilungsleitung) | verwaltet die Abteilung und ihre Personen, hat alle Rechte auf allen Projekten und Artikeln der Abteilung, auch ohne Projektmitgliedschaft; gibt Projekte und Artikel der Abteilung für die ganze Organisation frei |
| `member` | legt Projekte und Artikel in der Abteilung an, liest alles mit Sichtbarkeit „Abteilung“ |
| `guest` | sieht nur Projekte, zu denen er eingeladen ist, und kein Wissen der Abteilung |

| Aktion | Wer |
|---|---|
| Namen der Abteilungen sehen (z. B. zum Freigeben) | alle Benutzer der Organisation |
| Personen einer Abteilung sehen | ihre Leitungen, Mitglieder und Gäste, Organisations-Admins |
| Abteilung anlegen, löschen (nur wenn leer), mit Entra-Gruppe verbinden | OrganizationAdmin |
| Name und Beschreibung ändern, Personen hinzufügen, Rollen ändern, entfernen | OrganizationAdmin, Leitung der Abteilung |
| Personen ohne Abteilung sehen (`/departments/unassigned`) | OrganizationAdmin, jede Abteilungsleitung |

- Mit `PROJECTHUB_DEPARTMENTS_FROM_ENTRA_GROUPS=true` kommen Mitglieder verbundener Entra-Gruppen bei der Anmeldung in die Abteilung und gehen, wenn sie die Gruppe verlassen. Von Hand hinzugefügte Mitgliedschaften bleiben unberührt.
- Anlage, Änderung, Löschung und jede Mitgliedschaftsänderung (auch aus Entra) stehen im `audit_log` (`DepartmentCreated`, `DepartmentUpdated`, `DepartmentDeleted`, `DepartmentMemberAdded`, `DepartmentMemberRoleChanged`, `DepartmentMemberRemoved`).

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

Dazu kommt die Abteilung des Projekts (ADR 0021): Ihre Leitung hat alle Rechte. Je nach Sichtbarkeit des Projekts haben weitere Personen das Recht View:

| Sichtbarkeit (`project.visibility`) | lesen dürfen außerdem |
|---|---|
| `private` | niemand (nur Projektmitglieder, Leitung, Admins) |
| `department` (Standard für neue Projekte) | Mitglieder der Abteilung (keine Gäste) |
| `organization` | alle aktiven Benutzer der Organisation |

Bestehende Projekte sind nach Migration 016 `private`. Die Abgrenzung `member`/`editor` ist ein Default-Vorschlag (DEC-014).

## Projekte, Aufgaben, Kommentare, Dateien

| Aktion | benötigtes Recht |
|---|---|
| Projekt anlegen | Leitung und Mitglieder der gewählten Abteilung, Organisations-Admins; wird dabei Projekt-`admin` (DEC-016) |
| Sichtbarkeit und Abteilung ändern | Manage; verschieben nur in eine Abteilung, in der man Leitung oder Mitglied ist; `organization` nur Leitung der Abteilung und Organisations-Admins (ADR 0021) |
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
| Aufgaben als Excel/CSV exportieren | View (ADR 0018) |
| Aufgaben aus Excel/CSV importieren (legt neue Aufgaben an) | Contribute (ADR 0018) |
| Gantt ansehen, als PDF herunterladen | View |
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

## Wissen (Knowledge Hub)

Rechte je Artikel: Lesen, Bearbeiten, Verwalten. Sie ergeben sich aus (das höchste gilt):

| Quelle | Recht |
|---|---|
| Organisations-Admin | Verwalten (alle Artikel der Organisation) |
| Leitung der Abteilung des Artikels | Verwalten (ADR 0021) |
| verantwortliche Person (`owner_id`, beim Anlegen die anlegende Person) | Verwalten |
| Freigabe für die Person oder eine ihrer Abteilungen als Leitung oder Mitglied (`knowledge_permission`: `view`/`edit`/`admin`) | Lesen/Bearbeiten/Verwalten |
| Artikel ist veröffentlicht oder archiviert, Sichtbarkeit `department`, Person ist Leitung oder Mitglied der Abteilung | Lesen (ADR 0021) |
| Artikel ist veröffentlicht oder archiviert und hat die Sichtbarkeit `organization` | Lesen (DEC-020) |

Entwürfe und Artikel in Prüfung sieht nur, wer eines der ersten vier Rechte hat. Artikel mit Sichtbarkeit `restricted` sind ausschließlich über Freigaben sichtbar. Gäste einer Abteilung sehen ihr Wissen nicht.

| Aktion | benötigtes Recht |
|---|---|
| Artikel anlegen | Leitung und Mitglieder der Abteilung (bzw. der Abteilung des Bereichs), Organisations-Admins; wird verantwortliche Person |
| Artikel, Versionen, Beziehungen, Verweise, Kommentare lesen | Lesen |
| Kommentieren | Lesen |
| Inhalt speichern (neue Version), Metadaten, Tags, Beziehungen, Verweise ändern, Version wiederherstellen | Bearbeiten |
| Entwurf ⇄ In Prüfung | Bearbeiten |
| Veröffentlichen, Archivieren, Veröffentlichtes zurück in Entwurf | Verwalten (DEC-021) |
| Sichtbarkeit ändern, Freigaben verwalten, Artikel löschen | Verwalten; Sichtbarkeit `organization` nur Leitung der Abteilung und Organisations-Admins (ADR 0021) |
| Kommentar bearbeiten | nur die Autorin/der Autor |
| Kommentar löschen | Autorin/Autor oder Verwalten |
| Erwähnen (`@`) | nur Personen, die den Artikel lesen dürfen |
| Wissensbereiche anlegen und ändern | Organisations-Admin, Leitung der Abteilung des Bereichs |
| Wissensbereiche sehen | Leitung, Mitglieder der Abteilung, Organisations-Admins |
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

## Datenschutz

- Den Export der eigenen Daten (`/me/data-export`) bekommt jede angemeldete Person, nur für sich selbst. Jeder Export steht im `audit_log`.
- Personen anonymisieren (`/admin/users/{id}/anonymize`, in der Oberfläche unter „Verwaltung“) dürfen nur Organisations-Admins, nur in ihrer Organisation (sonst 404) und nie sich selbst (400). Die Anonymisierung steht im `audit_log`.
- Die Links auf Datenschutzhinweise und Impressum (`/legal`) sind ohne Anmeldung lesbar.

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
- Das Kalender-Abo (ADR 0018) verwaltet jede Person nur für sich selbst (`/me/calendar-feed`). Abgerufen wird es ohne Anmeldung mit dem Token aus der Adresse; es enthält nur, was die Person zu diesem Zeitpunkt sehen darf: ihr zugewiesene Aufgaben in sichtbaren Projekten und Meilensteine der Projekte, in denen sie Mitglied ist. Inaktive oder anonymisierte Personen: 404.
- Den Kalender eines Projekts (ADR 0020, `/projects/{id}/calendar-feed`) kann abonnieren, wer das Projekt sehen darf (View); jede Person hat dafür ihre eigene Adresse. Er enthält alle datierten Aufgaben und Meilensteine des Projekts. Beim Abruf wird das Leserecht der Person neu geprüft; wer es verloren hat, bekommt 404.

## Projektsymbol und Logo

- Symbol und Logo sieht, wer das Projekt sehen darf (View). Ändern, hochladen und entfernen braucht Edit (ADR 0020).
- Logos nur als PNG, JPEG oder WebP bis 256 KB, erkannt am Dateiinhalt; SVG wird abgelehnt.

## Identität

- Außerhalb von Development: Microsoft Entra ID. Die API validiert Bearer-Tokens gegen `https://login.microsoftonline.com/{ENTRA_TENANT_ID}/v2.0` mit Audience `ENTRA_CLIENT_ID`. Die App-Registrierung selbst ist ein Human Review Gate; ohne sie ist kein Login möglich (401). Der Login-Flow im Frontend (MSAL) folgt, sobald die Registrierung existiert.
- Development: ein gekapselter Development-Identity-Provider meldet einen der synthetischen Benutzer aus `DevelopmentSeedData` an (Header `X-Dev-User`, Standard `dev-ada`). Er stellt dieselben Claims wie Entra aus (`oid`, `tid`, `name`, `preferred_username`) und verweigert den Start außerhalb von Development.
- Realtime (SignalR, `/api/v1/hubs/projects`): Browser können bei WebSockets keine Header senden. Mit Entra kommt das Token deshalb als `access_token` in der Query, im Development-Modus der synthetische Benutzer als `devUser`. Beides gilt nur für Pfade unter `/api/v1/hubs`. Nachrichten enthalten nur die Projekt-ID; Inhalte lädt der Client über die autorisierte API.
- Benutzer werden nur angelegt, wenn `PROJECTHUB_USER_PROVISIONING=first-sign-in` gesetzt ist (DEC-013, ADR 0014): dann beim ersten Login, die erste Person einer Organisation als `admin`, alle weiteren als `member`. Name und E-Mail übernimmt die API bei jeder Anmeldung aus dem Token, wenn sie sich in Entra geändert haben (DEC-039); Rolle und Status nie. Die erste Person wird zusätzlich Leitung der Abteilung „Allgemein“. Wer beim ersten Login in keine Abteilung kommt, wird Admins und Abteilungsleitungen gemeldet (ADR 0021). Sonst und für gesperrte (`inactive`) Benutzer gilt: Authentifizierte Konten ohne aktiven ProjectHub-Benutzer erhalten 403.
