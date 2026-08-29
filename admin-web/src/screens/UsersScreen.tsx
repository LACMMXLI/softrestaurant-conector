import { useState } from 'react'
import type { FormEvent } from 'react'
import { Plus, UserRound, X } from 'lucide-react'
import { api, ApiError } from '../api'
import { ROLE_LABELS, ROLES } from '../types'
import type { Role, UserDetail, UserSummary } from '../types'

type UsersScreenProps = {
  users: UserSummary[]
  branches: { code: string; name: string }[]
  loading: boolean
  error: string | null
  onOpenUser: (id: string) => void
  onUserCreated: (user: UserDetail) => void
  onUnauthorized: () => void
}

export function UsersScreen({
  users,
  branches,
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
  const [role, setRole] = useState<Role>('OWNER')
  const [branchCodes, setBranchCodes] = useState<string[]>([])
  const [busy, setBusy] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)

  function resetForm() {
    setEmail('')
    setDisplayName('')
    setPassword('')
    setRole('OWNER')
    setBranchCodes([])
  }

  function toggleBranch(code: string) {
    setBranchCodes((current) =>
      current.includes(code) ? current.filter((c) => c !== code) : [...current, code],
    )
  }

  async function handleCreate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setBusy(true)
    setFormError(null)
    try {
      const user = await api.createUser(
        email.trim(),
        displayName.trim(),
        password,
        role,
        role === 'SUPERADMIN' ? [] : branchCodes,
      )
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
          <h2 id="users-title">Usuarios</h2>
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
              Rol
              <select value={role} onChange={(event) => setRole(event.target.value as Role)}>
                {ROLES.map((r) => (
                  <option key={r} value={r}>{ROLE_LABELS[r]}</option>
                ))}
              </select>
            </label>

            {role === 'SUPERADMIN' ? (
              <p className="panel-hint form-full-row">
                SUPERADMIN tiene acceso global: no necesita sucursales asignadas.
              </p>
            ) : (
              <fieldset className="branch-checklist form-full-row">
                <legend>Sucursales</legend>
                {branches.length === 0 ? (
                  <p className="panel-hint">No hay sucursales dadas de alta todavía.</p>
                ) : (
                  branches.map((branch) => (
                    <label key={branch.code} className="branch-checklist-item">
                      <input
                        type="checkbox"
                        checked={branchCodes.includes(branch.code)}
                        onChange={() => toggleBranch(branch.code)}
                      />
                      <span>{branch.name}</span>
                    </label>
                  ))
                )}
              </fieldset>
            )}

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
          <div className="table-scroll">
            <table>
              <thead>
                <tr>
                  <th>Usuario</th>
                  <th>Rol</th>
                  <th>Estado</th>
                  <th>Sucursales</th>
                </tr>
              </thead>
              <tbody>
                {users.map((user) => (
                  <tr key={user.id} className="table-row-clickable" onClick={() => onOpenUser(user.id)}>
                    <td>
                      <span className="table-cell-icon"><UserRound size={15} aria-hidden="true" />{user.displayName}</span>
                      <div className="table-cell-subtext">{user.email}</div>
                    </td>
                    <td>{ROLE_LABELS[user.role]}</td>
                    <td>
                      <span className={user.active ? 'status-pill status-ok' : 'status-pill status-off'}>
                        {user.active ? 'Activo' : 'Inactivo'}
                      </span>
                    </td>
                    <td>{user.role === 'SUPERADMIN' ? 'Acceso global' : user.branchCount}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  )
}
