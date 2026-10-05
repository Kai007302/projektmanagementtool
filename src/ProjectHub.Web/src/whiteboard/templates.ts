import type { Color, NewObject } from './model'

/**
 * Ready-made layouts for the whiteboard: the ten diagrams most whiteboard tools offer first. A template is only
 * a list of ordinary objects; inserting one copies them onto the board, after that they are edited like any other.
 *
 * Objects are drawn top to bottom, left to right, so a frame always starts above or left of what lies on it.
 */
export type Template = {
  id: string
  name: string
  description: string
  items: NewObject[]
}

const title = (text: string, y = -80): NewObject => ({ type: 'text', x: 0, y, w: 600, h: 40, text })

const label = (x: number, y: number, w: number, text: string, h = 40): NewObject => ({ type: 'text', x, y, w, h, text })

const frame = (x: number, y: number, w: number, h: number, text: string, color: Color = 'white'): NewObject => ({ type: 'rect', x, y, w, h, color, text })

const note = (x: number, y: number, text: string, color: Color = 'yellow', w = 160, h = 90): NewObject => ({ type: 'sticky', x, y, w, h, color, text })

const oval = (x: number, y: number, w: number, h: number, text: string, color: Color): NewObject => ({ type: 'ellipse', x, y, w, h, color, text })

const diamond = (x: number, y: number, w: number, h: number, text: string): NewObject => ({ type: 'diamond', x, y, w, h, color: 'yellow', text })

const arrow = (x: number, y: number, x2: number, y2: number): NewObject => ({ type: 'arrow', x, y, x2, y2, color: 'gray' })

const mindmap: Template = {
  id: 'mindmap',
  name: 'Mindmap',
  description: 'Ein Thema in der Mitte, Gedanken als Äste darum herum. Gut zum Sammeln und Ordnen von Ideen.',
  items: [
    title('Mindmap'),
    frame(60, 60, 180, 70, 'Ziele', 'green'),
    frame(840, 60, 180, 70, 'Ideen', 'green'),
    oval(420, 250, 240, 120, 'Zentrales Thema', 'blue'),
    frame(0, 275, 180, 70, 'Fragen', 'green'),
    frame(900, 275, 180, 70, 'Ressourcen', 'green'),
    frame(60, 490, 180, 70, 'Risiken', 'green'),
    frame(840, 490, 180, 70, 'Nächste Schritte', 'green'),
    arrow(450, 275, 240, 110),
    arrow(630, 275, 840, 110),
    arrow(420, 310, 180, 310),
    arrow(660, 310, 900, 310),
    arrow(450, 345, 240, 510),
    arrow(630, 345, 840, 510),
  ],
}

const flowchart: Template = {
  id: 'flowchart',
  name: 'Flussdiagramm',
  description: 'Ein Ablauf aus Start, Schritten, Entscheidungen und Ende. Für Prozesse und Abläufe.',
  items: [
    title('Ablauf'),
    oval(220, 0, 160, 70, 'Start', 'green'),
    arrow(300, 70, 300, 120),
    frame(200, 120, 200, 80, 'Schritt', 'blue'),
    arrow(300, 200, 300, 250),
    diamond(210, 250, 180, 130, 'Entscheidung?'),
    frame(500, 275, 200, 80, 'Nacharbeiten', 'blue'),
    arrow(600, 275, 400, 175),
    label(410, 270, 80, 'Nein'),
    arrow(390, 315, 500, 315),
    arrow(300, 380, 300, 440),
    label(310, 390, 60, 'Ja'),
    frame(200, 440, 200, 80, 'Nächster Schritt', 'blue'),
    arrow(300, 520, 300, 570),
    oval(220, 570, 160, 70, 'Ende', 'pink'),
  ],
}

