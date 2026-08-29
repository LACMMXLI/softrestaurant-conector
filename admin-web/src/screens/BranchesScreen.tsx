import { useState } from 'react'
import type { FormEvent } from 'react'
import { Plus, Store, X } from 'lucide-react'
import { api, ApiError } from '../api'
import type { Branch } from '../types'

type BranchesScreenProps = {
  branches: Branch[]
  loading: boolean
  error: string | null
  onOpenBranch: (code: string) => void
  onBranchCreated: (branch: Branch) => void
  onUnauthorized: () => void
}

export function BranchesScreen({
  branches,
  loading,
  error,
  onOpenBranch,
  onBranchCreated,
  onUnauthorized,
}: BranchesScreenProps) {
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
      const branch = await api.createBranch(code.trim(), name.trim(), timezone.trim())
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
      <section className="panel-card" aria-labelledby="branches-title">
        <div className="panel-card-header">
          <h2 id="branches-title">Sucursales</h2>
          <button
            className="secondary-button"
            type="button"
            onClick={() => setFormOpen((open) => !open)}
          >
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
              <input
                type="text"
                value={name}
                onChange={(event) => setName(event.target.value)}
                maxLength={200}
                placeholder="Sucursal Centro"
                required
              />
            </label>
            <label>
              Zona horaria
              <input
                type="text"
                value={timezone}
                onChange={(event) => setTimezone(event.target.value)}
                placeholder="America/Tijuana"
              />
            </label>
            <button className="primary-button" type="submit" disabled={busy}>
              <span>{busy ? 'Creando…' : 'Crear sucursal'}</span>
            </button>
          </form>
        ) : null}

        {formError ? <p className="form-error" role="alert">{formError}</p> : null}
        {error ? <p className="form-error" role="alert">{error}</p> : null}

        {loading ? (
          <p className="panel-hint">Cargando sucursales…</p>
        ) : branches.length === 0 ? (
          <p className="panel-hint">Todavía no hay sucursales dadas de alta.</p>
        ) : (
          <div className="table-scroll">
            <table>
              <thead>
                <tr>
                  <th>Sucursal</th>
                  <th>Código</th>
                  <th>Zona horaria</th>
                  <th>Estado</th>
                  <th>Última sincronización</th>
                </tr>
              </thead>
              <tbody>
                {branches.map((branch) => (
                  <tr
                    key={branch.code}
                    className="table-row-clickable"
                    onClick={() => onOpenBranch(branch.code)}
                  >
                    <td>
                      <span className="table-cell-icon"><Store size={15} aria-hidden="true" />{branch.name}</span>
                    </td>
                    <td><code>{branch.code}</code></td>
                    <td>{branch.timezone}</td>
                    <td>
                      <span className={branch.active ? 'status-pill status-ok' : 'status-pill status-off'}>
                        {branch.active ? 'Activa' : 'Inactiva'}
                      </span>
                    </td>
                    <td>{branch.lastSyncAt ? new Date(branch.lastSyncAt).toLocaleString('es-MX') : 'Nunca'}</td>
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
