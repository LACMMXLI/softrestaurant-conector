import { Search, X } from 'lucide-react'
import { useState } from 'react'
import { api } from '../api'
import { EmptyState } from '../components/EmptyState'
import { TicketRow } from '../components/TicketRow'
import { useApiResource } from '../hooks/useApiResource'

type SalesScreenProps = {
  branchCode: string
  date: string
  shiftId: number | null
  onOpenTicket: (folio: number, transient: boolean) => void
  onUnauthorized: () => void
}

export function SalesScreen({ branchCode, date, shiftId, onOpenTicket, onUnauthorized }: SalesScreenProps) {
  const [search, setSearch] = useState('')
  const [submittedSearch, setSubmittedSearch] = useState('')
  const [page, setPage] = useState(1)
  const { data: result, loading, error } = useApiResource(
    (signal) => api.sales(branchCode, date, shiftId, page, submittedSearch, signal),
    [branchCode, date, page, shiftId, submittedSearch],
    onUnauthorized,
    'No fue posible cargar los tickets.',
  )

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

  return (
    <div className="screen-stack">
      <section className="screen-title">
        <p className="utility-label">Detalle verificable</p>
        <h1>Todos los tickets</h1>
        <p>Recorre los registros que forman los totales del turno seleccionado.</p>
      </section>

      <form className="search-form" onSubmit={submitSearch} role="search">
        <Search size={19} aria-hidden="true" />
        <input
          type="search"
          value={search}
          onChange={(event) => setSearch(event.target.value)}
          placeholder="Buscar folio o número de cheque"
          aria-label="Buscar folio o número de cheque"
        />
        {search ? (
          <button className="icon-button" type="button" onClick={clearSearch} aria-label="Limpiar búsqueda">
            <X size={18} />
          </button>
        ) : null}
      </form>

      {error ? <EmptyState title="No se pudieron cargar los tickets" message={error} /> : null}
      {loading && !result ? <div className="skeleton list-skeleton" aria-label="Cargando tickets" /> : null}
      {result && !result.meta.canShowData ? (
        <EmptyState
          title={result.meta.coverage === 'invalid' ? 'Lote sin conciliación válida' : 'Periodo sin cobertura'}
          message="No se presentan tickets ni ceros hasta recibir una sincronización conciliada para este turno."
        />
      ) : null}
      {result?.meta.canShowData && result.meta.coverage === 'partial' ? (
        <div className="coverage-note" role="status">
          La lista corresponde únicamente a la parte conciliada de este turno.
        </div>
      ) : null}
      {!loading && result?.meta.canShowData && result.items.length === 0 ? (
        <EmptyState
          title={submittedSearch ? 'No encontramos ese ticket' : 'No hay tickets para este turno'}
          message={submittedSearch ? 'Prueba con otro folio o elimina la búsqueda.' : 'El turno está cubierto, pero no contiene tickets registrados.'}
        />
      ) : null}
      {result?.meta.canShowData && result.items.length > 0 ? (
        <section className="content-card tickets-card">
          <div className="section-heading horizontal">
            <div><p className="utility-label">Rastro completo</p><h2>Tickets registrados</h2></div>
            <span className="page-badge">Página {page}</span>
          </div>
          <div className="ticket-list">
            {result.items.map((ticket) => (
              <TicketRow key={ticket.folio} ticket={ticket} onOpen={onOpenTicket} />
            ))}
          </div>
          <div className="pagination-row">
            <button type="button" className="secondary-button" disabled={page === 1 || loading} onClick={() => setPage((value) => value - 1)}>
              Anterior
            </button>
            <span>Página {page}</span>
            <button type="button" className="secondary-button" disabled={!result.hasMore || loading} onClick={() => setPage((value) => value + 1)}>
              Siguiente
            </button>
          </div>
        </section>
      ) : null}
    </div>
  )
}
