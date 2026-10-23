import { Ban, Search } from 'lucide-react'
import { useState } from 'react'
import { api } from '../api'
import { useApiResource } from '../hooks/useApiResource'
import { formatAmount, formatTime } from '../format'
import type { CancellationMetric } from '../types'

type ProductCancellationsScreenProps = {
  branchCode: string
  date: string
  shiftId: number | null
  onUnauthorized: () => void
}

export function ProductCancellationsScreen({ branchCode, date, shiftId, onUnauthorized }: ProductCancellationsScreenProps) {
  const [user, setUser] = useState('')
  const [product, setProduct] = useState('')
  const [filters, setFilters] = useState({ user: '', product: '' })
  const [page, setPage] = useState(1)
  const { data, loading, error } = useApiResource(
    (signal) => api.productCancellations(branchCode, date, date, shiftId, filters.user, filters.product, page, signal),
    [branchCode, date, shiftId, filters, page],
    onUnauthorized,
    'No fue posible cargar las cancelaciones.',
  )

  function applyFilters(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setPage(1)
    setFilters({ user: user.trim(), product: product.trim() })
  }

  return (
    <div className="screen-stack">
      <section className="screen-title">
        <p className="utility-label">Auditoría de productos</p>
        <h1>Productos cancelados</h1>
        <p>Eventos de turnos cerrados y cuentas abiertas. Una cuenta eliminada antes de cobrar permanece visible aunque nunca haya generado cheque.</p>
      </section>

      <section className="operation-grid full">
        <div className="operation-stat danger-soft"><Ban size={20} /><span>Importe cancelado</span><strong>{formatAmount(data?.totalAmount ?? null)}</strong></div>
        <div className="operation-stat gold-soft"><Ban size={20} /><span>Unidades canceladas</span><strong>{data?.totalQuantity ?? '—'}</strong></div>
      </section>

      <form className="search-form compact-search" onSubmit={applyFilters}>
        <Search size={18} aria-hidden="true" />
        <input value={product} onChange={(event) => setProduct(event.target.value)} placeholder="Producto o ID" aria-label="Filtrar por producto o ID" />
        <input value={user} onChange={(event) => setUser(event.target.value)} placeholder="Empleado" aria-label="Filtrar por empleado" />
        <button className="secondary-button" type="submit">Filtrar</button>
      </form>
      {error ? <p className="form-error" role="alert">{error}</p> : null}

      <section className="content-card">
        <div className="section-heading"><div><p className="utility-label">Patrones</p><h2>Por empleado y producto</h2></div></div>
        <div className="top-products-grid">
          <MetricList title="Empleados" data={data?.byEmployee ?? []} />
          <MetricList title="Productos" data={data?.topProducts ?? []} />
        </div>
      </section>

      <section className="content-card">
        <div className="section-heading"><div><p className="utility-label">Rastro individual</p><h2>Eventos cancelados</h2></div></div>
        {loading && !data ? <div className="skeleton list-skeleton" aria-label="Cargando cancelaciones" /> : null}
        {data?.items.length === 0 ? <p className="quiet-empty">No hay productos cancelados con esos filtros.</p> : null}
        {data && data.items.length > 0 ? (
          <div className="activity-list">
            {data.items.map((item) => (
              <article className="activity-row cancellation" key={item.eventKey}>
                <span className="activity-icon cancel"><Ban size={18} /></span>
                <div>
                  <strong>{item.description || item.productId || 'Producto sin descripción'} · {item.quantity ?? '—'} u.</strong>
                  <p>
                    {formatTime(item.cancelledAt)} · {item.user || 'Usuario no registrado'} · {item.sourceKind === 'TRANSIENT' ? 'Cuenta abierta' : 'Histórico'} · {item.accountLabel || `Cuenta ${item.folio ?? item.tempFolio ?? 'sin folio'}`} · {item.accountStatus}
                  </p>
                  <small>
                    {item.reasonDescription || item.reason || 'Sin motivo registrado'}
                    {item.shiftId ? ` · Turno ${item.shiftId}` : ''}
                    {item.area ? ` · ${item.area}` : ''}
                  </small>
                </div>
                <b>{formatAmount(item.amount)}</b>
              </article>
            ))}
          </div>
        ) : null}
        <div className="pagination-row">
          <button type="button" className="secondary-button" disabled={page === 1 || loading} onClick={() => setPage((value) => value - 1)}>Anterior</button>
          <span>Página {page}</span>
          <button type="button" className="secondary-button" disabled={!data?.hasMore || loading} onClick={() => setPage((value) => value + 1)}>Siguiente</button>
        </div>
      </section>
    </div>
  )
}

function MetricList({ title, data }: { title: string; data: CancellationMetric[] }) {
  return (
    <div>
      <h3>{title}</h3>
      {data.length === 0 ? <p className="quiet-empty">Sin datos.</p> : data.map((item) => (
        <p key={item.label}><b>{item.label}</b><br /><small>{item.quantity} u. · {formatAmount(item.amount)}</small></p>
      ))}
    </div>
  )
}
