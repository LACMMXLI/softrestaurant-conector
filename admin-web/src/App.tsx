import { useCallback, useEffect, useState } from 'react'
import { Activity, Building2, LogOut, PanelsTopLeft, ShieldCheck, UsersRound } from 'lucide-react'
import { api, ApiError } from './api'
import { LoginScreen } from './components/LoginScreen'
import { ActivityScreen } from './screens/ActivityScreen'
import { BranchDetailScreen } from './screens/BranchDetailScreen'
import { BusinessDetailScreen } from './screens/BusinessDetailScreen'
import { BusinessesScreen } from './screens/BusinessesScreen'
import { UserDetailScreen } from './screens/UserDetailScreen'
import { UsersScreen } from './screens/UsersScreen'
import { SubscriptionNoticeScreen } from './screens/SubscriptionNoticeScreen'
import type { AdminUser, Branch, Business, UserDetail, UserSummary } from './types'

type SessionState = 'loading' | 'anonymous' | 'authenticated'
type View =
  | { name: 'businesses' }
  | { name: 'business-detail'; id: string }
  | { name: 'branch-detail'; code: string }
  | { name: 'users' }
  | { name: 'subscription-notice' }
  | { name: 'activity'; userId?: string }
  | { name: 'user-detail'; id: string }

export function App() {
  const [sessionState, setSessionState] = useState<SessionState>('loading')
  const [user, setUser] = useState<AdminUser | null>(null)
  const [branches, setBranches] = useState<Branch[]>([])
  const [branchesLoading, setBranchesLoading] = useState(false)
  const [branchesError, setBranchesError] = useState<string | null>(null)
  const [businesses, setBusinesses] = useState<Business[]>([])
  const [users, setUsers] = useState<UserSummary[]>([])
  const [usersLoading, setUsersLoading] = useState(false)
  const [usersError, setUsersError] = useState<string | null>(null)
  const [view, setView] = useState<View>({ name: 'businesses' })
  const [loginError, setLoginError] = useState<string | null>(null)
  const [loginBusy, setLoginBusy] = useState(false)

  const becomeAnonymous = useCallback((message?: string) => {
    setSessionState('anonymous')
    setUser(null)
    setBranches([])
    setBusinesses([])
    setUsers([])
    setView({ name: 'businesses' })
    if (message) setLoginError(message)
  }, [])

  const loadBranches = useCallback(async () => {
    setBranchesLoading(true)
    setBranchesError(null)
    try {
      const [branchList, businessList] = await Promise.all([api.branches(), api.businesses()])
      setBranches(branchList)
      setBusinesses(businessList)
    } catch (reason) {
      if (reason instanceof ApiError && reason.status === 401) return becomeAnonymous()
      setBranchesError(reason instanceof Error ? reason.message : 'No fue posible cargar las sucursales.')
    } finally {
      setBranchesLoading(false)
    }
  }, [becomeAnonymous])

  const loadUsers = useCallback(async () => {
    setUsersLoading(true)
    setUsersError(null)
    try {
      setUsers(await api.users())
    } catch (reason) {
      if (reason instanceof ApiError && reason.status === 401) return becomeAnonymous()
      setUsersError(reason instanceof Error ? reason.message : 'No fue posible cargar los usuarios.')
    } finally {
      setUsersLoading(false)
    }
  }, [becomeAnonymous])

  useEffect(() => {
    let active = true
    api.me()
      .then(async (session) => {
        if (!active) return
        if (session.user.role !== 'SUPERADMIN') {
          await api.logout().catch(() => undefined)
          becomeAnonymous('Esta cuenta no tiene permisos de administrador.')
          return
        }
        setUser(session.user)
        setSessionState('authenticated')
        await Promise.all([loadBranches(), loadUsers()])
      })
      .catch(() => {
        if (active) becomeAnonymous()
      })
    return () => { active = false }
  }, [becomeAnonymous, loadBranches, loadUsers])

  async function handleLogin(email: string, password: string) {
    setLoginBusy(true)
    setLoginError(null)
    try {
      const session = await api.login(email, password)
      if (session.user.role !== 'SUPERADMIN') {
        await api.logout().catch(() => undefined)
        setLoginError('Esta cuenta no tiene permisos de administrador.')
        return
      }
      setUser(session.user)
      setSessionState('authenticated')
      await Promise.all([loadBranches(), loadUsers()])
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

  function upsertBranchInList(next: Branch) {
    setBranches((current) => {
      const exists = current.some((branch) => branch.code === next.code)
      const updated = exists
        ? current.map((branch) => (branch.code === next.code ? next : branch))
        : [...current, next]
      return [...updated].sort((a, b) => a.name.localeCompare(b.name, 'es'))
    })
  }

  /** Negocio recién creado por el operador: entra a la lista y se abre para crear sus sucursales. */
  function handleBusinessCreated(created: Business) {
    setBusinesses((current) => [...current.filter((b) => b.id !== created.id), created].sort((a, b) => a.name.localeCompare(b.name, 'es')))
    setView({ name: 'business-detail', id: created.id })
  }

  /** Alta o cambio de una sucursal: refresca la lista y los conteos del negocio dueño. */
  function handleBranchChanged(next: Branch) {
    upsertBranchInList(next)
    api.businesses()
      .then(setBusinesses)
      .catch((reason: unknown) => {
        if (reason instanceof ApiError && reason.status === 401) becomeAnonymous()
      })
  }

  function upsertUserInList(next: UserDetail) {
    setUsers((current) => {
      const summary: UserSummary = {
        id: next.id,
        email: next.email,
        displayName: next.displayName,
        role: next.role,
        active: next.active,
        lastLoginAt: next.lastLoginAt,
        createdAt: next.createdAt,
        businessCount: next.businesses.length,
        subscription: next.subscription,
      }
      const exists = current.some((u) => u.id === next.id)
      const updated = exists ? current.map((u) => (u.id === next.id ? summary : u)) : [...current, summary]
      return [...updated].sort((a, b) => a.displayName.localeCompare(b.displayName, 'es'))
    })
  }

  if (sessionState === 'loading') {
    return (
      <main className="startup-screen" aria-label="Iniciando panel">
        <span className="brand-mark"><ShieldCheck size={26} /></span>
        <p>Preparando el panel de administración…</p>
      </main>
    )
  }

  if (sessionState === 'anonymous' || !user) {
    return <LoginScreen error={loginError} busy={loginBusy} onLogin={handleLogin} />
  }

  const detailBusiness = view.name === 'business-detail' ? businesses.find((b) => b.id === view.id) ?? null : null
  const detailBranch = view.name === 'branch-detail' ? branches.find((b) => b.code === view.code) ?? null : null
  const detailBranchBusiness = detailBranch ? businesses.find((b) => b.id === detailBranch.businessId) ?? null : null
  const detailUserSummary = view.name === 'user-detail' ? users.find((u) => u.id === view.id) ?? null : null

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <button className="sidebar-brand" type="button" onClick={() => setView({ name: 'businesses' })}>
          <span className="sidebar-brand-mark"><ShieldCheck size={19} /></span>
          <span><small>SoftRestaurant</small>Control de plataforma</span>
        </button>

        <nav className="sidebar-nav">
          <button
            type="button"
            className={view.name === 'businesses' || view.name === 'business-detail' || view.name === 'branch-detail' ? 'nav-item active' : 'nav-item'}
            onClick={() => setView({ name: 'businesses' })}
          >
            <Building2 size={18} aria-hidden="true" />
            <span>Negocios</span>
          </button>
          <button
            type="button"
            className={view.name === 'users' || view.name === 'user-detail' ? 'nav-item active' : 'nav-item'}
            onClick={() => setView({ name: 'users' })}
          >
            <UsersRound size={18} aria-hidden="true" />
            <span>Usuarios</span>
          </button>
          <button
            type="button"
            className={view.name === 'activity' ? 'nav-item active' : 'nav-item'}
            onClick={() => setView({ name: 'activity' })}
          >
            <Activity size={18} aria-hidden="true" />
            <span>Actividad</span>
          </button>
          <button
            type="button"
            className={view.name === 'subscription-notice' ? 'nav-item active' : 'nav-item'}
            onClick={() => setView({ name: 'subscription-notice' })}
          >
            <PanelsTopLeft size={18} aria-hidden="true" />
            <span>Avisos al cliente</span>
          </button>
        </nav>

        <div className="sidebar-footer">
          <div className="sidebar-user"><span>{user.displayName}</span></div>
          <button className="secondary-button" type="button" onClick={() => void handleLogout()}>
            <LogOut size={16} aria-hidden="true" />
            <span>Cerrar sesión</span>
          </button>
        </div>
      </aside>

      <main className="app-main">
        {view.name === 'branch-detail' && detailBranch ? (
          <BranchDetailScreen
            branch={detailBranch}
            businessName={detailBranchBusiness?.name ?? 'el negocio'}
            onBack={() => setView(detailBranchBusiness ? { name: 'business-detail', id: detailBranchBusiness.id } : { name: 'businesses' })}
            onBranchUpdated={handleBranchChanged}
            onUnauthorized={becomeAnonymous}
          />
        ) : null}

        {view.name === 'business-detail' && detailBusiness ? (
          <BusinessDetailScreen
            business={detailBusiness}
            branches={branches.filter((branch) => branch.businessId === detailBusiness.id)}
            onBack={() => setView({ name: 'businesses' })}
            onOpenBranch={(code) => setView({ name: 'branch-detail', code })}
            onBranchCreated={handleBranchChanged}
            onUnauthorized={becomeAnonymous}
          />
        ) : null}

        {view.name === 'businesses'
          || (view.name === 'business-detail' && !detailBusiness)
          || (view.name === 'branch-detail' && !detailBranch) ? (
          <BusinessesScreen
            businesses={businesses}
            users={users}
            loading={branchesLoading}
            error={branchesError}
            onOpenBusiness={(id) => setView({ name: 'business-detail', id })}
            onBusinessCreated={handleBusinessCreated}
            onUnauthorized={becomeAnonymous}
          />
        ) : null}

        {view.name === 'user-detail' && detailUserSummary ? (
          <UserDetailFetcher
            id={detailUserSummary.id}
            allBusinesses={businesses}
            onBack={() => setView({ name: 'users' })}
            onOpenActivity={() => setView({ name: 'activity', userId: detailUserSummary.id })}
            onUserUpdated={upsertUserInList}
            onSelfSessionInvalidated={becomeAnonymous}
            onUnauthorized={becomeAnonymous}
          />
        ) : null}

        {view.name === 'users' || (view.name === 'user-detail' && !detailUserSummary) ? (
          <UsersScreen
            users={users}
            loading={usersLoading}
            error={usersError}
            onOpenUser={(id) => setView({ name: 'user-detail', id })}
            onUserCreated={(created) => {
              upsertUserInList(created)
              void loadUsers()
            }}
            onUnauthorized={becomeAnonymous}
          />
        ) : null}

        {view.name === 'activity' ? (
          <ActivityScreen
            key={view.userId ?? 'all'}
            users={users}
            businesses={businesses}
            branches={branches}
            initialUserId={view.userId}
            onBackToUser={view.userId ? () => setView({ name: 'user-detail', id: view.userId! }) : undefined}
            onUnauthorized={becomeAnonymous}
          />
        ) : null}

        {view.name === 'subscription-notice' ? (
          <SubscriptionNoticeScreen onUnauthorized={becomeAnonymous} />
        ) : null}
      </main>

      <nav className="mobile-nav" aria-label="Navegación principal">
        <button
          type="button"
          className={view.name === 'businesses' || view.name === 'business-detail' || view.name === 'branch-detail' ? 'mobile-nav-item active' : 'mobile-nav-item'}
          onClick={() => setView({ name: 'businesses' })}
        >
          <Building2 size={20} aria-hidden="true" />
          <span>Negocios</span>
        </button>
        <button
          type="button"
          className={view.name === 'users' || view.name === 'user-detail' ? 'mobile-nav-item active' : 'mobile-nav-item'}
          onClick={() => setView({ name: 'users' })}
        >
          <UsersRound size={20} aria-hidden="true" />
          <span>Usuarios</span>
        </button>
        <button
          type="button"
          className={view.name === 'activity' ? 'mobile-nav-item active' : 'mobile-nav-item'}
          onClick={() => setView({ name: 'activity' })}
        >
          <Activity size={20} aria-hidden="true" />
          <span>Actividad</span>
        </button>
        <button
          type="button"
          className={view.name === 'subscription-notice' ? 'mobile-nav-item active' : 'mobile-nav-item'}
          onClick={() => setView({ name: 'subscription-notice' })}
        >
          <PanelsTopLeft size={20} aria-hidden="true" />
          <span>Avisos</span>
        </button>
      </nav>
    </div>
  )
}

/// UserDetailScreen necesita el detalle completo (con sucursales), no solo el resumen de la
/// lista: lo carga aquí y muestra un estado breve de espera mientras llega.
function UserDetailFetcher({
  id,
  allBusinesses,
  onBack,
  onOpenActivity,
  onUserUpdated,
  onSelfSessionInvalidated,
  onUnauthorized,
}: {
  id: string
  allBusinesses: Business[]
  onBack: () => void
  onOpenActivity: () => void
  onUserUpdated: (user: UserDetail) => void
  onSelfSessionInvalidated: (message: string) => void
  onUnauthorized: () => void
}) {
  const [detail, setDetail] = useState<UserDetail | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let active = true
    setDetail(null)
    setError(null)
    api.user(id)
      .then((loaded) => { if (active) setDetail(loaded) })
      .catch((reason: unknown) => {
        if (!active) return
        if (reason instanceof ApiError && reason.status === 401) return onUnauthorized()
        setError(reason instanceof Error ? reason.message : 'No fue posible cargar el usuario.')
      })
    return () => { active = false }
  }, [id, onUnauthorized])

  function handleUpdated(next: UserDetail) {
    setDetail(next)
    onUserUpdated(next)
  }

  if (error) return <p className="form-error" role="alert">{error}</p>
  if (!detail) return <p className="panel-hint">Cargando usuario…</p>

  return (
    <UserDetailScreen
      user={detail}
      allBusinesses={allBusinesses}
      onBack={onBack}
      onOpenActivity={onOpenActivity}
      onUserUpdated={handleUpdated}
      onSelfSessionInvalidated={onSelfSessionInvalidated}
      onUnauthorized={onUnauthorized}
    />
  )
}
