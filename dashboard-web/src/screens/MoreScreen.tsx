import { Database, LogOut, Server, ShieldCheck, UserRound } from 'lucide-react'
import { StatusPill } from '../components/StatusPill'
import { timeAgo } from '../format'
import type { DashboardBranch, DashboardHome, DashboardUser } from '../types'

type MoreScreenProps = {
  user: DashboardUser
  branch: DashboardBranch
  dashboard: DashboardHome | null
  onLogout: () => Promise<void>
}

export function MoreScreen({ user, branch, dashboard, onLogout }: MoreScreenProps) {
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
      </section>

      <button className="logout-button" type="button" onClick={() => void onLogout()}>
        <LogOut size={19} /> Cerrar sesión
      </button>
    </div>
  )
}

function roleLabel(role: DashboardUser['role']) {
  if (role === 'OWNER') return 'Propietario'
  if (role === 'MANAGER') return 'Gerente'
  return 'Consulta'
}