const retrospective: Template = {
  id: 'retrospective',
  name: 'Retrospektive',
  description: 'Drei Spalten für den Rückblick im Team: was lief gut, was nicht, was probieren wir als Nächstes.',
  items: [
    title('Retrospektive'),
    frame(0, 0, 340, 480, 'Was lief gut?', 'green'),
    frame(360, 0, 340, 480, 'Was lief nicht gut?', 'pink'),
    frame(720, 0, 340, 480, 'Was probieren wir als Nächstes?', 'blue'),
    note(20, 60, 'Gute Zusammenarbeit'),
    note(380, 60, 'Zu viele Meetings'),
    note(740, 60, 'Daily auf 15 Minuten begrenzen'),
  ],
}

const swot: Template = {
  id: 'swot',
  name: 'SWOT-Analyse',
  description: 'Stärken, Schwächen, Chancen und Risiken in vier Feldern. Für Strategie und Projektstart.',
  items: [
    title('SWOT-Analyse'),
    frame(0, 0, 420, 300, 'Stärken (intern)\nWas können wir besonders gut? Welche Vorteile haben wir?', 'green'),
    frame(440, 0, 420, 300, 'Schwächen (intern)\nWo fehlt uns etwas? Was sollten wir verbessern?', 'pink'),
    frame(0, 320, 420, 300, 'Chancen (extern)\nWelche Entwicklungen können wir nutzen?', 'blue'),
    frame(440, 320, 420, 300, 'Risiken (extern)\nWas könnte uns schaden? Was tut der Wettbewerb?', 'yellow'),
  ],
}

const eisenhower: Template = {
  id: 'eisenhower',
  name: 'Eisenhower-Matrix',
  description: 'Aufgaben nach Wichtigkeit und Dringlichkeit sortieren und entscheiden, was zuerst kommt.',
  items: [
    title('Prioritäten'),
    label(160, 0, 400, 'Dringend'),
    label(580, 0, 400, 'Nicht dringend'),
    frame(160, 50, 400, 260, '1 · Sofort erledigen\nWichtig und dringend', 'pink'),
    frame(580, 50, 400, 260, '2 · Einplanen\nWichtig, aber nicht dringend: Termin festlegen', 'blue'),
    label(0, 160, 150, 'Wichtig'),
    frame(160, 330, 400, 260, '3 · Abgeben\nDringend, aber nicht wichtig: delegieren', 'yellow'),
    frame(580, 330, 400, 260, '4 · Weglassen\nWeder wichtig noch dringend', 'gray'),
    label(0, 425, 150, 'Nicht wichtig', 70),
  ],
}

const stakeholder: Template = {
  id: 'stakeholder',
  name: 'Stakeholder-Matrix',
  description: 'Beteiligte nach Einfluss und Interesse einordnen und festlegen, wie sie eingebunden werden.',
  items: [
    title('Stakeholder', -120),
    label(-10, -60, 140, 'Einfluss'),
    arrow(40, 640, 40, -10),
    frame(60, 0, 420, 300, 'Zufriedenstellen\nHoher Einfluss, geringes Interesse', 'yellow'),
    frame(500, 0, 420, 300, 'Eng einbinden\nHoher Einfluss, hohes Interesse', 'pink'),
    frame(60, 320, 420, 300, 'Beobachten\nGeringer Einfluss, geringes Interesse', 'gray'),
    frame(500, 320, 420, 300, 'Informieren\nGeringer Einfluss, hohes Interesse', 'blue'),
    arrow(40, 640, 930, 640),
    label(790, 650, 140, 'Interesse'),
  ],
}

const ishikawa: Template = {
  id: 'ishikawa',
  name: 'Ursache-Wirkungs-Diagramm',
  description: 'Fischgräte nach Ishikawa: mögliche Ursachen eines Problems nach Kategorien sammeln.',
  items: [
    title('Ursache und Wirkung'),
    frame(60, 20, 180, 60, 'Mensch', 'gray'),
    frame(330, 20, 180, 60, 'Methode', 'gray'),
    frame(600, 20, 180, 60, 'Material', 'gray'),
    arrow(150, 80, 270, 300),
    arrow(420, 80, 540, 300),
    arrow(690, 80, 810, 300),
    frame(900, 240, 220, 120, 'Problem oder Wirkung', 'pink'),
    arrow(0, 300, 900, 300),
    arrow(150, 520, 270, 300),
    arrow(420, 520, 540, 300),
    arrow(690, 520, 810, 300),
    frame(60, 520, 180, 60, 'Maschine', 'gray'),
    frame(330, 520, 180, 60, 'Mitwelt', 'gray'),
    frame(600, 520, 180, 60, 'Messung', 'gray'),
  ],
}

