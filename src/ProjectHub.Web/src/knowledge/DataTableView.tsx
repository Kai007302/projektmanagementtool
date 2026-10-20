import { useId, useState } from 'react'
import type { ChartKind } from './blocks'
import { chartData, formatNumber, niceTicks, seriesColors, type ChartData } from './dataTable'
import './insights.css'

type Props = { title?: string; columns: string[]; rows: string[][]; chart: ChartKind; /** Slides show the chart only, larger. */ large?: boolean }

/** A data table with its chart; the person switches between chart and table. Without numbers it is a table. */
export function DataTableView({ title, columns, rows, chart, large = false }: Props) {
  const data = chart === 'none' ? null : chartData(columns, rows)
  const [view, setView] = useState<'chart' | 'table'>(data ? 'chart' : 'table')
  const headingId = useId()
  if (columns.length === 0) return null

  return (
    <figure className={large ? 'data-block large' : 'data-block'} aria-labelledby={title ? headingId : undefined}>
      <div className="data-block-header">
        {title && (
          <figcaption id={headingId} className="data-block-title">
            {title}
          </figcaption>
        )}
        {data && !large && (
          <div className="segmented" role="group" aria-label="Ansicht der Messdaten">
            <button type="button" aria-pressed={view === 'chart'} onClick={() => setView('chart')}>
              Diagramm
            </button>
            <button type="button" aria-pressed={view === 'table'} onClick={() => setView('table')}>
              Tabelle
            </button>
          </div>
        )}
      </div>
      {data && (view === 'chart' || large) ? (
        <DataChart data={data} kind={chart === 'bar' ? 'bar' : 'line'} />
      ) : (
        <div className="table-scroll">
          <table className="data-table">
            <thead>
              <tr>
                {columns.map((column, index) => (
                  <th key={index} scope="col">
                    {column}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {rows.map((row, index) => (
                <tr key={index}>
                  {row.map((cell, i) => (i === 0 ? <th key={i} scope="row">{cell}</th> : <td key={i}>{cell}</td>))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </figure>
  )
}

const WIDTH = 640
const HEIGHT = 260
const PAD = { left: 48, right: 16, top: 12, bottom: 36 }

/** Line or grouped bar chart as SVG; a text summary names every series with its range for screen readers. */
export function DataChart({ data, kind }: { data: ChartData; kind: 'line' | 'bar' }) {
  const values = data.series.flatMap((s) => s.values.filter((v): v is number => v !== null))
  const ticks = niceTicks(Math.min(0, ...values), Math.max(...values))
  const yMin = ticks[0]
  const yMax = ticks[ticks.length - 1]
  const plotWidth = WIDTH - PAD.left - PAD.right
  const plotHeight = HEIGHT - PAD.top - PAD.bottom
  const y = (value: number) => PAD.top + plotHeight - ((value - yMin) / (yMax - yMin || 1)) * plotHeight
  const count = data.labels.length
  const ordered = kind === 'line' && data.xValues !== null
  const xMin = ordered ? Math.min(...data.xValues!) : 0
  const xMax = ordered ? Math.max(...data.xValues!) : Math.max(1, count - 1)
  const slot = plotWidth / Math.max(1, count)
  const x = (index: number) =>
    kind === 'bar'
      ? PAD.left + slot * index + slot / 2
      : PAD.left + (((ordered ? data.xValues![index] : index) - xMin) / (xMax - xMin || 1)) * plotWidth
  // At most about eight labels on the x axis, so they never overlap.
  const every = Math.max(1, Math.ceil(count / 8))
  const summary = data.series
    .map((s) => {
      const numbers = s.values.filter((v): v is number => v !== null)
      return `${s.name} von ${formatNumber(Math.min(...numbers))} bis ${formatNumber(Math.max(...numbers))}`
    })
    .join('; ')
  const barWidth = Math.max(2, Math.min(28, (slot * 0.8) / data.series.length))

  return (
    <div className="data-chart">
      <ul className="chart-legend" aria-hidden="true">
        {data.series.map((s, i) => (
          <li key={s.name + i}>
            <span className="legend-swatch" style={{ background: seriesColors[i] }} /> {s.name}
          </li>
        ))}
      </ul>
      <svg viewBox={`0 0 ${WIDTH} ${HEIGHT}`} role="img" aria-label={`${kind === 'bar' ? 'Balkendiagramm' : 'Liniendiagramm'} über ${data.xName}: ${summary}`}>
        {ticks.map((tick) => (
          <g key={tick}>
            <line x1={PAD.left} x2={WIDTH - PAD.right} y1={y(tick)} y2={y(tick)} className={tick === 0 ? 'chart-axis' : 'chart-grid'} />
            <text x={PAD.left - 6} y={y(tick) + 4} textAnchor="end" className="chart-label">
              {formatNumber(tick)}
            </text>
          </g>
        ))}
        {data.labels.map((label, index) =>
          index % every === 0 ? (
            <text key={index} x={x(index)} y={HEIGHT - PAD.bottom + 18} textAnchor="middle" className="chart-label">
              {label.length > 12 ? `${label.slice(0, 11)}…` : label}
            </text>
          ) : null,
        )}
        <text x={WIDTH - PAD.right} y={HEIGHT - 4} textAnchor="end" className="chart-label chart-axis-name">
          {data.xName}
        </text>
        {kind === 'bar'
          ? data.series.map((s, si) =>
              s.values.map((value, index) =>
                value === null ? null : (
                  <rect
                    key={`${si}-${index}`}
                    x={x(index) - (barWidth * data.series.length) / 2 + barWidth * si}
                    y={Math.min(y(value), y(0))}
                    width={barWidth - 1}
                    height={Math.abs(y(0) - y(value))}
                    rx={2}
                    fill={seriesColors[si]}
                  />
                ),
              ),
            )
          : data.series.map((s, si) => {
              const points = s.values.flatMap((value, index) => (value === null ? [] : [`${x(index)},${y(value)}`]))
              return (
                <g key={si} fill={seriesColors[si]}>
                  <polyline points={points.join(' ')} fill="none" stroke={seriesColors[si]} strokeWidth={2.5} strokeLinejoin="round" strokeLinecap="round" />
                  {count <= 60 && s.values.map((value, index) => (value === null ? null : <circle key={index} cx={x(index)} cy={y(value)} r={3.5} />))}
                </g>
              )
            })}
      </svg>
    </div>
  )
}
