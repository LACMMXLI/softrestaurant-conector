import { useState } from 'react'
import type { FormEvent } from 'react'
import { ArrowLeft, Ban, CheckCircle2, RefreshCw, Save } from 'lucide-react'
import { api, ApiError } from '../api'
import { ConnectorsScreen } from './ConnectorsScreen'
import type { Branch } from '../types'

type BranchDetailScreenProps = {
  branch: Branch
  onBack: () => void
  onBranchUpdated: (branch: Branch) => void
  onUnauthorized: () => void
}

export function BranchDetailScreen({ branch, onBack, onBranchUpdated, onUnauthorized }: BranchDetailScreenProps) {
  const [name, setName] = useState(branch.name)
  const [timezone, setTimezone] = useState(branch.timezone)
  const [saving, setSaving] = useState(false)
  const [togglingStatus, setTogglingStatus] = useState(false)
  const [requestingSync, setRequestingSync] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function handleSave(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setSaving(true)
    setError(null)
    try {
      const updated = await api.updateBranch(branch.code, name.trim(), timezone.trim())
      onBranchUpdated(updated)
    } catch (reason) {
      if (reason instanceof ApiError && reason.status === 401) return onUnauthorized()
      setError(reason instanceof Error ? reason.message : 'No fue posible guardar los cambios.')
    } finally {
      setSaving(false)
    }
  }

  async function handleRequestSync() {
    setRequestingSync(true)
    setError(null)
    try {
      const updated = await api.requestSync(branch.code)
      onBranchUpdated(updated)
    } catch (reason) {
      if (reason instanceof ApiError && reason.status === 401) return onUnauthorized()
      setError(reason instanceof Error ? reason.message : 'No fue posible solicitar la sincronización.')
    } finally {
      setRequestingSync(false)
    }
  }

  async function handleToggleActive() {
    setTogglingStatus(true)
    setError(null)
    try {
      const updated = await api.setBranchActive(branch.code, !branch.active)
      onBranchUpdated(updated)
    } catch (reason) {
      if (reason instanceof ApiError && reason.status === 401) return onUnauthorized()
      setError(reason instanceof Error ? reason.message : 'No fue posible cambiar el estado.')
    } finally {
      setTogglingStatus(false)
    }
  }

  return (
    <div className="panel-stack">
      <button className="icon-button back-button" type="button" onClick={onBack}>
        <ArrowLeft size={17} aria-hidden="true" />
        <span>Volver a sucursales</span>
      </button>

      <section className="panel-card" aria-labelledby="branch-detail-title">
        <div className="panel-card-header">
          <h2 id="branch-detail-title">
            {branch.name} <code className="branch-code-badge">{branch.code}</code>
          </h2>
          <span className={branch.active ? 'status-pill status-ok' : 'status-pill status-off'}>
            {branch.active ? 'Activa' : 'Inactiva'}
          </span>
        </div>

        <div className="panel-card-footer">
          <button
            className="secondary-button"
            type="button"
            onClick={() => void handleRequestSync()}
            disabled={requestingSync || !branch.active}
          >
            <RefreshCw size={16} aria-hidden="true" className={requestingSync ? 'spinning' : ''} />
            <span>{requestingSync ? 'Solicitando…' : 'Sincronizar ahora'}</span>
          </button>
          <p className="panel-hint">
            {branch.syncRequestedAt
              ? `Última solicitud: ${new Date(branch.syncRequestedAt).toLocaleString('es-MX')}. El agente la recoge en su siguiente latido.`
              : 'Pide al agente de esta sucursal que sincronice en cuanto pueda, en vez de esperar a su próximo ciclo automático.'}
          </p>
        </div>

        <form className="inline-form" onSubmit={handleSave}>
          <label>
            Nombre
            <input
              type="text"
              value={name}
              onChange={(event) => setName(event.target.value)}
              maxLength={200}
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
              required
            />
          </label>
          <button className="primary-button" type="submit" disabled={saving}>
            <Save size={16} aria-hidden="true" />
            <span>{saving ? 'Guardando…' : 'Guardar cambios'}</span>
          </button>
        </form>

        {error ? <p className="form-error" role="alert">{error}</p> : null}

        <div className="panel-card-footer">
          <button
            className={branch.active ? 'secondary-button danger-outline' : 'secondary-button'}
            type="button"
            onClick={() => void handleToggleActive()}
            disabled={togglingStatus}
          >
            {branch.active ? <Ban size={16} aria-hidden="true" /> : <CheckCircle2 size={16} aria-hidden="true" />}
            <span>
              {togglingStatus
                ? 'Actualizando…'
                : branch.active
                  ? 'Desactivar sucursal'
                  : 'Activar sucursal'}
            </span>
          </button>
          <p className="panel-hint">
            Desactivar no borra ventas, turnos ni conectores: solo oculta la sucursal del dashboard
            operativo y detiene nuevas activaciones. Puede reactivarse en cualquier momento.
          </p>
        </div>
      </section>

      <ConnectorsScreen branch={branch} onUnauthorized={onUnauthorized} onBranchUpdated={onBranchUpdated} />
    </div>
  )
}
