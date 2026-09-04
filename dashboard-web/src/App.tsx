import { useCallback, useEffect, useMemo, useState } from 'react'
import { BarChart3, Building2, CalendarDays, CreditCard, LayoutDashboard, LogOut, Menu, ReceiptText, RefreshCw, Store, UserRound } from 'lucide-react'
import { api, ApiError } from './api'
import { Brand } from './components/Brand'
import { LoginScreen } from './components/LoginScreen'
import { TicketSheet } from './components/TicketSheet'
import { dateInTimezone } from './format'
import { BusinessesScreen } from './screens/BusinessesScreen'
import { DashboardScreen } from './screens/DashboardScreen'
import { BusinessDashboardScreen } from './screens/BusinessDashboardScreen'
import { MoreScreen } from './screens/MoreScreen'
import { OperationsScreen } from './screens/OperationsScreen'
import { ProductCancellationsScreen } from './screens/ProductCancellationsScreen'
import { SalesScreen } from './screens/SalesScreen'
import type { BusinessDashboard, BusinessMembership, DashboardBranch, DashboardHome, DashboardShift, DashboardUser, Subscription } from './types'

type SessionState = 'loading' | 'anonymous' | 'authenticated'
type Tab = 'home' | 'sales' | 'operations' | 'cancellations' | 'businesses' | 'more'
type SelectedTicket = { folio: number; openAccount: boolean }

const storedBranchKey = 'sr-dashboard:v1:branch'

