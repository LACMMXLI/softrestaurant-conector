import { useState } from 'react'
import type { FormEvent } from 'react'
import { Building2, ChevronRight, Plus, ShieldCheck, UserRound, X } from 'lucide-react'
import { api, ApiError } from '../api'
import { ROLE_LABELS, ROLES } from '../types'
import type { Role, UserDetail, UserSummary } from '../types'

type UsersScreenProps = {
  users: UserSummary[]
  loading: boolean
  error: string | null
  onOpenUser: (id: string) => void
  onUserCreated: (user: UserDetail) => void
  onUnauthorized: () => void
}

export function UsersScreen({
  users,
  loading,
  error,
  onOpenUser,
  onUserCreated,
  onUnauthorized,
}: UsersScreenProps) {
  const [formOpen, setFormOpen] = useState(false)
  const [email, setEmail] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [password, setPassword] = useState('')
  const [role, setRole] = useState<Role>('USER')
  const [busy, setBusy] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)

  function resetForm() {
    setEmail('')
    setDisplayName('')
    setPassword('')
    setRole('USER')
  }

  async function handleCreate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setBusy(true)
    setFormError(null)
    try {
      const user = await api.createUser(email.trim(), displayName.trim(), password, role)
      onUserCreated(user)
      setFormOpen(false)
      resetForm()
    } catch (reason) {
      if (reason instanceof ApiError && reason.status === 401) return onUnauthorized()
      setFormError(reason instanceof Error ? reason.message : 'No fue posible crear la cuenta.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="panel-stack">
      <section className="panel-card" aria-labelledby="users-title">
        <div className="panel-card-header">
          <div>
            <p className="section-kicker">Acceso de plataforma</p>
            <h1 id="users-title">Usuarios</h1>
            <p className="panel-hint">Define quién puede entrar y a qué negocios tiene acceso.</p>
          </div>
          <button className="secondary-button" type="button" onClick={() => setFormOpen((open) => !open)}>
            {formOpen ? <X size={16} aria-hidden="true" /> : <Plus size={16} aria-hidden="true" />}
            <span>{formOpen ? 'Cancelar' : 'Nuevo usuario'}</span>
          </button>
        </div>

        {formOpen ? (
          <form className="inline-form user-form" onSubmit={handleCreate}>
            <label>
              Correo
              <input
                type="email"
                value={email}
                onChange={(event) => setEmail(event.target.value)}
                required
              />
            </label>
            <label>
              Nombre
              <input
                type="text"
                value={displayName}
                onChange={(event) => setDisplayName(event.target.value)}
                maxLength={200}
                required
              />
            </label>
            <label>
              Contraseña
              <input
                type="password"
                value={password}
                onChange={(event) => setPassword(event.target.value)}
                minLength={12}
                autoComplete="new-password"
                required
              />
            </label>
            <label>
              Rol de plataforma
              <select value={role} onChange={(event) => setRole(event.target.value as Role)}>
                {ROLES.map((r) => (
                  <option key={r} value={r}>{ROLE_LABELS[r]}</option>
                ))}
              </select>
            </label>
            <p className="panel-hint form-full-row">
              El rol aquí solo distingue operador de plataforma (SUPERADMIN) de cuenta normal
              (USER). El acceso a negocios/sucursales se asigna después, desde el detalle de la
              cuenta.
            </p>

            <button className="primary-button" type="submit" disabled={busy}>
              <span>{busy ? 'Creando…' : 'Crear usuario'}</span>
            </button>
          </form>
        ) : null}

        {formError ? <p className="form-error" role="alert">{formError}</p> : null}
        {error ? <p className="form-error" role="alert">{error}</p> : null}

        {loading ? (
          <p className="panel-hint">Cargando usuarios…</p>
        ) : users.length === 0 ? (
          <p className="panel-hint">Todavía no hay usuarios dados de alta.</p>
        ) : (
          <div className="management-list" role="list">
            {users.map((user) => (
              <button key={user.id} className="management-row" type="button" onClick={() => onOpenUser(user.id)}>
                <span className="row-icon"><UserRound size={19} aria-hidden="true" /></span>
                <span className="management-row-main">
                  <span className="management-row-title">{user.displayName}</span>
                  <span className="management-row-email">{user.email}</span>
                  <span className="management-row-meta"><span>{user.role === 'SUPERADMIN' ? <ShieldCheck size={13} aria-hidden="true" /> : null}{ROLE_LABELS[user.role]}</span><span><Building2 size={13} aria-hidden="true" />{user.businessCount} {user.businessCount === 1 ? 'negocio' : 'negocios'}</span></span>
                </span>
                <span className="management-row-side"><span className={user.active ? 'status-pill status-ok' : 'status-pill status-off'}>{user.active ? 'Activo' : 'Inactivo'}</span><ChevronRight size={18} aria-hidden="true" /></span>
              </button>
            ))}
          </div>
        )}
      </section>
    </div>
  )
}
