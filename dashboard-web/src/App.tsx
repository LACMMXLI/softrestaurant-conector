import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { UserRound } from 'lucide-react'
import { api, ApiError } from './api'
import type { ActivityEvent } from './api'
import { Brand } from './components/Brand'
import { ContextControls } from './components/ContextControls'
import { LoginScreen } from './components/LoginScreen'
import { Navigation } from './components/Navigation'
import { ProductTotals } from './components/ProductTotals'
import { ExpiryNoticeDialog, SubscriptionAccessNotice, SubscriptionSummary } from './components/SubscriptionNotices'
import { TicketSheet } from './components/TicketSheet'
import { dateInTimezone } from './format'
import { getExpiryWarning } from './lib/subscription'
import { businessIdFromCode, isConsolidatedCode, isTabAvailable } from './lib/navigation'
import type { Tab } from './lib/navigation'
import { BusinessesScreen } from './screens/BusinessesScreen'
import { BusinessDashboardScreen } from './screens/BusinessDashboardScreen'
import { DashboardScreen } from './screens/DashboardScreen'
import { ExpenseCategoriesScreen } from './screens/ExpenseCategoriesScreen'
import { MoreScreen } from './screens/MoreScreen'
import { OperationsScreen } from './screens/OperationsScreen'
import { ProductCancellationsScreen } from './screens/ProductCancellationsScreen'
import { SalesScreen } from './screens/SalesScreen'
import type { BusinessDashboard, BusinessMembership, DashboardBranch, DashboardHome, DashboardSession, DashboardShift, DashboardUser, Subscription } from './types'

