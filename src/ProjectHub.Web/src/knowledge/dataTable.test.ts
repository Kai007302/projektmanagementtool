import { describe, expect, it } from 'vitest'
import { chartData, MAX_ROWS, niceTicks, parseNumber, parseTable } from './dataTable'

describe('data tables', () => {
  it('reads a table pasted from Excel', () => {
    const table = parseTable('Temperatur\tAusbeute\r\n20\t12,5\r\n\r\n40\t18\n')
    expect(table).toEqual({ columns: ['Temperatur', 'Ausbeute'], rows: [['20', '12,5'], ['40', '18']], notes: [] })
  })

  it('reads CSV with semicolons, quotes and missing cells', () => {
    const table = parseTable('Probe;"Ergebnis; Kommentar";Wert\nA;"sagt ""gut""";1\nB')
    expect(table.columns).toEqual(['Probe', 'Ergebnis; Kommentar', 'Wert'])
    expect(table.rows).toEqual([
      ['A', 'sagt "gut"', '1'],
      ['B', '', ''],
    ])
  })

  it('names empty column headers and cuts tables at the limits', () => {
    const lines = ['x,', ...Array.from({ length: MAX_ROWS + 5 }, (_, i) => `${i},${'a'.repeat(250)}`)]
    const table = parseTable(lines.join('\n'))
    expect(table.columns).toEqual(['x', 'Spalte 2'])
    expect(table.rows).toHaveLength(MAX_ROWS)
    expect(table.rows[0][1]).toHaveLength(200)
    expect(table.notes).toHaveLength(2)
  })

  it('understands German and English numbers', () => {
    expect(parseNumber('12,5')).toBe(12.5)
    expect(parseNumber('1.234,5')).toBe(1234.5)
    expect(parseNumber('1,234.5')).toBe(1234.5)
    expect(parseNumber('1.234.567')).toBe(1234567)
    expect(parseNumber('-3 %')).toBe(-3)
    expect(parseNumber('n/a')).toBeNull()
    expect(parseNumber('')).toBeNull()
  })

  it('charts the number columns over the first column', () => {
    const data = chartData(
      ['Tag', 'Ausbeute', 'Notiz', 'Druck'],
      [
        ['1', '10', 'gut', '2,5'],
        ['3', '12', 'schlecht', ''],
      ],
    )
    expect(data).toEqual({
      xName: 'Tag',
      labels: ['1', '3'],
      xValues: [1, 3],
      series: [
        { name: 'Ausbeute', values: [10, 12] },
        { name: 'Druck', values: [2.5, null] },
      ],
    })
    expect(chartData(['Probe', 'Notiz'], [['A', 'gut']])).toBeNull()
    expect(chartData(['Probe', 'Wert'], [['A', '1']])?.xValues).toBeNull()
  })

  it('finds round axis steps', () => {
    expect(niceTicks(0, 18)).toEqual([0, 5, 10, 15, 20])
    expect(niceTicks(5, 5)).toEqual([0, 2, 4, 6, 8, 10])
  })
})
