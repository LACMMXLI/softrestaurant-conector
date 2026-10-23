import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { Activity, ArrowLeft, Building2, LogIn, MousePointerClick, Users } from 'lucide-react'
import { api, ApiError } from '../api'
import { actionLabel, EVENT_TYPE_LABELS, VIEW_LABELS } from '../activityLabels'
import type { ActivityFilters, ActivityItem, ActivityPage, ActivitySummary, Branch, Business, UserSummary } from '../types'

type ActivityScreenProps = {
  users: UserSummary[]
  businesses: Business[]
  branches: Branch[]
  /** Si viene, el historial abre filtrado por esa cuenta y ofrece volver a su ficha. */
  initialUserId?: string
  onBackToUser?: () => void
  onUnauthorized: () => void
}

const dateOnly = (date: Date) => {
  const local = new Date(date.getTime() - date.getTimezoneOffset() * 60_000)
  return local.toISOString().slice(0, 10)
}

function defaultFilters(userId = ''): ActivityFilters {
  const from = new Date()
  from.setDate(from.getDate() - 6)
  return { userId, businessId: '', branchCode: '', eventType: '', fromDate: dateOnly(from), toDate: dateOnly(new Date()), includeAdmins: false }
}

export function ActivityScreen({ users, businesses, branches, initialUserId, onBackToUser, onUnauthorized }: ActivityScreenProps) {
  const [draft, setDraft] = useState<ActivityFilters>(() => defaultFilters(initialUserId))
  const [filters, setFilters] = useState<ActivityFilters>(draft)
  const [page, setPage] = useState(1)
  const [list, setList] = useState<ActivityPage | null>(null)
  const [summary, setSummary] = useState<ActivitySummary | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    setLoading(true)
    setError(null)
    Promise.all([api.activity(filters, page, controller.signal), api.activitySummary(filters, controller.signal)])
      .then(([nextList, nextSummary]) => {
        if (controller.signal.aborted) return
        setList(nextList)
        setSummary(nextSummary)
      })
      .catch((reason: unknown) => {
        if (controller.signal.aborted || (reason instanceof DOMException && reason.name === 'AbortError')) return
        if (reason instanceof ApiError && reason.status === 401) return onUnauthorized()
        setError(reason instanceof Error ? reason.message : 'No fue posible cargar la actividad.')
      })
      .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [filters, page, onUnauthorized])

  function apply(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setPage(1)
    setFilters(draft)
  }

  function reset() {
    const cleared = defaultFilters()
    setDraft(cleared)
    setPage(1)
    setFilters(cleared)
  }

  const visibleBranches = draft.businessId ? branches.filter((branch) => branch.businessId === draft.businessId) : branches
  const maxDay = Math.max(1, ...(summary?.byDay.map((point) => point.events) ?? [1]))

  return (
    <div className="panel-stack activity-screen">
      {onBackToUser ? (
        <button className="icon-button back-button" type="button" onClick={onBackToUser}>
          <ArrowLeft size={17} aria-hidden="true" />
          <span>Volver al usuario</span>
        </button>
      ) : null}

      <section className="panel-card" aria-labelledby="activity-title">
        <p className="section-kicker">Uso de la plataforma</p>
        <h1 id="activity-title">Actividad de usuarios</h1>
        <p className="panel-hint">
          Historial de visitas e interacciones de las cuentas con el panel web: quién entró, a qué negocio y sucursal
          consultó y qué hizo. No incluye contraseñas ni el texto de las búsquedas.
        </p>

        <form className="inline-form" onSubmit={apply}>
          <label>
            Usuario
            <select value={draft.userId} onChange={(event) => setDraft({ ...draft, userId: event.target.value })}>
              <option value="">Todos</option>
              {users.map((user) => <option key={user.id} value={user.id}>{user.displayName} · {user.email}</option>)}
            </select>
          </label>
          <label>
            Negocio
            <select value={draft.businessId} onChange={(event) => setDraft({ ...draft, businessId: event.target.value, branchCode: '' })}>
              <option value="">Todos</option>
              {businesses.map((business) => <option key={business.id} value={business.id}>{business.name}</option>)}
            </select>
          </label>
          <label>
            Sucursal
            <select value={draft.branchCode} onChange={(event) => setDraft({ ...draft, branchCode: event.target.value })}>
              <option value="">Todas</option>
              {visibleBranches.map((branch) => <option key={branch.code} value={branch.code}>{branch.name}</option>)}
            </select>
          </label>
          <label>
            Tipo
            <select value={draft.eventType} onChange={(event) => setDraft({ ...draft, eventType: event.target.value })}>
              <option value="">Todos</option>
              {Object.entries(EVENT_TYPE_LABELS).map(([key, label]) => <option key={key} value={key}>{label}</option>)}
            </select>
          </label>
          <label>
            Desde
            <input type="date" value={draft.fromDate} max={draft.toDate || undefined} onChange={(event) => setDraft({ ...draft, fromDate: event.target.value })} />
          </label>
          <label>
            Hasta
            <input type="date" value={draft.toDate} min={draft.fromDate || undefined} onChange={(event) => setDraft({ ...draft, toDate: event.target.value })} />
          </label>
          <label className="activity-check">
            <input type="checkbox" checked={draft.includeAdmins} onChange={(event) => setDraft({ ...draft, includeAdmins: event.target.checked })} />
            Incluir administradores de la plataforma
          </label>
          <button className="primary-button" type="submit" disabled={loading}>Aplicar</button>
          <button className="secondary-button" type="button" onClick={reset} disabled={loading}>Limpiar</button>
        </form>
        {error ? <p className="form-error" role="alert">{error}</p> : null}
      </section>

      {summary ? (
        <section className="panel-card" aria-label="Resumen del periodo">
          <div className="activity-totals">
            <Stat icon={<Activity size={18} />} label="Eventos" value={summary.totals.events} />
            <Stat icon={<Users size={18} />} label="Usuarios activos" value={summary.totals.activeUsers} />
            <Stat icon={<LogIn size={18} />} label="Visitas (aperturas)" value={summary.totals.appOpens} />
            <Stat icon={<Building2 size={18} />} label="Negocios activos" value={summary.totals.activeBusinesses} />
          </div>

          {summary.byDay.length > 0 ? (
            <div className="activity-days" role="img" aria-label="Eventos por día">
              {summary.byDay.map((point) => (
                <div className="activity-day" key={point.day} title={`${point.day}: ${point.events} eventos, ${point.users} usuarios`}>
                  <span className="activity-day-bar" style={{ height: `${Math.max(4, (point.events / maxDay) * 100)}%` }} />
                  <small>{point.day.slice(5)}</small>
                </div>
              ))}
            </div>
          ) : null}

          <div className="activity-tops">
            <TopList title="Usuarios más activos" rows={summary.topUsers.map((row) => ({ key: row.userId, label: row.name || row.email || 'Sin nombre', sub: row.email, events: row.events }))} />
            <TopList title="Acciones frecuentes" rows={summary.topActions.map((row) => ({ key: row.key, label: actionLabel(row.key), sub: null, events: row.events }))} />
            <TopList title="Negocios más activos" rows={summary.topBusinesses.map((row) => ({ key: row.key, label: row.label || 'Negocio eliminado', sub: null, events: row.events }))} />
          </div>
        </section>
      ) : null}

      <section className="panel-card" aria-labelledby="activity-list-title">
        <div className="panel-card-header">
          <h2 id="activity-list-title">Historial</h2>
          <span className="panel-hint">Página {page}</span>
        </div>
        {loading && !list ? <p className="panel-hint">Cargando actividad…</p> : null}
        {list && list.items.length === 0 ? <p className="panel-hint">No hay actividad registrada con estos filtros.</p> : null}
        {list && list.items.length > 0 ? (
          <div className="activity-list" role="list">
            {list.items.map((item) => <ActivityRow key={item.id} item={item} />)}
          </div>
        ) : null}
        <div className="activity-pagination">
          <button className="secondary-button" type="button" disabled={page === 1 || loading} onClick={() => setPage((value) => value - 1)}>Anterior</button>
          <button className="secondary-button" type="button" disabled={!list?.hasMore || loading} onClick={() => setPage((value) => value + 1)}>Siguiente</button>
        </div>
      </section>
    </div>
  )
}