type SessionState = 'loading' | 'anonymous' | 'authenticated'
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
  const [rangeEnd, setRangeEnd] = useState('')
  const [shifts, setShifts] = useState<DashboardShift[]>([])
  const [shiftId, setShiftId] = useState<number | null>(null)
  // Mientras se resuelven los turnos de la sucursal no se consulta el dashboard: así se evita
  // pedirlo primero sin turno y de nuevo con el turno abierto (doble carga y parpadeo).
  const [contextLoading, setContextLoading] = useState(false)
  const [tab, setTab] = useState<Tab>('home')
  const [dashboard, setDashboard] = useState<DashboardHome | null>(null)
  const [businessDashboard, setBusinessDashboard] = useState<BusinessDashboard | null>(null)
  const [dashboardLoading, setDashboardLoading] = useState(false)
  const [dashboardError, setDashboardError] = useState<string | null>(null)
  const [loginError, setLoginError] = useState<string | null>(null)
  const [loginBusy, setLoginBusy] = useState(false)
  const [selectedTicket, setSelectedTicket] = useState<SelectedTicket | null>(null)
  const [refreshKey, setRefreshKey] = useState(0)
  // Aviso de cuenta por vencer: se reabre en cada inicio de sesión y recarga; cerrarlo solo lo oculta en esta carga.
  const [expiryNoticeOpen, setExpiryNoticeOpen] = useState(true)
  const shiftRequest = useRef(0)

  const currentBranch = useMemo(
    () => branches.find((branch) => branch.code === branchCode) ?? null,
    [branchCode, branches],
  )
  const selectedBusinessId = businessIdFromCode(branchCode)
  const consolidated = selectedBusinessId !== null
  const currentRole = businesses.find((business) => business.id === currentBranch?.businessId)?.role ?? null
  const isCurrentBusinessOwner = currentRole === 'OWNER'
  const canRequestSync = currentRole === 'OWNER' || currentRole === 'MANAGER'
  const historyMinimumDate = useMemo(() => {
    if (subscription?.historyDays === null) return undefined
    if (subscription?.historyDays === undefined) return undefined
    const today = new Date()
    today.setUTCDate(today.getUTCDate() - (subscription.historyDays - 1))
    return today.toISOString().slice(0, 10)
  }, [subscription?.historyDays])

  const becomeAnonymous = useCallback(() => {
    shiftRequest.current += 1
    setSessionState('anonymous')
    setUser(null)
    setSubscription(null)
    setBranches([])
    setBusinesses([])
    setDashboard(null)
    setBusinessDashboard(null)
    setShifts([])
    setShiftId(null)
    setContextLoading(false)
    setSelectedTicket(null)
  }, [])

  const closeTicket = useCallback(() => setSelectedTicket(null), [])

  const updateBranch = useCallback((next: DashboardBranch) => {
    setBranches((current) => current.map((branch) => (branch.code === next.code ? next : branch)))
  }, [])

  /**
   * Punto único para cambiar de sucursal o de resumen general: fija fecha y turno de forma
   * coherente y descarta respuestas de turnos de una selección anterior.
   */
  const selectScope = useCallback((code: string, available: DashboardBranch[]) => {
    const request = ++shiftRequest.current
    setBranchCode(code)
    setDashboard(null)
    setBusinessDashboard(null)
    setShifts([])
    setShiftId(null)

    const businessId = businessIdFromCode(code)
    if (businessId) {
      // El resumen general usa la zona horaria de las sucursales del negocio.
      const timezone = available.find((branch) => branch.businessId === businessId)?.timezone
        ?? Intl.DateTimeFormat().resolvedOptions().timeZone
      const today = dateInTimezone(timezone)
      setDate(today)
      setRangeEnd(today)
      setContextLoading(false)
      setTab((current) => (isTabAvailable(current, true) ? current : 'home'))
      return
    }

    const branch = available.find((candidate) => candidate.code === code)
    if (!branch) {
      setDate('')
      setContextLoading(false)
      return
    }
    const today = dateInTimezone(branch.timezone)
    setDate(today)
    setRangeEnd(today)
    setContextLoading(true)
    api.shifts(code)
      .then((availableShifts) => {
        if (request !== shiftRequest.current) return
        const current = availableShifts.find((shift) => shift.isOpen) ?? availableShifts[0]
        setShifts(availableShifts)
        setShiftId(current?.id ?? null)
        if (current?.openedAt) setDate(current.openedAt.slice(0, 10))
        setContextLoading(false)
      })
      .catch((reason: unknown) => {
        if (request !== shiftRequest.current) return
        if (reason instanceof ApiError && reason.status === 401) return becomeAnonymous()
        setShifts([])
        setContextLoading(false)
      })
  }, [becomeAnonymous])

  const openSession = useCallback(async (session: DashboardSession) => {
    const canAccess = session.subscription.canAccessContent
    const [availableBranches, availableBusinesses] = canAccess
      ? await Promise.all([api.branches(), api.businesses()])
      : [[] as DashboardBranch[], [] as BusinessMembership[]]
    setUser(session.user)
    setSubscription(session.subscription)
    setExpiryNoticeOpen(true)
    setBusinesses(availableBusinesses)
    setBranches(availableBranches)
    const stored = localStorage.getItem(storedBranchKey)
    const selected = availableBranches.find((branch) => branch.code === stored) ?? availableBranches[0]
    if (selected) selectScope(selected.code, availableBranches)
    else selectScope('', availableBranches)
    setSessionState('authenticated')
  }, [selectScope])

  useEffect(() => {
    let active = true
    api.me()
      .then((session) => (active ? openSession(session) : undefined))
      .catch(() => {
        if (active) becomeAnonymous()
      })
    return () => { active = false }
  }, [becomeAnonymous, openSession])

  useEffect(() => {
    if (sessionState !== 'authenticated' || !branchCode || !date || contextLoading) return
    const controller = new AbortController()
    setDashboardLoading(true)
    setDashboardError(null)
    setDashboard(null)
    setBusinessDashboard(null)
    const request = selectedBusinessId
      ? api.businessDashboard(selectedBusinessId, date, rangeEnd || date, controller.signal)
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
  }, [becomeAnonymous, branchCode, contextLoading, date, rangeEnd, refreshKey, selectedBusinessId, sessionState, shiftId])

  async function handleLogin(email: string, password: string) {
    setLoginBusy(true)
    setLoginError(null)
    try {
      await openSession(await api.login(email, password))
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
      // Cuenta recién creada: sin negocios todavía. Se deja al usuario en la pantalla de
      // negocios para que cree el primero, sin consultar listas que están vacías.
      setUser(session.user)
      setSubscription(session.subscription)
      setExpiryNoticeOpen(true)
      setBusinesses([])
      setBranches([])
      selectScope('', [])
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
    if (nextCode !== branchCode && (isConsolidatedCode(nextCode) || branches.some((branch) => branch.code === nextCode))) {
      localStorage.setItem(storedBranchKey, nextCode)
      const nextBranch = branches.find((branch) => branch.code === nextCode)
      api.trackActivity({
        kind: 'scope',
        view: tab,
        branchCode: isConsolidatedCode(nextCode) ? undefined : nextCode,
        businessId: businessIdFromCode(nextCode) ?? nextBranch?.businessId,
        detail: isConsolidatedCode(nextCode) ? 'resumen general' : undefined,
      })
      selectScope(nextCode, branches)
    }
  }

  function handleShiftChange(next: DashboardShift | null) {
    track('filter', tab, next ? `turno=${next.number}` : 'turno=ninguno')
    setShiftId(next?.id ?? null)
    if (next?.openedAt) setDate(next.openedAt.slice(0, 10))
  }

  function handleDateChange(nextDate: string) {
    track('filter', tab, `fecha=${nextDate}`)
    setDate(nextDate)
    if (!consolidated) setShiftId(null)
  }

  // Reporta la navegación al historial de actividad del operador (no bloquea ni muestra errores).
  const track = (kind: ActivityEvent['kind'], view?: Tab, detail?: string) =>
    api.trackActivity({
      kind,
      view,
      branchCode: consolidated ? undefined : branchCode || undefined,
      businessId: selectedBusinessId ?? currentBranch?.businessId,
      detail,
    })

  const changeTab = (next: Tab) => {
    if (next !== tab) track('view', next)
    setTab(next)
  }
  const refresh = () => {
    track('refresh', tab)
    setRefreshKey((value) => value + 1)
  }
  const openTicket = (folio: number, openAccount: boolean) => setSelectedTicket({ folio, openAccount })
  const loading = dashboardLoading || contextLoading
  // Las cuentas de operador (SUPERADMIN) no tienen un plan que renovar.
  const expiryWarning = user?.role === 'SUPERADMIN' ? null : getExpiryWarning(subscription)
  const expiryNotice = expiryWarning && expiryNoticeOpen
    ? <ExpiryNoticeDialog expiry={expiryWarning} onClose={() => setExpiryNoticeOpen(false)} />
    : null

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
    return <SubscriptionAccessNotice onLogout={() => void handleLogout()} />
  }

  if (!currentBranch && !consolidated) {
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
        {expiryNotice}
      </div>
    )
  }

  return (
    <div className="app-shell">
      <aside className="desktop-sidebar">
        <Brand className="sidebar-brand" />
        <Navigation tab={tab} consolidated={consolidated} onChange={changeTab} />
        <div className="sidebar-user"><UserRound size={18} /><span>{user.displayName}</span></div>
      </aside>

      <div className="app-column">
        <header className="app-header">
          <Brand compact className="mobile-brand" />
          <ContextControls
            branchCode={branchCode}
            branches={branches}
            businesses={businesses}
            subscription={subscription}
            shifts={shifts}
            shiftId={shiftId}
            date={date}
            rangeEnd={rangeEnd}
            minimumDate={historyMinimumDate}
            consolidated={consolidated}
            refreshing={loading}
            onBranchChange={handleBranchChange}
            onShiftChange={handleShiftChange}
            onDateChange={handleDateChange}
            onRangeEndChange={setRangeEnd}
            onRefresh={refresh}
          />
        </header>

        {subscription ? <SubscriptionSummary subscription={subscription} /> : null}

        <main className="app-main">
          {tab === 'home' ? (
            consolidated ? (
              <BusinessDashboardScreen
                data={businessDashboard}
                loading={loading}
                error={dashboardError}
                onRetry={refresh}
                onOpenBranch={handleBranchChange}
              />
            ) : (
              <DashboardScreen
                data={dashboard}
                loading={loading}
                error={dashboardError}
                onRetry={refresh}
                onOpenTicket={openTicket}
                onOpenAccount={(folio) => openTicket(folio, true)}
                onOpenSales={() => changeTab('sales')}
              />
            )
          ) : null}
          {tab === 'sales' && !consolidated ? (
            <SalesScreen key={`${branchCode}:${shiftId}`} branchCode={branchCode} date={date} shiftId={shiftId} onOpenTicket={openTicket} onUnauthorized={becomeAnonymous} />
          ) : null}
          {tab === 'operations' && !consolidated ? (
            <OperationsScreen
              key={`${branchCode}:${shiftId}`}
              branchCode={branchCode}
              date={date}
              shiftId={shiftId}
              data={dashboard}
              loading={loading}
              canManageExpenses={isCurrentBusinessOwner}
              onUnauthorized={becomeAnonymous}
            />
          ) : null}
          {tab === 'cancellations' && !consolidated ? (
            <ProductCancellationsScreen branchCode={branchCode} date={date} shiftId={shiftId} onUnauthorized={becomeAnonymous} />
          ) : null}
          {tab === 'products' ? (
            <ProductTotals
              items={consolidated ? businessDashboard?.topProducts.all ?? [] : dashboard?.topProducts.all ?? []}
              scope={consolidated ? `de todas las sucursales de ${businessDashboard?.businessName ?? 'este negocio'}` : `de la sucursal ${currentBranch?.name ?? ''}`}
              loading={loading}
              error={dashboardError}
            />
          ) : null}
          {tab === 'businesses' ? <BusinessesScreen onUnauthorized={becomeAnonymous} /> : null}
          {tab === 'expense-categories' && currentBranch ? (
            <ExpenseCategoriesScreen branchCode={currentBranch.code} canManage={isCurrentBusinessOwner} onUnauthorized={becomeAnonymous} />
          ) : null}
          {tab === 'more' ? (
            <MoreScreen
              user={user}
              branch={currentBranch}
              dashboard={dashboard}
              onLogout={handleLogout}
              onBranchUpdated={updateBranch}
              onOpenExpenseCategories={() => changeTab('expense-categories')}
              canManageExpenses={isCurrentBusinessOwner}
              canRequestSync={canRequestSync}
              onUnauthorized={becomeAnonymous}
            />
          ) : null}
        </main>

        <nav className="mobile-nav" aria-label="Navegación principal">
          <Navigation tab={tab} consolidated={consolidated} onChange={changeTab} />
        </nav>
      </div>

      {selectedTicket !== null ? (
        <TicketSheet branchCode={branchCode} folio={selectedTicket.folio} openAccount={selectedTicket.openAccount} onClose={closeTicket} onUnauthorized={becomeAnonymous} />
      ) : null}
      {expiryNotice}
    </div>
  )
}
