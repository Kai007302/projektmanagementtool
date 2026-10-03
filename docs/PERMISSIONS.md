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

## Teams (`team_member.role`)

| Aktion | Wer |
|---|---|
| Teams und Mitglieder ansehen | alle Benutzer der Organisation |
| Team anlegen | OrganizationAdmin |
| Mitglieder hinzufügen/entfernen | OrganizationAdmin, Team-`owner` |

Team-Anlage und Mitgliederänderungen werden im `audit_log` protokolliert (`TeamCreated`, `TeamMemberAdded`, `TeamMemberRemoved`).

## Identität

- Außerhalb von Development: Microsoft Entra ID. Die API validiert Bearer-Tokens gegen `https://login.microsoftonline.com/{ENTRA_TENANT_ID}/v2.0` mit Audience `ENTRA_CLIENT_ID`. Die App-Registrierung selbst ist ein Human Review Gate; ohne sie ist kein Login möglich (401). Der Login-Flow im Frontend (MSAL) folgt, sobald die Registrierung existiert.
- Development: ein gekapselter Development-Identity-Provider meldet einen der synthetischen Benutzer aus `DevelopmentSeedData` an (Header `X-Dev-User`, Standard `dev-ada`). Er stellt dieselben Claims wie Entra aus (`oid`, `tid`, `name`, `preferred_username`) und verweigert den Start außerhalb von Development.
- Benutzer werden nicht automatisch angelegt (DEC-013). Authentifizierte Konten ohne aktiven ProjectHub-Benutzer erhalten 403.
