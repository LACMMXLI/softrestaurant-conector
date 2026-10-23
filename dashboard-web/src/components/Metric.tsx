type MetricProps = {
  icon: React.ReactNode
  label: string
  value: string
}

export function Metric({ icon, label, value }: MetricProps) {
  return (
    <article className="metric-item">
      <span className="metric-icon" aria-hidden="true">{icon}</span>
      <span>{label}</span>
      <strong>{value}</strong>
    </article>
  )
}
