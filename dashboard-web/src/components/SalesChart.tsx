import type { HourlySalesPoint } from '../types'
import { formatAmount } from '../format'

type SalesChartProps = {
  points: HourlySalesPoint[]
}

export function SalesChart({ points }: SalesChartProps) {
  if (points.length === 0) {
    return (
      <div className="chart-empty">
        <span className="chart-baseline" />
        <p>No hay ventas registradas por hora para este día.</p>
      </div>
    )
  }

  const width = 680
  const height = 210
  const paddingX = 28
  const paddingY = 24
  const maximum = Math.max(...points.map((point) => point.sales), 1)
  const coordinates = points.map((point, index) => ({
    ...point,
    x: points.length === 1
      ? width / 2
      : paddingX + (index / (points.length - 1)) * (width - paddingX * 2),
    y: height - paddingY - (point.sales / maximum) * (height - paddingY * 2),
  }))
  const path = coordinates.map((point, index) =>
    `${index === 0 ? 'M' : 'L'} ${point.x.toFixed(1)} ${point.y.toFixed(1)}`).join(' ')
  const total = points.reduce((sum, point) => sum + point.sales, 0)

  return (
    <div className="sales-chart">
      <div className="chart-summary">
        <p>Ritmo por hora</p>
        <span>{points.length} horas con venta · {formatAmount(total)} en el día</span>
      </div>
      <div className="chart-scroll">
        <svg viewBox={`0 0 ${width} ${height}`} role="img" aria-labelledby="hourly-chart-title hourly-chart-desc">
          <title id="hourly-chart-title">Venta registrada por hora</title>
          <desc id="hourly-chart-desc">La gráfica contiene únicamente horas que tienen ventas registradas.</desc>
          <line className="chart-grid" x1={paddingX} y1={height - paddingY} x2={width - paddingX} y2={height - paddingY} />
          <path className="chart-line" d={path} />
          {coordinates.map((point, index) => (
            <g key={point.hour}>
              <circle className={index === coordinates.length - 1 ? 'chart-dot latest' : 'chart-dot'} cx={point.x} cy={point.y} r="5" />
              <text className="chart-hour" x={point.x} y={height - 4} textAnchor="middle">
                {String(point.hour).padStart(2, '0')}h
              </text>
            </g>
          ))}
        </svg>
      </div>
    </div>
  )
}