export function App() {
  const [sessionState, setSessionState] = useState<SessionState>('loading')
  const [user, setUser] = useState<DashboardUser | null>(null)
  const [subscription, setSubscription] = useState<Subscription | null>(null)
  const [branches, setBranches] = useState<DashboardBranch[]>([])
  const [businesses, setBusinesses] = useState<BusinessMembership[]>([])
  const [branchCode, setBranchCode] = useState('')
  const [date, setDate] = useState('')
  const [shifts, setShifts] = useState<DashboardShift[]>([])
  const [shiftId, setShiftId] = useState<number | null>(null)
  const [tab, setTab] = useState<Tab>('home')
  const [dashboard, setDashboard] = useState<DashboardHome | null>(null)
  const [businessDashboard, setBusinessDashboard] = useState<BusinessDashboard | null>(null)
  const [dashboardLoading, setDashboardLoading] = useState(false)
  const [dashboardError, setDashboardError] = useState<string | null>(null)
  const [loginError, setLoginError] = useState<string | null>(null)
  const [loginBusy, setLoginBusy] = useState(false)
  const [selectedTicket, setSelectedTicket] = useState<SelectedTicket | null>(null)
  const [refreshKey, setRefreshKey] = useState(0)

  const currentBranch = useMemo(
    () => branches.find((branch) => branch.code === branchCode) ?? null,
    [branchCode, branches],
  )
  const selectedBusinessId = branchCode.startsWith('all:') ? branchCode.slice(4) : null
  const historyMinimumDate = useMemo(() => {
    const today = new Date()
    today.setUTCDate(today.getUTCDate() - ((subscription?.plan === 'PLUS' ? 7 : 3) - 1))
    return today.toISOString().slice(0, 10)
  }, [subscription?.plan])

  const becomeAnonymous = useCallback(() => {
    setSessionState('anonymous')
    setUser(null)
    setSubscription(null)
    setBranches([])
    setBusinesses([])
    setDashboard(null)
    setBusinessDashboard(null)
    setShifts([])
    setShiftId(null)
    setSelectedTicket(null)
  }, [])

  const closeTicket = useCallback(() => setSelectedTicket(null), [])

  const updateBranch = useCallback((next: DashboardBranch) => {
    setBranches((current) => current.map((branch) => (branch.code === next.code ? next : branch)))
  }, [])

  const applyBranches = useCallback((availableBranches: DashboardBranch[]) => {
    setBranches(availableBranches)
    const stored = localStorage.getItem(storedBranchKey)
    const selected = availableBranches.find((branch) => branch.code === stored) ?? availableBranches[0]
    if (!selected) {
      setBranchCode('')
      setDate('')
      setShifts([])
      setShiftId(null)
      return
    }
    setBranchCode(selected.code)
    setDate(dateInTimezone(selected.timezone))
    setShiftId(null)
    void api.shifts(selected.code).then((available) => {
      setShifts(available)
      const current = available.find((shift) => shift.isOpen) ?? available[0]
      setShiftId(current?.id ?? null)
      if (current?.openedAt) setDate(current.openedAt.slice(0, 10))
    }).catch(() => setShifts([]))
  }, [])

  useEffect(() => {
    let active = true
    api.me()
      .then(async (session) => ({ session, availableBranches: session.subscription.canAccessContent ? await api.branches() : [], availableBusinesses: session.subscription.canAccessContent ? await api.businesses() : [] }))
      .then(({ session, availableBranches, availableBusinesses }) => {
        if (!active) return
        setUser(session.user)
        setSubscription(session.subscription)
        setBusinesses(availableBusinesses)
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
    setBusinessDashboard(null)
    const request = selectedBusinessId
      ? api.businessDashboard(selectedBusinessId, date, controller.signal)
      : api.dashboard(branchCode, date, shiftId, controller.signal)
    request
      .then((nextDashboard) => {
        if (controller.signal.aborted) return
        if (selectedBusinessId) setBusinessDashboard(nextDashboard as BusinessDashboard)
        else setDashboard(nextDashboard as DashboardHome)
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
  }, [becomeAnonymous, branchCode, date, refreshKey, selectedBusinessId, sessionState, shiftId])

  async function handleLogin(email: string, password: string) {
    setLoginBusy(true)
    setLoginError(null)
    try {
      const session = await api.login(email, password)
      const resolvedBranches = session.subscription.canAccessContent ? await api.branches() : []
      const resolvedBusinesses = session.subscription.canAccessContent ? await api.businesses() : []
      setUser(session.user)
      setSubscription(session.subscription)
      setBusinesses(resolvedBusinesses)
      applyBranches(resolvedBranches)
      setSessionState('authenticated')
    } catch (reason) {
      setLoginError(reason instanceof Error ? reason.message : 'No fue posible iniciar sesión.')
    } finally {
      setLoginBusy(false)
    }
  }

  async function handleRegister(email: string, password: string, displayName: string) {
    setLoginBusy(true)
    setLoginError(null)
    try {
      const session = await api.register(email, password, displayName)
      // Cuenta recién creada: sin negocios todavía, applyBranches([]) deja al usuario en la
      // pantalla "sin sucursales" (que ahora ofrece directamente crear su primer negocio).
      setUser(session.user)
      setSubscription(session.subscription)
      applyBranches([])
      setSessionState('authenticated')
    } catch (reason) {
      setLoginError(reason instanceof Error ? reason.message : 'No fue posible crear la cuenta.')
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
    if (nextCode.startsWith('all:')) {
      localStorage.setItem(storedBranchKey, nextCode)
      setBranchCode(nextCode)
      setDate(dateInTimezone('America/Tijuana'))
      setShiftId(null)
      setShifts([])
      setDashboard(null)
      setBusinessDashboard(null)
      setTab('home')
      return
    }
    const nextBranch = branches.find((branch) => branch.code === nextCode)
    if (!nextBranch) return
    localStorage.setItem(storedBranchKey, nextCode)
    setBranchCode(nextCode)
    setDate(dateInTimezone(nextBranch.timezone))
    setShiftId(null)
    void api.shifts(nextCode).then((available) => {
      setShifts(available)
      const current = available.find((shift) => shift.isOpen) ?? available[0]
      setShiftId(current?.id ?? null)
      if (current?.openedAt) setDate(current.openedAt.slice(0, 10))
    }).catch(() => setShifts([]))
    setDashboard(null)
    setBusinessDashboard(null)
  }

  if (sessionState === 'loading') {
    return (
      <main className="startup-screen" aria-label="Iniciando dashboard">
        <Brand compact />
        <p>Preparando RestaurantAgent…</p>
      </main>
    )
  }

  if (sessionState === 'anonymous' || !user) {
    return <LoginScreen error={loginError} busy={loginBusy} onLogin={handleLogin} onRegister={handleRegister} />
  }

  if (subscription && !subscription.canAccessContent) {
    const suspended = subscription.status === 'SUSPENDED'
    return (
      <main className="subscription-wall">
        <section className="subscription-wall-card" role="alert" aria-labelledby="subscription-title">
          <span className="subscription-wall-icon"><CreditCard size={30} /></span>
          <p className="subscription-wall-kicker">Acceso al restaurante pausado</p>
          <h1 id="subscription-title">{suspended ? 'Tu cuenta está desactivada' : 'Tu periodo de acceso terminó'}</h1>
          <p>{suspended
            ? 'El administrador suspendió temporalmente esta cuenta. Tus negocios, sucursales e historial permanecen guardados.'
            : 'La prueba gratuita de 15 días o el periodo contratado ya venció. Tus datos no se eliminaron, pero no pueden visualizarse hasta renovar.'}</p>
          <div className="subscription-wall-plan">Plan {subscription.plan === 'PLUS' ? 'Plus' : 'Basic'}</div>
          <p>Comunícate directamente con el administrador para registrar tu pago o solicitar una activación.</p>
          <button className="primary-button" type="button" onClick={() => void handleLogout()}><LogOut size={17} /> Cerrar sesión</button>
        </section>
      </main>
    )
  }

  if (!currentBranch && !selectedBusinessId) {
    // Cuenta válida pero sin ninguna sucursal accesible todavía — el caso normal para un
    // usuario recién registrado, antes de crear su primer negocio/sucursal. En vez de un
    // callejón sin salida, se ofrece directamente la pantalla de negocios.
    return (
      <div className="app-shell">
        <div className="app-column">
          <header className="app-header">
            <Brand compact className="mobile-brand" />
          </header>
          <main className="app-main">
            <BusinessesScreen onUnauthorized={becomeAnonymous} />
            <button className="logout-button" type="button" onClick={() => void handleLogout()}>Cerrar sesión</button>
          </main>
        </div>
      </div>
    )
  }

  return (
    <div className="app-shell">
      <aside className="desktop-sidebar">
        <Brand className="sidebar-brand" />
        <Navigation tab={tab} onChange={setTab} />
        <div className="sidebar-user"><UserRound size={18} /><span>{user.displayName}</span></div>
      </aside>

      <div className="app-column">
        <header className="app-header">
          <Brand compact className="mobile-brand" />
          <div className="context-controls">
            <label className="context-select">
              <Store size={17} aria-hidden="true" />
              <span className="sr-only">Sucursal</span>
              <select value={branchCode} onChange={(event) => handleBranchChange(event.target.value)}>
                {subscription?.plan === 'PLUS' && businesses.filter((business) => branches.some((branch) => branch.businessId === business.id)).map((business) => (
                  <option value={`all:${business.id}`} key={`all:${business.id}`}>Resumen general · {business.name}</option>
                ))}
                {branches.map((branch) => <option value={branch.code} key={branch.code}>{branch.name}</option>)}
              </select>
            </label>
            {!selectedBusinessId ? <label className="context-select">
              <span className="sr-only">Turno</span>
              <select value={shiftId ?? ''} onChange={(event) => {
                const next = shifts.find((shift) => shift.id === Number(event.target.value))
                setShiftId(next?.id ?? null)
                if (next?.openedAt) setDate(next.openedAt.slice(0, 10))
              }}>
                {shifts.length === 0 ? <option value="">Sin turnos sincronizados</option> : null}
                {shifts.map((shift) => <option value={shift.id} key={shift.id}>{shift.isOpen ? 'Abierto' : 'Cerrado'} · Turno {shift.number} · {shift.cashier || 'Sin cajero'}</option>)}
              </select>
            </label> : null}
            <label className="context-select context-date">
              <CalendarDays size={16} aria-hidden="true" />
              <span className="sr-only">Fecha</span>
              <input type="date" value={date} min={historyMinimumDate} onChange={(event) => {
                setDate(event.target.value)
                if (!selectedBusinessId) setShiftId(null)
              }} />
            </label>
            <button className="icon-button refresh-button" type="button" onClick={() => setRefreshKey((value) => value + 1)} aria-label="Actualizar datos">
              <RefreshCw size={18} className={dashboardLoading ? 'spinning' : ''} />
            </button>
          </div>
        </header>

        <main className="app-main">
          {tab === 'home' ? (
            selectedBusinessId ? <BusinessDashboardScreen
              data={businessDashboard}
              loading={dashboardLoading}
              error={dashboardError}
              onRetry={() => setRefreshKey((value) => value + 1)}
              onOpenBranch={handleBranchChange}
            /> : <DashboardScreen
              data={dashboard}
              loading={dashboardLoading}
              error={dashboardError}
              onRetry={() => setRefreshKey((value) => value + 1)}
              onOpenTicket={(folio, transient) => setSelectedTicket({ folio, openAccount: transient })}
              onOpenAccount={(folio) => setSelectedTicket({ folio, openAccount: true })}
              onOpenSales={() => setTab('sales')}
            />
          ) : null}
          {tab === 'sales' ? (
            <SalesScreen key={`${branchCode}:${shiftId}`} branchCode={branchCode} date={date} shiftId={shiftId} onOpenTicket={(folio, transient) => setSelectedTicket({ folio, openAccount: transient })} onUnauthorized={becomeAnonymous} />
          ) : null}
          {tab === 'operations' ? (
            <OperationsScreen
              key={`${branchCode}:${shiftId}`}
              branchCode={branchCode}
              date={date}
              shiftId={shiftId}
              data={dashboard}
              loading={dashboardLoading}
              onUnauthorized={becomeAnonymous}
            />
          ) : null}
          {tab === 'cancellations' ? <ProductCancellationsScreen branchCode={branchCode} date={date} shiftId={shiftId} onUnauthorized={becomeAnonymous} /> : null}
          {tab === 'businesses' ? <BusinessesScreen onUnauthorized={becomeAnonymous} /> : null}
          {tab === 'more' && currentBranch ? (
            <MoreScreen
              user={user}
              branch={currentBranch}
              dashboard={dashboard}
              onLogout={handleLogout}
              onBranchUpdated={updateBranch}
              onUnauthorized={becomeAnonymous}
            />
          ) : null}
        </main>

        <nav className="mobile-nav" aria-label="Navegación principal">
          <Navigation tab={tab} onChange={setTab} />
        </nav>
      </div>

      {selectedTicket !== null ? (
        <TicketSheet branchCode={branchCode} folio={selectedTicket.folio} openAccount={selectedTicket.openAccount} onClose={closeTicket} onUnauthorized={becomeAnonymous} />
      ) : null}
    </div>
  )
}

function Navigation({ tab, onChange }: { tab: Tab; onChange: (tab: Tab) => void }) {
  const items: Array<{ key: Tab; label: string; icon: React.ReactNode }> = [
    { key: 'home', label: 'Inicio', icon: <LayoutDashboard size={20} /> },
    { key: 'sales', label: 'Ventas', icon: <BarChart3 size={20} /> },
    { key: 'operations', label: 'Operación', icon: <ReceiptText size={20} /> },
    { key: 'cancellations', label: 'Cancelados', icon: <ReceiptText size={20} /> },
    { key: 'businesses', label: 'Negocios', icon: <Building2 size={20} /> },
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
