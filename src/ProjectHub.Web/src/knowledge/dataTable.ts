/** Limits of a data table, the same as the API's (DEC-046). */
export const MAX_COLUMNS = 20
export const MAX_ROWS = 500
export const MAX_CELL = 200
export const MAX_SERIES = 6

/** Series colors on light ground; neighbours differ in lightness as well as hue (indigo, orange, teal, pink, lime, violet). */
export const seriesColors = ['#4f46e5', '#c2410c', '#0f766e', '#be185d', '#4d7c0f', '#7c3aed']

export type ParsedTable = { columns: string[]; rows: string[][]; notes: string[] }

/**
 * Reads a table pasted from Excel (tab separated) or a CSV file (comma or semicolon, quotes allowed). The first line holds
 * the column names. Longer tables, rows and cells are cut to the limits, and a note says so.
 */
export function parseTable(text: string): ParsedTable {
  const lines = text.replace(/\r\n?/g, '\n').split('\n').filter((line) => line.trim().length > 0)
  if (lines.length === 0) return { columns: [], rows: [], notes: [] }
  const delimiter = lines.some((line) => line.includes('\t')) ? '\t' : count(lines[0], ';') > count(lines[0], ',') ? ';' : ','
  const records = lines.map((line) => splitLine(line, delimiter))
  const notes: string[] = []

  let columns = records[0].map((cell, index) => cell.trim() || `Spalte ${index + 1}`)
  if (columns.length > MAX_COLUMNS) {
    notes.push(`Nur die ersten ${MAX_COLUMNS} Spalten wurden übernommen.`)
    columns = columns.slice(0, MAX_COLUMNS)
  }
  let body = records.slice(1)
  if (body.length > MAX_ROWS) {
    notes.push(`Nur die ersten ${MAX_ROWS} Zeilen wurden übernommen.`)
    body = body.slice(0, MAX_ROWS)
  }
  let cut = false
  const rows = body.map((record) =>
    columns.map((_, index) => {
      const cell = (record[index] ?? '').trim()
      if (cell.length > MAX_CELL) cut = true
      return cell.slice(0, MAX_CELL)
    }),
  )
  if (cut) notes.push(`Zellen mit mehr als ${MAX_CELL} Zeichen wurden gekürzt.`)
  return { columns: columns.map((c) => c.slice(0, MAX_CELL)), rows, notes }
}

const count = (text: string, char: string) => text.split(char).length - 1

/** One CSV line: quoted cells may hold the delimiter, "" is a quote. */
function splitLine(line: string, delimiter: string): string[] {
  if (delimiter === '\t') return line.split('\t')
  const cells: string[] = []
  let cell = ''
  let quoted = false
  for (let i = 0; i < line.length; i++) {
    const char = line[i]
    if (quoted) {
      if (char === '"' && line[i + 1] === '"') {
        cell += '"'
        i++
      } else if (char === '"') {
        quoted = false
      } else {
        cell += char
      }
    } else if (char === '"' && cell.trim() === '') {
      quoted = true
      cell = ''
    } else if (char === delimiter) {
      cells.push(cell)
      cell = ''
    } else {
      cell += char
    }
  }
  cells.push(cell)
  return cells
}

/** Numbers as people type them: 12,5 and 1.234,5 (German) as well as 12.5 and 1,234.5; a trailing % is allowed. */
export function parseNumber(cell: string): number | null {
  let text = cell.replace(/[\s ]/g, '').replace(/%$/, '')
  if (!/^[-+]?[\d.,]+$/.test(text) || !/\d/.test(text)) return null
  const comma = text.lastIndexOf(',')
  const dot = text.lastIndexOf('.')
  if (comma >= 0 && dot >= 0) {
    text = comma > dot ? text.replaceAll('.', '').replace(',', '.') : text.replaceAll(',', '')
  } else if (comma >= 0) {
    // One comma is a decimal comma; several are thousands separators.
    text = count(text, ',') === 1 ? text.replace(',', '.') : text.replaceAll(',', '')
  } else if (count(text, '.') > 1) {
    text = text.replaceAll('.', '')
  }
  const value = Number(text)
  return Number.isFinite(value) ? value : null
}

export type Series = { name: string; values: (number | null)[] }

export type ChartData = {
  /** The first column: the x axis. */
  xName: string
  labels: string[]
  /** Numeric x values when every label is a number, so lines keep their real distances. */
  xValues: number[] | null
  series: Series[]
}

/** The number columns after the first one become series; a column counts as numeric when most of its cells are numbers. */
export function chartData(columns: string[], rows: string[][]): ChartData | null {
  if (columns.length < 2 || rows.length === 0) return null
  const labels = rows.map((row) => row[0] ?? '')
  const xs = labels.map(parseNumber)
  const xValues = xs.every((x) => x !== null) ? (xs as number[]) : null
  const series = columns
    .slice(1)
    .map((name, offset) => {
      const cells = rows.map((row) => (row[offset + 1] ?? '').trim())
      return { name, values: cells.map(parseNumber), filled: cells.filter((cell) => cell !== '').length }
    })
    .filter((s) => {
      const numbers = s.values.filter((v) => v !== null).length
      return numbers > 0 && numbers * 2 >= s.filled
    })
    .slice(0, MAX_SERIES)
    .map(({ name, values }) => ({ name, values }))
  return series.length === 0 ? null : { xName: columns[0], labels, xValues, series }
}

/** Round axis steps (1, 2, 2.5 or 5 times a power of ten) for about <paramref name="target"/> ticks. */
export function niceTicks(min: number, max: number, target = 5): number[] {
  if (min === max) {
    const pad = Math.abs(min) || 1
    min -= pad
    max += pad
  }
  const raw = (max - min) / target
  const power = 10 ** Math.floor(Math.log10(raw))
  const step = [1, 2, 2.5, 5, 10].map((f) => f * power).find((s) => s >= raw) ?? 10 * power
  const start = Math.floor(min / step) * step
  const ticks: number[] = []
  for (let value = start; value <= max + step * 0.5 && ticks.length < 20; value += step) ticks.push(Number(value.toFixed(10)))
  return ticks
}

const numberFormat = new Intl.NumberFormat('de-DE', { maximumFractionDigits: 2 })

export const formatNumber = (value: number) => numberFormat.format(value)
