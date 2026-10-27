import { useState } from 'react'
import type { FormEvent } from 'react'
import { ArrowLeft, ChevronRight, Clock3, Plus, Store, UserRound, X } from 'lucide-react'
import { api, ApiError } from '../api'
import { BUSINESS_ROLE_LABELS } from '../types'
import type { Branch, Business } from '../types'

type BusinessDetailScreenProps = {
  business: Business
  branches: Branch[]
  onBack: () => void
  onOpenBranch: (code: string) => void
  onBranchCreated: (branch: Branch) => void
  onUnauthorized: () => void
}

export function BusinessDetailScreen({ business, branches, onBack, onOpenBranch, onBranchCreated, onUnauthorized }: BusinessDetailScreenProps) {
  const [formOpen, setFormOpen] = useState(false)
  const [code, setCode] = useState('')
  const [name, setName] = useState('')
  const [timezone, setTimezone] = useState('America/Tijuana')
  const [busy, setBusy] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)

  async function handleCreate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setBusy(true)
    setFormError(null)
    try {
      const branch = await api.createBranch(business.id, code.trim(), name.trim(), timezone.trim())
      onBranchCreated(branch)
      setFormOpen(false)
      setCode('')
      setName('')
      setTimezone('America/Tijuana')
    } catch (reason) {
      if (reason instanceof ApiError && reason.status === 401) return onUnauthorized()
      setFormError(reason instanceof Error ? reason.message : 'No fue posible crear la sucursal.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="panel-stack">
      <button className="icon-button back-button" type="button" onClick={onBack}>
        <ArrowLeft size={17} aria-hidden="true" />
        <span>Volver a negocios</span>
      </button>

      <section className="panel-card" aria-labelledby="business-detail-title">
        <div className="panel-card-header">
          <div>
            <p className="section-kicker">Negocio</p>
            <h1 id="business-detail-title">{business.name} <code className="branch-code-badge">{business.slug}</code></h1>
            <p className="panel-hint">Alta: {new Date(business.createdAt).toLocaleDateString('es-MX', { dateStyle: 'long' })}</p>
          </div>
          <span className={business.active ? 'status-pill status-ok' : 'status-pill status-off'}>{business.active ? 'Activo' : 'Inactivo'}</span>
        </div>

        <h2 className="business-subtitle">Equipo</h2>
        {business.members.length === 0 ? (
          <p className="panel-hint">Este negocio no tiene personas asignadas. Asígnalas desde Usuarios.</p>
        ) : (
          <ul className="business-members">
            {business.members.map((member) => (
              <li key={member.userId}>
                <span className="row-icon"><UserRound size={16} aria-hidden="true" /></span>
                <span className="business-member-main"><b>{member.displayName}</b><small>{member.email}</small></span>
                <span className="status-pill">{BUSINESS_ROLE_LABELS[member.role]}</span>
              </li>
            ))}
          </ul>
        )}
      </section>

      <section className="panel-card" aria-labelledby="business-branches-title">
        <div className="panel-card-header">
          <div>
            <p className="section-kicker">Dentro de {business.name}</p>
            <h2 id="business-branches-title">Sucursales</h2>
            <p className="panel-hint">Estado y conexión de cada punto de venta de este negocio.</p>
          </div>
          <button className="secondary-button" type="button" onClick={() => setFormOpen((open) => !open)}>
            {formOpen ? <X size={16} aria-hidden="true" /> : <Plus size={16} aria-hidden="true" />}
            <span>{formOpen ? 'Cancelar' : 'Nueva sucursal'}</span>
          </button>
        </div>

        {formOpen ? (
          <form className="inline-form" onSubmit={handleCreate}>
            <label>
              Código
              <input
                type="text"
                value={code}
                onChange={(event) => setCode(event.target.value)}
                placeholder="sucursal-centro"
                pattern="[a-z0-9][a-z0-9-]{1,62}"
                title="Minúsculas, dígitos y guiones. De 2 a 63 caracteres."
                required
              />
            </label>
            <label>
              Nombre
              <input type="text" value={name} onChange={(event) => setName(event.target.value)} maxLength={200} placeholder="Sucursal Centro" required />
            </label>
            <label>
              Zona horaria
              <input type="text" value={timezone} onChange={(event) => setTimezone(event.target.value)} placeholder="America/Tijuana" />
            </label>
            <button className="primary-button" type="submit" disabled={busy}>
              <span>{busy ? 'Creando…' : `Crear sucursal en ${business.name}`}</span>
            </button>
          </form>
        ) : null}
        {formError ? <p className="form-error" role="alert">{formError}</p> : null}

        {branches.length === 0 ? (
          <p className="panel-hint">Este negocio todavía no tiene sucursales.</p>
        ) : (
          <div className="management-list" role="list">
            {branches.map((branch) => (
              <button key={branch.code} className="management-row" type="button" onClick={() => onOpenBranch(branch.code)}>
                <span className="row-icon"><Store size={19} aria-hidden="true" /></span>
                <span className="management-row-main">
                  <span className="management-row-title">{branch.name}</span>
                  <span className="management-row-meta"><code>{branch.code}</code><span>{branch.timezone}</span></span>
                  <span className="management-row-sync"><Clock3 size={13} aria-hidden="true" />{branch.lastSyncAt ? `Sincronizó ${new Date(branch.lastSyncAt).toLocaleString('es-MX')}` : 'Aún no sincroniza'}</span>
                </span>
                <span className="management-row-side">
                  <span className={branch.active ? 'status-pill status-ok' : 'status-pill status-off'}>{branch.active ? 'Activa' : 'Inactiva'}</span>
                  <ChevronRight size={17} aria-hidden="true" />
                </span>
              </button>
            ))}
          </div>
        )}
      </section>
    </div>
  )
}
