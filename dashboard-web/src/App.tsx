import { useCallback, useEffect, useMemo, useState } from 'react'
import { BarChart3, CalendarDays, LayoutDashboard, Menu, ReceiptText, RefreshCw, Store, UserRound } from 'lucide-react'
import { api, ApiError } from './api'
import { LoginScreen } from './components/LoginScreen'
import { TicketSheet } from './components/TicketSheet'
import { dateInTimezone } from './format'
import { DashboardScreen } from './screens/DashboardScreen'
import { MoreScreen } from './screens/MoreScreen'
import { OperationsScreen } from './screens/OperationsScreen'
import { SalesScreen } from './screens/SalesScreen'
import type { DashboardBranch, DashboardHome, DashboardUser } from './types'

type SessionState = 'loading' | 'anonymous' | 'authenticated'
type Tab = 'home' | 'sales' | 'operations' | 'more'

const storedBranchKey = 'sr-dashboard:v1:branch'

export function App() {
  const [sessionState, setSessionState] = useState<SessionState>('loading')
  const [user, setUser] = useState<DashboardUser | null>(null)
  const [branches, setBranches] = useState<DashboardBranch[]>([])
  const [branchCode, setBranchCode] = useState('')
  const [date, setDate] = useState('')
  const [tab, setTab] = useState<Tab>('home')
  const [dashboard, setDashboard] = useState<DashboardHome | null>(null)
  const [dashboardLoading, setDashboardLoading] = useState(false)
  const [dashboardError, setDashboardError] = useState<string | null>(null)
  const [loginError, setLoginError] = useState<string | null>(null)
  const [loginBusy, setLoginBusy] = useState(false)
  const [selectedFolio, setSelectedFolio] = useState<number | null>(null)
  const [refreshKey, setRefreshKey] = useState(0)

  const currentBranch = useMemo(
    () => branches.find((branch) => branch.code === branchCode) ?? null,
    [branchCode, branches],
  )

  const becomeAnonymous = useCallback(() => {
    setSessionState('anonymous')
    setUser(null)
    setBranches([])
    setDashboard(null)
    setSelectedFolio(null)
  }, [])

  const closeTicket = useCallback(() => setSelectedFolio(null), [])

  const applyBranches = useCallback((availableBranches: DashboardBranch[]) => {
    setBranches(availableBranches)
    const stored = localStorage.getItem(storedBranchKey)
    const selected = availableBranches.find((branch) => branch.code === stored) ?? availableBranches[0]
    if (!selected) {
      setBranchCode('')
      setDate('')
      return
    }
    setBranchCode(selected.code)
    setDate(dateInTimezone(selected.timezone))
  }, [])

  useEffect(() => {
    let active = true
    api.me()
      .then(async (session) => ({ session, availableBranches: await api.branches() }))
      .then(({ session, availableBranches }) => {
        if (!active) return
        setUser(session.user)
        applyBranches(availableBranches)
        setSessionState('authenticated')
      })
      .catch(() => {
        if (active) becomeAnonymous()
      })
    return () => { active = false }
  }, [applyBranches, becomeAnonymous])

  useEffect(() => {
    if (sessionState !== 'authenticated' || !branchCode || !date) return
    const controller = new AbortController()
    setDashboardLoading(true)
    setDashboardError(null)
    setDashboard(null)
    api.dashboard(branchCode, date, controller.signal)
      .then((nextDashboard) => {
        if (!controller.signal.aborted) setDashboard(nextDashboard)
      })
      .catch((reason: unknown) => {
        if (reason instanceof DOMException && reason.name === 'AbortError') return
        if (reason instanceof ApiError && reason.status === 401) becomeAnonymous()
        else setDashboardError(reason instanceof Error ? reason.message : 'No fue posible cargar el dashboard.')
      })
      .finally(() => {
        if (!controller.signal.aborted) setDashboardLoading(false)
      })
    return () => controller.abort()
  }, [becomeAnonymous, branchCode, date, refreshKey, sessionState])

  async function handleLogin(email: string, password: string) {
    setLoginBusy(true)
    setLoginError(null)
    try {
      const session = await api.login(email, password)
      const resolvedBranches = await api.branches()
      setUser(session.user)
      applyBranches(resolvedBranches)
      setSessionState('authenticated')
    } catch (reason) {
      setLoginError(reason instanceof Error ? reason.message : 'No fue posible iniciar sesión.')
    } finally {
      setLoginBusy(false)
    }
  }

  async function handleLogout() {
    try {
      await api.logout()
    } finally {
      becomeAnonymous()
    }
  }

  function handleBranchChange(nextCode: string) {
    const nextBranch = branches.find((branch) => branch.code === nextCode)
    if (!nextBranch) return
    localStorage.setItem(storedBranchKey, nextCode)
    setBranchCode(nextCode)
    setDate(dateInTimezone(nextBranch.timezone))
    setDashboard(null)
  }

  if (sessionState === 'loading') {
    return (
      <main className="startup-screen" aria-label="Iniciando dashboard">
        <span className="brand-mark"><ReceiptText size={26} /></span>
        <p>Preparando el pulso operativo…</p>
      </main>
    )
  }

  if (sessionState === 'anonymous' || !user) {
    return <LoginScreen error={loginError} busy={loginBusy} onLogin={handleLogin} />
  }

  if (!currentBranch) {
    return (
      <main className="startup-screen no-branches">
        <Store size={28} />
        <h1>Sin sucursales asignadas</h1>
        <p>La cuenta es válida, pero todavía no tiene una sucursal disponible.</p>
        <button className="secondary-button" type="button" onClick={() => void handleLogout()}>Cerrar sesión</button>
      </main>
    )
  }

  return (
    <div className="app-shell">
      <aside className="desktop-sidebar">
        <div className="sidebar-brand"><ReceiptText size={22} /><span>Pulso</span></div>
        <Navigation tab={tab} onChange={setTab} />
        <div className="sidebar-user"><UserRound size={18} /><span>{user.displayName}</span></div>
      </aside>

      <div className="app-column">
        <header className="app-header">
          <div className="mobile-brand"><ReceiptText size={20} /><span>Pulso</span></div>
          <div className="context-controls">
            <label className="context-select">
              <Store size={17} aria-hidden="true" />
              <span className="sr-only">Sucursal</span>
              <select value={branchCode} onChange={(event) => handleBranchChange(event.target.value)}>
                {branches.map((branch) => <option value={branch.code} key={branch.code}>{branch.name}</option>)}
              </select>
            </label>
            <label className="context-date">
              <CalendarDays size={17} aria-hidden="true" />
              <span className="sr-only">Fecha</span>
              <input type="date" value={date} onChange={(event) => setDate(event.target.value)} />
            </label>
            <button className="icon-button refresh-button" type="button" onClick={() => setRefreshKey((value) => value + 1)} aria-label="Actualizar datos">
              <RefreshCw size={18} className={dashboardLoading ? 'spinning' : ''} />
            </button>
          </div>
        </header>

        <main className="app-main">
          {tab === 'home' ? (
            <DashboardScreen
              data={dashboard}
              loading={dashboardLoading}
              error={dashboardError}
              onRetry={() => setRefreshKey((value) => value + 1)}
              onOpenTicket={setSelectedFolio}
              onOpenSales={() => setTab('sales')}
            />
          ) : null}
          {tab === 'sales' ? (
            <SalesScreen key={`${branchCode}:${date}`} branchCode={branchCode} date={date} onOpenTicket={setSelectedFolio} onUnauthorized={becomeAnonymous} />
          ) : null}
          {tab === 'operations' ? (
            <OperationsScreen
              key={`${branchCode}:${date}`}
              branchCode={branchCode}
              date={date}
              data={dashboard}
              loading={dashboardLoading}
              onUnauthorized={becomeAnonymous}
            />
          ) : null}
          {tab === 'more' ? <MoreScreen user={user} branch={currentBranch} dashboard={dashboard} onLogout={handleLogout} /> : null}
        </main>

        <nav className="mobile-nav" aria-label="Navegación principal">
          <Navigation tab={tab} onChange={setTab} />
        </nav>
      </div>

      {selectedFolio !== null ? (
        <TicketSheet branchCode={branchCode} folio={selectedFolio} onClose={closeTicket} onUnauthorized={becomeAnonymous} />
      ) : null}
    </div>
  )
}

function Navigation({ tab, onChange }: { tab: Tab; onChange: (tab: Tab) => void }) {
  const items: Array<{ key: Tab; label: string; icon: React.ReactNode }> = [
    { key: 'home', label: 'Inicio', icon: <LayoutDashboard size={20} /> },
    { key: 'sales', label: 'Ventas', icon: <BarChart3 size={20} /> },
    { key: 'operations', label: 'Operación', icon: <ReceiptText size={20} /> },
    { key: 'more', label: 'Más', icon: <Menu size={20} /> },
  ]
  return (
    <div className="nav-items">
      {items.map((item) => (
        <button
          type="button"
          key={item.key}
          className={tab === item.key ? 'nav-item active' : 'nav-item'}
          onClick={() => onChange(item.key)}
          aria-current={tab === item.key ? 'page' : undefined}
        >
          {item.icon}<span>{item.label}</span>
        </button>
      ))}
    </div>
  )
}
