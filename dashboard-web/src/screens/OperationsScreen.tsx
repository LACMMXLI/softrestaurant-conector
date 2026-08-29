import { ArrowDownToLine, ArrowUpFromLine, Ban, CircleAlert } from 'lucide-react'
import { EmptyState } from '../components/EmptyState'
import { formatAmount, formatTime } from '../format'
import type { DashboardHome } from '../types'

type OperationsScreenProps = {
  data: DashboardHome | null
  loading: boolean
}

export function OperationsScreen({ data, loading }: OperationsScreenProps) {
  if (loading && !data) return <div className="skeleton list-skeleton" aria-label="Cargando operación" />
  if (!data) return null
  if (!data.meta.canShowData) {
    return <EmptyState title="Operación sin cobertura" message="Hace falta una sincronización conciliada para consultar caja y cancelaciones." />
  }

  return (
    <div className="screen-stack">
      <section className="screen-title">
        <p className="utility-label">Eventos del día</p>
        <h1>Operación</h1>
        <p>Cancelaciones y movimientos que requieren contexto, con la fuente tal como fue sincronizada.</p>
      </section>

      <section className="operation-grid full">
        <div className="operation-stat gold-soft">
          <ArrowUpFromLine size={20} />
          <span>Salidas de caja</span>
          <strong>{formatAmount(data.summary.cashOut)}</strong>
        </div>
        <div className="operation-stat teal-soft">
          <ArrowDownToLine size={20} />
          <span>Entradas de caja</span>
          <strong>{formatAmount(data.summary.cashIn)}</strong>
        </div>
      </section>

      <section className="content-card">
        <div className="section-heading">
          <p className="utility-label">Movimientos recientes</p>
          <h2>Caja</h2>
        </div>
        {data.recentCashMovements.length === 0 ? (
          <p className="quiet-empty">No hay entradas o salidas registradas para esta fecha.</p>
        ) : (
          <div className="activity-list">
            {data.recentCashMovements.map((item) => (
              <article className="activity-row" key={item.folio}>
                <span className={item.type === 1 ? 'activity-icon out' : 'activity-icon in'}>
                  {item.type === 1 ? <ArrowUpFromLine size={18} /> : <ArrowDownToLine size={18} />}
                </span>
                <div>
                  <strong>{item.concept || (item.type === 1 ? 'Salida sin concepto' : 'Entrada sin concepto')}</strong>
                  <p>{formatTime(item.date)} · Folio {item.folio}{item.reference ? ` · ${item.reference}` : ''}</p>
                </div>
                <b>{formatAmount(item.amount)}</b>
              </article>
            ))}
          </div>
        )}
      </section>

      <section className="content-card">
        <div className="section-heading horizontal">
          <div>
            <p className="utility-label">Productos retirados</p>
            <h2>Cancelaciones</h2>
          </div>
          <span className="count-badge"><Ban size={15} /> {data.summary.cancelledLines ?? '—'}</span>
        </div>
        {data.recentCancellations.length === 0 ? (
          <p className="quiet-empty">No hay líneas canceladas registradas para esta fecha.</p>
        ) : (
          <div className="activity-list">
            {data.recentCancellations.map((item, index) => (
              <article className="activity-row cancellation" key={`${item.folio ?? 'sin-folio'}-${item.productId ?? 'sin-producto'}-${index}`}>
                <span className="activity-icon cancel"><CircleAlert size={18} /></span>
                <div>
                  <strong>{item.description || 'Producto sin descripción sincronizada'}</strong>
                  <p>
                    {item.quantity ?? '—'} unidad(es)
                    {item.folio ? ` · Ticket ${item.folio}` : ' · Sin vínculo histórico'}
                    {item.user ? ` · ${item.user}` : ''}
                  </p>
                  {item.reason ? <small>{item.reason}</small> : null}
                </div>
                <b>{item.occurrences > 1 ? `×${item.occurrences}` : ''}</b>
              </article>
            ))}
          </div>
        )}
      </section>
    </div>
  )
}
