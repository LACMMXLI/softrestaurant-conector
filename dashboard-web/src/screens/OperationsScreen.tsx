import { ArrowDownToLine, ArrowUpFromLine, Ban, CircleAlert, Search, X } from 'lucide-react'
import { useEffect, useState } from 'react'
import { api, ApiError } from '../api'
import { EmptyState } from '../components/EmptyState'
import { formatAmount, formatTime } from '../format'
import type { CashMovementsPage, DashboardHome } from '../types'

type OperationsScreenProps = {
  branchCode: string
  date: string
  shiftId: number | null
  data: DashboardHome | null
  loading: boolean
  onUnauthorized: () => void
}

export function OperationsScreen({ branchCode, date, shiftId, data, loading, onUnauthorized }: OperationsScreenProps) {
  const [search, setSearch] = useState('')
  const [submittedSearch, setSubmittedSearch] = useState('')
  const [type, setType] = useState<number | null>(null)
  const [page, setPage] = useState(1)
  const [result, setResult] = useState<CashMovementsPage | null>(null)
  const [movementsLoading, setMovementsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    setMovementsLoading(true)
    setError(null)
    api.cashMovements(branchCode, date, shiftId, page, type, submittedSearch, controller.signal)
      .then((nextResult) => {
        if (!controller.signal.aborted) setResult(nextResult)
      })
      .catch((reason: unknown) => {
        if (reason instanceof DOMException && reason.name === 'AbortError') return
        if (reason instanceof ApiError && reason.status === 401) onUnauthorized()
        else setError(reason instanceof Error ? reason.message : 'No fue posible cargar los movimientos.')
      })
      .finally(() => {
        if (!controller.signal.aborted) setMovementsLoading(false)
      })
    return () => controller.abort()
  }, [branchCode, date, onUnauthorized, page, shiftId, submittedSearch, type])

  function submitSearch(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setPage(1)
    setSubmittedSearch(search.trim())
  }

  function clearSearch() {
    setSearch('')
    setSubmittedSearch('')
    setPage(1)
  }

  function selectType(nextType: number | null) {
    setType(nextType)
    setPage(1)
  }

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
        <p>Consulta todas las entradas, salidas y cancelaciones sincronizadas para el turno seleccionado.</p>
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

      <section className="content-card movements-card">
        <div className="section-heading horizontal">
          <div><p className="utility-label">Rastro completo</p><h2>Todos los movimientos de caja</h2></div>
          <span className="page-badge">Página {page}</span>
        </div>

        <form className="search-form compact-search" onSubmit={submitSearch} role="search">
          <Search size={18} aria-hidden="true" />
          <input
            type="search"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder="Buscar concepto, referencia o folio"
            aria-label="Buscar movimientos de caja"
          />
          {search ? <button className="icon-button" type="button" onClick={clearSearch} aria-label="Limpiar búsqueda"><X size={18} /></button> : null}
        </form>

        <div className="filter-chips" aria-label="Filtrar movimientos">
          <button type="button" className={type === null ? 'active' : ''} onClick={() => selectType(null)}>Todos</button>
          <button type="button" className={type === 1 ? 'active' : ''} onClick={() => selectType(1)}>Salidas</button>
          <button type="button" className={type === 2 ? 'active' : ''} onClick={() => selectType(2)}>Entradas</button>
        </div>

        {error ? <p className="form-error" role="alert">{error}</p> : null}
        {movementsLoading && !result ? <div className="skeleton list-skeleton" aria-label="Cargando movimientos" /> : null}
        {!movementsLoading && result?.items.length === 0 ? <p className="quiet-empty">No hay movimientos que coincidan con este filtro.</p> : null}
        {result && result.items.length > 0 ? (
          <>
            <div className="activity-list">
              {result.items.map((item) => (
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
            <div className="pagination-row">
              <button type="button" className="secondary-button" disabled={page === 1 || movementsLoading} onClick={() => setPage((value) => value - 1)}>Anterior</button>
              <span>Página {page}</span>
              <button type="button" className="secondary-button" disabled={!result.hasMore || movementsLoading} onClick={() => setPage((value) => value + 1)}>Siguiente</button>
            </div>
          </>
        ) : null}
      </section>

      <section className="content-card">
        <div className="section-heading horizontal">
          <div><p className="utility-label">Productos retirados</p><h2>Cancelaciones recientes</h2></div>
          <span className="count-badge"><Ban size={15} /> {data.summary.cancelledLines ?? '—'}</span>
        </div>
        {data.recentCancellations.length === 0 ? <p className="quiet-empty">No hay líneas canceladas registradas para esta fecha.</p> : (
          <div className="activity-list">
            {data.recentCancellations.map((item, index) => (
              <article className="activity-row cancellation" key={`${item.folio ?? 'sin-folio'}-${item.productId ?? 'sin-producto'}-${index}`}>
                <span className="activity-icon cancel"><CircleAlert size={18} /></span>
                <div>
                  <strong>{item.description || 'Producto sin descripción sincronizada'}</strong>
                  <p>{item.quantity ?? '—'} unidad(es){item.folio ? ` · Ticket ${item.folio}` : ' · Sin vínculo histórico'}{item.user ? ` · ${item.user}` : ''}</p>
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
