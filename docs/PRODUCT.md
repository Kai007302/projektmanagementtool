# Product Specification

## Ziel

ProjectHub ist eine browserbasierte interne Projektmanagement- und Collaboration-Plattform für ca. 6.000 Mitarbeitende.

## Kernfunktionen

- Organisationen, Abteilungen, Benutzer
- Projekte
- Aufgaben und Subtasks
- Kommentare und @Mentions
- Kanban
- Gantt
- Benachrichtigungen
- Activity Feed
- Audit Log
- Anhänge
- Suche
- kollaboratives Whiteboard mit Vorlagen für gängige Darstellungen (Mindmap, Flussdiagramm, Retrospektive, SWOT u. a.) und einem Notizstapel wie in Miro (16 Farben, Ziehen aufs Board, Taste N, Notiz daneben per Klick, mehrere Notizen auf einmal)
- Knowledge Hub
- Knowledge Galaxy (animierte Knowledge-Graph-Darstellung)
- Knowledge Articles, How-Tos, Best Practices, Prozesse, FAQs, Checklisten und Templates
- Beziehungen zwischen Wissen, Projekten, Tasks, Abteilungen und Whiteboards
- semantische Suche / AI-RAG als optional aktivierbare Schicht

## Integrationen

- Microsoft Entra ID
- Microsoft Graph / Outlook Mail
- optionale Outlook-Kalenderaktionen
- Webex Meetings
- Webex Spaces
- Webex Webhooks

## Knowledge Hub Prinzipien

Knowledge ist eine First-Class-Domain neben Projects und Tasks. Der Knowledge Hub muss besonders einfach zu pflegen sein und gleichzeitig eine hochwertige, moderne Darstellung ermöglichen.

Der Knowledge Galaxy ist ausschließlich eine visuelle Navigation auf den Knowledge Graph. Die Datenquelle bleibt die strukturierte Knowledge-Domain.

Knowledge Article Typen:

- Artikel
- How-To
- Best Practice
- Prozess
- Richtlinie
- FAQ
- Vorlage
- Checkliste
- Glossar

Knowledge kann bidirektional verknüpft werden mit:

- Projects
- Tasks
- Teams
- Whiteboards
- anderen Knowledge Articles

AI-Funktionen dürfen nur Inhalte verwenden, auf die der aktuelle Benutzer Zugriff hat. AI-Antworten müssen die verwendeten Knowledge-Quellen referenzieren.

## Nicht in V1

- eigener Kalender
- eigenes Mail-/Chat-System
- native Mobile Apps
- Microservice-Landschaft
- komplette Dokumentenplattform
- KI-Funktionen als technische Voraussetzung

## Domänenprinzip

Projekt und Task sind die fachliche Quelle der Wahrheit.

Kanban und Gantt sind Ansichten auf Tasks.

Whiteboard-Objekte können Tasks referenzieren, replizieren deren Business-State aber nicht.

Outlook und Webex bleiben externe Systeme.
