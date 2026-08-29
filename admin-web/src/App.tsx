import { useCallback, useEffect, useState } from 'react'
import { LogOut, ShieldCheck } from 'lucide-react'
import { api, ApiError } from './api'
import { LoginScreen } from './components/LoginScreen'
import { BranchDetailScreen } from './screens/BranchDetailScreen'
import { BranchesScreen } from './screens/BranchesScreen'
import { UserDetailScreen } from './screens/UserDetailScreen'
import { UsersScreen } from './screens/UsersScreen'
import type { AdminUser, Branch, UserDetail, UserSummary } from './types'

type SessionState = 'loading' | 'anonymous' | 'authenticated'
type View =
  | { name: 'branches' }
  | { name: 'branch-detail'; code: string }
  | { name: 'users' }
  | { name: 'user-detail'; id: string }

export function App() {
  const [sessionState, setSessionState] = useState<SessionState>('loading')
  const [user, setUser] = useState<AdminUser | null>(null)
  const [branches, setBranches] = useState<Branch[]>([])
  const [branchesLoading, setBranchesLoading] = useState(false)
  const [branchesError, setBranchesError] = useState<string | null>(null)
  const [users, setUsers] = useState<UserSummary[]>([])
  const [usersLoading, setUsersLoading] = useState(false)
  const [usersError, setUsersError] = useState<string | null>(null)
  const [view, setView] = useState<View>({ name: 'branches' })
  const [loginError, setLoginError] = useState<string | null>(null)
  const [loginBusy, setLoginBusy] = useState(false)

  const becomeAnonymous = useCallback((message?: string) => {
    setSessionState('anonymous')
    setUser(null)
    setBranches([])
    setUsers([])
    setView({ name: 'branches' })
    if (message) setLoginError(message)
  }, [])

  const loadBranches = useCallback(async () => {
    setBranchesLoading(true)
    setBranchesError(null)
    try {
      setBranches(await api.branches())
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
        branchCount: next.branches.length,
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

  const detailBranch = view.name === 'branch-detail' ? branches.find((b) => b.code === view.code) ?? null : null
  const detailUserSummary = view.name === 'user-detail' ? users.find((u) => u.id === view.id) ?? null : null

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <button className="sidebar-brand" type="button" onClick={() => setView({ name: 'branches' })}>
          <ShieldCheck size={22} /><span>Admin SaaS</span>
        </button>

        <nav className="sidebar-nav">
          <button
            type="button"
            className={view.name === 'branches' || view.name === 'branch-detail' ? 'nav-item active' : 'nav-item'}
            onClick={() => setView({ name: 'branches' })}
          >
            Sucursales
          </button>
          <button
            type="button"
            className={view.name === 'users' || view.name === 'user-detail' ? 'nav-item active' : 'nav-item'}
            onClick={() => setView({ name: 'users' })}
          >
            Usuarios
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
            onBack={() => setView({ name: 'branches' })}
            onBranchUpdated={upsertBranchInList}
            onUnauthorized={becomeAnonymous}
          />
        ) : null}

        {view.name === 'branches' || (view.name === 'branch-detail' && !detailBranch) ? (
          <BranchesScreen
            branches={branches}
            loading={branchesLoading}
            error={branchesError}
            onOpenBranch={(code) => setView({ name: 'branch-detail', code })}
            onBranchCreated={upsertBranchInList}
            onUnauthorized={becomeAnonymous}
          />
        ) : null}

        {view.name === 'user-detail' && detailUserSummary ? (
          <UserDetailFetcher
            id={detailUserSummary.id}
            allBranches={branches}
            onBack={() => setView({ name: 'users' })}
            onUserUpdated={upsertUserInList}
            onSelfSessionInvalidated={becomeAnonymous}
            onUnauthorized={becomeAnonymous}
          />
        ) : null}

        {view.name === 'users' || (view.name === 'user-detail' && !detailUserSummary) ? (
          <UsersScreen
            users={users}
            branches={branches}
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
      </main>
    </div>
  )
}

/// UserDetailScreen necesita el detalle completo (con sucursales), no solo el resumen de la
/// lista: lo carga aquí y muestra un estado breve de espera mientras llega.
function UserDetailFetcher({
  id,
  allBranches,
  onBack,
  onUserUpdated,
  onSelfSessionInvalidated,
  onUnauthorized,
}: {
  id: string
  allBranches: Branch[]
  onBack: () => void
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
      allBranches={allBranches}
      onBack={onBack}
      onUserUpdated={handleUpdated}
      onSelfSessionInvalidated={onSelfSessionInvalidated}
      onUnauthorized={onUnauthorized}
    />
  )
}
