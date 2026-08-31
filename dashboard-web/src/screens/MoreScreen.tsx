import { useState } from 'react'
import { Database, LogOut, RefreshCw, Server, ShieldCheck, UserRound } from 'lucide-react'
import { StatusPill } from '../components/StatusPill'
import { timeAgo } from '../format'
import { api, ApiError } from '../api'
import type { DashboardBranch, DashboardHome, DashboardUser } from '../types'

type MoreScreenProps = {
  user: DashboardUser
  branch: DashboardBranch
  dashboard: DashboardHome | null
  onLogout: () => Promise<void>
  onBranchUpdated: (branch: DashboardBranch) => void
  onUnauthorized: () => void
}

export function MoreScreen({ user, branch, dashboard, onLogout, onBranchUpdated, onUnauthorized }: MoreScreenProps) {
  const [requestingSync, setRequestingSync] = useState(false)
  const [syncError, setSyncError] = useState<string | null>(null)

  async function handleRequestSync() {
    setRequestingSync(true)
    setSyncError(null)
    try {
      const result = await api.requestSync(branch.code)
      onBranchUpdated({ ...branch, syncRequestedAt: result.syncRequestedAt })
    } catch (reason) {
      // El rol de negocio (OWNER/MANAGER/VIEWER) ya no viaja en DashboardUser — central-api
      // rechaza con 403 si el rol del usuario en ESE negocio es VIEWER; se muestra tal cual.
      if (reason instanceof ApiError && reason.status === 401) return onUnauthorized()
      setSyncError(reason instanceof Error ? reason.message : 'No fue posible solicitar la sincronización.')
    } finally {
      setRequestingSync(false)
    }
  }

  return (
    <div className="screen-stack">
      <section className="screen-title">
        <p className="utility-label">Cuenta y sistema</p>
        <h1>Más</h1>
        <p>Identidad, alcance y estado técnico de la sucursal seleccionada.</p>
      </section>

      <section className="content-card profile-card">
        <span className="profile-avatar"><UserRound size={24} /></span>
        <div>
          <h2>{user.displayName}</h2>
          <p>{user.email}</p>
          <span className="role-badge"><ShieldCheck size={14} /> {roleLabel(user.role)}</span>
        </div>
      </section>

      <section className="content-card system-card">
        <div className="section-heading horizontal">
          <div>
            <p className="utility-label">Fuente seleccionada</p>
            <h2>{branch.name}</h2>
          </div>
          {dashboard ? <StatusPill freshness={dashboard.meta.freshness} coverage={dashboard.meta.coverage} /> : null}
        </div>
        <dl className="detail-list">
          <div><dt><Server size={16} /> Código</dt><dd>{branch.code}</dd></div>
          <div><dt><Database size={16} /> Última sincronización</dt><dd>{timeAgo(branch.lastSyncAt)}</dd></div>
          <div><dt>Zona horaria</dt><dd>{branch.timezone}</dd></div>
          <div><dt>Conciliación</dt><dd>{branch.reconciliationOk === null ? 'Sin lote' : branch.reconciliationOk ? 'Correcta' : 'Pendiente'}</dd></div>
        </dl>

        <button className="secondary-button" type="button" onClick={() => void handleRequestSync()} disabled={requestingSync}>
          <RefreshCw size={16} aria-hidden="true" />
          <span>{requestingSync ? 'Solicitando…' : 'Sincronizar ahora'}</span>
        </button>
        <p className="panel-hint">
          {branch.syncRequestedAt
            ? `Última solicitud: ${new Date(branch.syncRequestedAt).toLocaleString('es-MX')}. El agente la recoge en su siguiente latido.`
            : 'Pide al agente de esta sucursal que sincronice en cuanto pueda.'}
        </p>
        {syncError ? <p className="form-error" role="alert">{syncError}</p> : null}
      </section>

      <button className="logout-button" type="button" onClick={() => void onLogout()}>
        <LogOut size={19} /> Cerrar sesión
      </button>
    </div>
  )
}

function roleLabel(role: DashboardUser['role']) {
  return role === 'SUPERADMIN' ? 'Administrador de plataforma' : 'Cuenta'
}
