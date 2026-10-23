import { CheckCircle2, TrendingDown, TrendingUp } from 'lucide-react'
import { cashVerdict } from '../lib/cash'
import { formatAmount, formatSignedAmount } from '../format'

type CashVerdictProps = {
  expected: number | null
  declared: number | null
  difference: number | null
}

const verdicts = {
  balanced: { icon: <CheckCircle2 size={26} />, title: 'CAJA CUADRADA', tone: 'ok' },
  short: { icon: <TrendingDown size={26} />, title: 'FALTANTE', tone: 'short' },
  over: { icon: <TrendingUp size={26} />, title: 'SOBRANTE', tone: 'over' },
} as const

export function CashVerdict({ expected, declared, difference }: CashVerdictProps) {
  const verdict = cashVerdict(difference)

  if (verdict === 'pending') {
    return (
      <div className="cash-verdict cash-verdict--pending" role="status">
        <div>
          <small>Resultado del corte</small>
          <strong>Sin declaración de caja</strong>
          <p>Todavía no hay efectivo declarado por los cajeros para este turno, así que no se puede saber si hay faltante o sobrante.</p>
        </div>
      </div>
    )
  }

  const amount = Math.abs(difference ?? 0)
  const { icon, title, tone } = verdicts[verdict]
  const explanation = verdict === 'balanced'
    ? 'Lo declarado por los cajeros coincide con el efectivo esperado.'
    : verdict === 'short'
      ? `Los cajeros declararon ${formatAmount(amount)} MENOS de lo que debía haber en caja.`
      : `Los cajeros declararon ${formatAmount(amount)} MÁS de lo que debía haber en caja.`

  return (
    <div className={`cash-verdict cash-verdict--${tone}`} role="status">
      <span className="cash-verdict-icon" aria-hidden="true">{icon}</span>
      <div className="cash-verdict-main">
        <small>Resultado del corte</small>
        <strong>{title}</strong>
        <p>{explanation}</p>
      </div>
      <b className="cash-verdict-amount" aria-label={verdict === 'balanced' ? 'Sin diferencia' : `${title.toLowerCase()} de ${formatAmount(amount)}`}>
        {formatSignedAmount(difference)}
      </b>
      <dl className="cash-verdict-math">
        <div><dt>Esperado</dt><dd>{formatAmount(expected)}</dd></div>
        <div><dt>Declarado</dt><dd>{formatAmount(declared)}</dd></div>
        <div><dt>Diferencia</dt><dd>{formatSignedAmount(difference)}</dd></div>
      </dl>
      <p className="cash-verdict-rule">Diferencia = declarado − esperado. Negativo es faltante; positivo es sobrante.</p>
    </div>
  )
}
