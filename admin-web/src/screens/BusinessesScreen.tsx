import { useState } from 'react'
import type { FormEvent } from 'react'
import { Briefcase, ChevronRight, Plus, Search, Store, X } from 'lucide-react'
import { api, ApiError } from '../api'
import type { Business, UserSummary } from '../types'

type BusinessesScreenProps = {
  businesses: Business[]
  users: UserSummary[]
  loading: boolean
  error: string | null
  onOpenBusiness: (id: string) => void
  onBusinessCreated: (business: Business) => void
  onUnauthorized: () => void
}

export function BusinessesScreen({ businesses, users, loading, error, onOpenBusiness, onBusinessCreated, onUnauthorized }: BusinessesScreenProps) {
  const [query, setQuery] = useState('')
  const [formOpen, setFormOpen] = useState(false)
  const [name, setName] = useState('')
  const [ownerId, setOwnerId] = useState('')
  const [busy, setBusy] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)

  async function handleCreate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setBusy(true)
    setFormError(null)
    try {
      const created = await api.createBusiness(name.trim(), ownerId || null)
      onBusinessCreated(created)
      setName('')
      setOwnerId('')
      setFormOpen(false)
    } catch (reason) {
      if (reason instanceof ApiError && reason.status === 401) return onUnauthorized()
      setFormError(reason instanceof Error ? reason.message : 'No fue posible crear el negocio.')
    } finally {
      setBusy(false)
    }
  }

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
              Cada negocio agrupa sus sucursales y a las personas que lo administran. Puedes crear un negocio y sus
              sucursales aunque todavía no tengan equipo ni conector asignado; los dueños también pueden crear los suyos.
            </p>
          </div>
          <button className="secondary-button" type="button" onClick={() => setFormOpen((open) => !open)}>
            {formOpen ? <X size={16} aria-hidden="true" /> : <Plus size={16} aria-hidden="true" />}
            <span>{formOpen ? 'Cancelar' : 'Nuevo negocio'}</span>
          </button>
        </div>

        {formOpen ? (
          <form className="inline-form" onSubmit={handleCreate}>
            <label>
              Nombre del negocio
              <input type="text" value={name} onChange={(event) => setName(event.target.value)} maxLength={200} placeholder="Fatboy" required />
            </label>
            <label>
              Propietario (opcional)
              <select value={ownerId} onChange={(event) => setOwnerId(event.target.value)}>
                <option value="">Sin propietario por ahora</option>
                {users.filter((user) => user.active).map((user) => (
                  <option key={user.id} value={user.id}>{user.displayName} · {user.email}</option>
                ))}
              </select>
            </label>
            <p className="panel-hint form-full-row">
              Sin propietario, el negocio queda creado pero nadie lo ve en su cuenta todavía. Puedes asignar a una persona
              después desde Usuarios.
            </p>
            <button className="primary-button" type="submit" disabled={busy}>
              <span>{busy ? 'Creando…' : 'Crear negocio'}</span>
            </button>
          </form>
        ) : null}
        {formError ? <p className="form-error" role="alert">{formError}</p> : null}

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
