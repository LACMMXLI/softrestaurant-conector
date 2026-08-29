import { AlertTriangle, CheckCircle2, Clock3, CloudOff } from 'lucide-react'
import type { DashboardMeta } from '../types'

type StatusPillProps = {
  freshness: DashboardMeta['freshness']
  coverage: DashboardMeta['coverage']
}

export function StatusPill({ freshness, coverage }: StatusPillProps) {
  if (coverage === 'invalid') {
    return <span className="status-pill danger"><AlertTriangle size={14} /> Conciliación pendiente</span>
  }
  if (coverage === 'missing' || freshness === 'missing') {
    return <span className="status-pill muted"><CloudOff size={14} /> Sin datos</span>
  }
  if (freshness === 'stale') {
    return <span className="status-pill warning"><Clock3 size={14} /> Datos atrasados</span>
  }
  if (coverage === 'partial') {
    return <span className="status-pill current"><Clock3 size={14} /> Cobertura parcial</span>
  }
  return <span className="status-pill success"><CheckCircle2 size={14} /> Al día</span>
}