const roadmapLanes = ['Produkt', 'Technik', 'Organisation']

const roadmap: Template = {
  id: 'roadmap',
  name: 'Roadmap',
  description: 'Vorhaben je Bereich über vier Quartale, mit Meilenstein. Für Planung und Abstimmung.',
  items: [
    title('Roadmap'),
    arrow(180, -20, 1220, -20),
    ...['Q1', 'Q2', 'Q3', 'Q4'].map((quarter, i) => frame(180 + i * 260, 0, 240, 50, quarter, 'blue')),
    ...roadmapLanes.flatMap((lane, i) => [frame(0, 70 + i * 160, 160, 140, lane, 'gray'), frame(180, 70 + i * 160, 1040, 140, '')]),
    note(200, 95, 'Prototyp testen', 'yellow', 220),
    note(460, 95, 'Version 1.0', 'yellow', 220),
    note(460, 255, 'Schnittstellen anbinden', 'green', 220),
    note(720, 255, 'Lasttest', 'green', 220),
    note(720, 415, 'Schulungen', 'pink', 220),
    diamond(1020, 400, 140, 100, 'Go-live'),
  ],
}

const journeyPhases = ['Aufmerksamkeit', 'Recherche', 'Entscheidung', 'Nutzung', 'Bindung']

const journeyRows = [
  'Aktivitäten\nWas tut die Person?',
  'Berührungspunkte\nWo trifft sie auf uns?',
  'Gedanken und Gefühle',
  'Probleme',
  'Chancen',
]

const customerJourney: Template = {
  id: 'customer-journey',
  name: 'Customer Journey Map',
  description: 'Den Weg einer Person über alle Phasen festhalten: was sie tut, denkt und wo es hakt.',
  items: [
    title('Customer Journey'),
    arrow(200, -20, 1300, -20),
    ...journeyPhases.map((phase, i) => frame(200 + i * 220, 0, 200, 60, phase, 'blue')),
    ...journeyRows.flatMap((row, i) => [frame(0, 80 + i * 130, 180, 120, row, 'gray'), frame(200, 80 + i * 130, 1100, 120, '')]),
  ],
}

const businessModelCanvas: Template = {
  id: 'business-model-canvas',
  name: 'Business Model Canvas',
  description: 'Ein Geschäftsmodell auf einer Seite: neun Bausteine vom Wertangebot bis zu den Kosten.',
  items: [
    title('Business Model Canvas'),
    frame(0, 0, 280, 440, 'Schlüsselpartner\nWer hilft uns? Welche Lieferanten brauchen wir?'),
    frame(280, 0, 280, 220, 'Schlüsselaktivitäten\nWas müssen wir tun, um das Wertangebot zu liefern?'),
    frame(560, 0, 280, 440, 'Wertangebote\nWelches Problem lösen wir? Welchen Nutzen stiften wir?', 'blue'),
    frame(840, 0, 280, 220, 'Kundenbeziehungen\nWie gewinnen und halten wir Kunden?'),
    frame(1120, 0, 280, 440, 'Kundensegmente\nFür wen schaffen wir Wert?'),
    frame(280, 220, 280, 220, 'Schlüsselressourcen\nWas brauchen wir dafür?'),
    frame(840, 220, 280, 220, 'Kanäle\nWie erreichen wir unsere Kunden?'),
    frame(0, 440, 700, 200, 'Kostenstruktur\nWelche Kosten entstehen?'),
    frame(700, 440, 700, 200, 'Einnahmequellen\nWofür zahlen Kunden?'),
  ],
}

export const templates: Template[] = [
  mindmap,
  flowchart,
  retrospective,
  swot,
  eisenhower,
  stakeholder,
  ishikawa,
  roadmap,
  customerJourney,
  businessModelCanvas,
]
