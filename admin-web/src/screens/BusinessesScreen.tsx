import { useState } from 'react'
import { Briefcase, ChevronRight, Search, Store } from 'lucide-react'
import type { Business } from '../types'

type BusinessesScreenProps = {
  businesses: Business[]
  loading: boolean
  error: string | null
  onOpenBusiness: (id: string) => void
}

export function BusinessesScreen({ businesses, loading, error, onOpenBusiness }: BusinessesScreenProps) {
  const [query, setQuery] = useState('')
  const normalized = query.trim().toLowerCase()
  const visible = normalized
    ? businesses.filter((business) =>
        business.name.toLowerCase().includes(normalized)
        || business.members.some((member) => member.email.toLowerCase().includes(normalized) || member.displayName.toLowerCase().includes(normalized)))
    : businesses

  return (
    <div className="panel-stack">
      <section className="panel-card" aria-labelledby="businesses-title">
        <div className="panel-card-header">
          <div>
            <p className="section-kicker">Clientes de la plataforma</p>
            <h1 id="businesses-title">Negocios</h1>
            <p className="panel-hint">
              Cada negocio agrupa sus sucursales y a las personas que lo administran. Entra a un negocio para ver y
              administrar sus sucursales. Los negocios los crean sus propios dueños desde su cuenta.
            </p>
          </div>
        </div>

        {businesses.length > 0 ? (
          <label className="business-search">
            <Search size={16} aria-hidden="true" />
            <span className="sr-only">Buscar negocio o responsable</span>
            <input type="search" value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Buscar negocio o responsable" />
          </label>
        ) : null}

        {error ? <p className="form-error" role="alert">{error}</p> : null}

        {loading ? (
          <p className="panel-hint">Cargando negocios…</p>
        ) : businesses.length === 0 ? (
          <p className="panel-hint">Todavía no hay negocios dados de alta.</p>
        ) : visible.length === 0 ? (
          <p className="panel-hint">Ningún negocio coincide con la búsqueda.</p>
        ) : (
          <div className="management-list" role="list">
            {visible.map((business) => {
              const owners = business.members.filter((member) => member.role === 'OWNER')
              return (
                <button key={business.id} className="management-row" type="button" onClick={() => onOpenBusiness(business.id)}>
                  <span className="row-icon"><Briefcase size={19} aria-hidden="true" /></span>
                  <span className="management-row-main">
                    <span className="management-row-title">{business.name}</span>
                    <span className="management-row-meta">
                      <span><Store size={13} aria-hidden="true" />{branchSummary(business)}</span>
                      <span>{owners.length > 0 ? `Propietario: ${owners.map((owner) => owner.displayName).join(', ')}` : 'Sin propietario asignado'}</span>
                    </span>
                  </span>
                  <span className="management-row-side">
                    <span className={business.active ? 'status-pill status-ok' : 'status-pill status-off'}>{business.active ? 'Activo' : 'Inactivo'}</span>
                    <ChevronRight size={17} aria-hidden="true" />
                  </span>
                </button>
              )
            })}
          </div>
        )}
      </section>
    </div>
  )
}

export function branchSummary(business: Pick<Business, 'branchCount' | 'activeBranchCount'>): string {
  if (business.branchCount === 0) return 'Sin sucursales'
  const noun = business.branchCount === 1 ? 'sucursal' : 'sucursales'
  return business.activeBranchCount === business.branchCount
    ? `${business.branchCount} ${noun}`
    : `${business.branchCount} ${noun} · ${business.activeBranchCount} activa${business.activeBranchCount === 1 ? '' : 's'}`
}