function Stat({ icon, label, value }: { icon: React.ReactNode; label: string; value: number }) {
  return (
    <article className="activity-stat">
      <span aria-hidden="true">{icon}</span>
      <strong>{value.toLocaleString('es-MX')}</strong>
      <small>{label}</small>
    </article>
  )
}

function TopList({ title, rows }: { title: string; rows: Array<{ key: string; label: string; sub: string | null; events: number }> }) {
  return (
    <div className="activity-top">
      <h3>{title}</h3>
      {rows.length === 0 ? <p className="panel-hint">Sin datos.</p> : (
        <ol>
          {rows.map((row) => (
            <li key={row.key}>
              <span><b>{row.label}</b>{row.sub && row.sub !== row.label ? <small>{row.sub}</small> : null}</span>
              <strong>{row.events.toLocaleString('es-MX')}</strong>
            </li>
          ))}
        </ol>
      )}
    </div>
  )
}

function ActivityRow({ item }: { item: ActivityItem }) {
  const when = new Date(item.occurredAt)
  const failed = item.status !== null && item.status >= 400
  const where = [item.businessName, item.branchName ?? item.branchCode].filter(Boolean).join(' › ')
  const detail = typeof item.metadata.detail === 'string' ? item.metadata.detail : null
  return (
    <details className="activity-row" role="listitem">
      <summary>
        <span className="activity-icon" aria-hidden="true"><MousePointerClick size={16} /></span>
        <span className="activity-main">
          <b>{actionLabel(item.action)}{detail ? ` · ${detail}` : ''}</b>
          <small>
            {item.userName || item.userEmail || 'Sin sesión'}
            {item.view ? ` · ${VIEW_LABELS[item.view] ?? item.view}` : ''}
            {where ? ` · ${where}` : ''}
          </small>
        </span>
        <span className="activity-side">
          <time dateTime={item.occurredAt}>{when.toLocaleString('es-MX', { dateStyle: 'short', timeStyle: 'medium' })}</time>
          {failed ? <span className="status-pill status-off">{item.status}</span> : null}
        </span>
      </summary>
      <dl className="activity-detail">
        <div><dt>Tipo</dt><dd>{EVENT_TYPE_LABELS[item.eventType] ?? item.eventType} · {item.source === 'client' ? 'reportado por la app' : 'observado en el servidor'}</dd></div>
        {item.userEmail ? <div><dt>Correo</dt><dd>{item.userEmail}</dd></div> : null}
        {item.method ? <div><dt>Petición</dt><dd><code>{item.method} {item.path}</code> · {item.status} · {item.durationMs} ms</dd></div> : null}
        {item.ip ? <div><dt>IP</dt><dd>{item.ip}</dd></div> : null}
        {item.userAgent ? <div><dt>Navegador</dt><dd>{item.userAgent}</dd></div> : null}
        {item.sessionRef ? <div><dt>Sesión</dt><dd><code>{item.sessionRef}</code></dd></div> : null}
        {Object.keys(item.metadata).length > 0 ? <div><dt>Datos</dt><dd><code>{JSON.stringify(item.metadata)}</code></dd></div> : null}
      </dl>
    </details>
  )
}
